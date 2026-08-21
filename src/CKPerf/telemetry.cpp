// telemetry.cpp — background sampler for the numbers that only matter over time.
//
// The point of this thread is to settle one question cheaply on the very first play
// session: does the process die because of a bad pointer, or because it ran out of
// address space? Those two have completely different fixes, and guessing between them
// has already cost this project time. A memory curve that climbs steadily toward the
// LAA ceiling and then stops answers it without any reverse engineering at all.
//
// It also drains the frame counters, so one log line per second carries both the
// resource picture and the framerate picture side by side -- which is what lets a
// hitch be matched against a spike in commit.

#include "ckperf.h"
#include <stdio.h>

namespace ckperf {

static HANDLE        g_thread = nullptr;
static HANDLE        g_stop   = nullptr;
static volatile LONG g_peakPrivateMb = 0;

// ------------------------------------------------------------- live object census
//
// The engine keeps every handle-addressable object in one flat table: 0x00481A20 is
// nothing but `return table[handle & 0xFFFF]`, and 0x00481A40 clears a slot when the
// object dies. So counting the non-empty slots of that table gives the live object
// count for free, with no extra reverse engineering and no per-frame cost.
//
// This is exactly the number worth having next to the crashes. Every fault this tool
// has repaired was a script writing through a handle whose slot had just been cleared,
// so it is not only "how big is the battle" -- the DEATHS figure below is the direct
// driver of how many stale references are in flight at any moment.
//
// It counts all handle-managed objects, not units alone: buildings, projectiles and
// effects share the table. Treat it as battle scale rather than an army roster.

constexpr uintptr_t kHandleTable  = 0x00798CB8;
constexpr int       kHandleSlots  = 0x10000;

static unsigned char* g_prevOccupied = nullptr;   // one bit per slot, for birth/death deltas
static bool           g_censusUsable = false;
static LONG           g_peakLive = 0;
static volatile LONG  g_lastLive = 0;

static void CensusInit() {
    // The table lives in the game's .data, so it is only there once the image is loaded.
    // Verified rather than assumed: a wrong address here would report convincing nonsense.
    // Walk the whole span rather than demanding one contiguous region. The first
    // attempt required a single VirtualQuery region to cover all 256 KB and refused
    // the table outright, because .data is split into several regions by page
    // protection -- the memory was there all along, the check was simply too strict.
    uintptr_t p   = kHandleTable;
    uintptr_t end = kHandleTable + (uintptr_t)kHandleSlots * 4;
    while (p < end) {
        MEMORY_BASIC_INFORMATION mbi;
        if (!VirtualQuery((LPCVOID)p, &mbi, sizeof(mbi)) || mbi.State != MEM_COMMIT ||
            (mbi.Protect & PAGE_GUARD) || (mbi.Protect & 0xFF) == PAGE_NOACCESS) {
            Logf("object census: handle table at 0x%08X is not fully readable (stopped at "
                 "0x%08X); live counts unavailable.", (unsigned)kHandleTable, (unsigned)p);
            return;
        }
        uintptr_t next = (uintptr_t)mbi.BaseAddress + mbi.RegionSize;
        if (next <= p) return;
        p = next;
    }
    g_prevOccupied = (unsigned char*)VirtualAlloc(nullptr, kHandleSlots / 8,
                                                  MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
    g_censusUsable = g_prevOccupied != nullptr;
    if (g_censusUsable) {
        Logf("object census: watching the %d-slot handle table at 0x%08X.",
             kHandleSlots, (unsigned)kHandleTable);
    }
}

// Counts occupied slots and, by diffing against the previous sample, how many objects
// were created and destroyed in between.
static void CensusSample(int& live, int& born, int& died) {
    live = born = died = 0;
    if (!g_censusUsable) return;

    const uint32_t* table = (const uint32_t*)kHandleTable;
    for (int i = 0; i < kHandleSlots; ++i) {
        bool occupied = table[i] != 0;
        unsigned char mask = (unsigned char)(1u << (i & 7));
        unsigned char& bits = g_prevOccupied[i >> 3];
        bool wasOccupied = (bits & mask) != 0;

        if (occupied) {
            ++live;
            if (!wasOccupied) { ++born; bits |= mask; }
        } else if (wasOccupied) {
            ++died;
            bits = (unsigned char)(bits & ~mask);
        }
    }
    if (live > g_peakLive) g_peakLive = live;
    InterlockedExchange(&g_lastLive, live);
}

struct AddressSpace {
    SIZE_T freeTotal;
    SIZE_T freeLargest;
    SIZE_T committed;
};

static AddressSpace ScanAddressSpace() {
    AddressSpace a = {};
    SYSTEM_INFO si;
    GetSystemInfo(&si);
    uintptr_t p  = (uintptr_t)si.lpMinimumApplicationAddress;
    uintptr_t hi = (uintptr_t)si.lpMaximumApplicationAddress;
    MEMORY_BASIC_INFORMATION mbi;
    while (p < hi && VirtualQuery((LPCVOID)p, &mbi, sizeof(mbi))) {
        if (mbi.State == MEM_FREE) {
            a.freeTotal += mbi.RegionSize;
            if (mbi.RegionSize > a.freeLargest) a.freeLargest = mbi.RegionSize;
        } else if (mbi.State == MEM_COMMIT) {
            a.committed += mbi.RegionSize;
        }
        uintptr_t next = (uintptr_t)mbi.BaseAddress + mbi.RegionSize;
        if (next <= p) break;
        p = next;
    }
    return a;
}

static DWORD WINAPI TelemetryThread(LPVOID) {
    int tick = 0;
    long lastSuppressed = 0;
    CensusInit();
    const double period = g_cfg.telemetryMs / 1000.0;

    // The engine loads content packs lazily, so the module list is not final at
    // startup; refresh it a few times early, then only occasionally.
    for (;;) {
        DWORD w = WaitForSingleObject(g_stop, (DWORD)g_cfg.telemetryMs);
        if (w == WAIT_OBJECT_0) break;
        ++tick;

        if (tick <= 5 || (tick % 30) == 0) ModulesRefresh();

        PROCESS_MEMORY_COUNTERS_EX pmc = {};
        pmc.cb = sizeof(pmc);
        unsigned privMb = 0, wsMb = 0;
        if (K32GetProcessMemoryInfo(GetCurrentProcess(), (PROCESS_MEMORY_COUNTERS*)&pmc, sizeof(pmc))) {
            privMb = (unsigned)(pmc.PrivateUsage  >> 20);
            wsMb   = (unsigned)(pmc.WorkingSetSize >> 20);
            if ((LONG)privMb > g_peakPrivateMb) g_peakPrivateMb = (LONG)privMb;
        }

        // The full address-space walk is the expensive part, so it runs every fifth
        // sample. Fragmentation does not change fast enough for that to hide anything.
        if ((tick % 5) == 0) {
            AddressSpace a = ScanAddressSpace();
            Logf("mem: private %u MB | working set %u MB | committed %u MB | free %u MB | largest free block %u MB",
                 privMb, wsMb,
                 (unsigned)(a.committed   >> 20),
                 (unsigned)(a.freeTotal   >> 20),
                 (unsigned)(a.freeLargest >> 20));
            if (a.freeLargest < (32u << 20)) {
                Logf("mem: WARNING largest free block is under 32 MB -- the process is close to "
                     "exhausting contiguous address space. A crash from here on is an allocation "
                     "failure, not a bad pointer.");
            }
        } else {
            Logf("mem: private %u MB | working set %u MB", privMb, wsMb);
        }

        // Battle scale first: it is the variable everything else in this log is a
        // function of, so it belongs on its own line every single sample.
        int live = 0, born = 0, died = 0;
        CensusSample(live, born, died);
        if (g_censusUsable) {
            Logf("objects: %d live (peak %ld) | +%d born, -%d died since the last sample",
                 live, g_peakLive, born, died);
        }

        FrameTimingDrainAndLog(period);

        // Reported only when it moves. A rising count is direct evidence that the
        // engine is trying to write through null script references -- i.e. that the
        // guard is the thing keeping this session alive.
        long suppressed = GuardSuppressedCount();
        if (suppressed != lastSuppressed) {
            Logf("guard: suppressed %ld null write-backs so far (+%ld since the last sample)",
                 suppressed, suppressed - lastSuppressed);
            lastSuppressed = suppressed;
        }

        // Same only-when-it-moves discipline as the guard counter above: the sidecar own
        // redirect-hit counters, so a session log shows which of the 31 redirected sites
        // actually fired without repeating 32 unchanged numbers every second.
        HighResolutionLogHitCounts();

        // Measures real CVXVisible rectangle generation against the 127-slot sidecar capacity
        // to detect whether live gameplay ever overflows the consumer loop cap.
        HighResolutionLogVisibleCount();
    }

    Logf("telemetry stopped. peak private bytes %d MB, peak live objects %ld.",
         (int)g_peakPrivateMb, g_peakLive);
    return 0;
}

// Exposed so a crash report can state the battle scale at the moment of the fault --
// the single most useful piece of context for a bug that only shows up when the map is
// busy. Returns -1 when the census is not running.
long CensusLiveObjects() { return g_censusUsable ? InterlockedCompareExchange(&g_lastLive, 0, 0) : -1; }
long CensusPeakObjects() { return g_censusUsable ? g_peakLive : -1; }

void TelemetryStart() {
    if (!g_cfg.telemetry) return;
    g_stop = CreateEventW(nullptr, TRUE, FALSE, nullptr);
    g_thread = CreateThread(nullptr, 0, TelemetryThread, nullptr, 0, nullptr);
    if (g_thread) Logf("telemetry started (every %d ms)", g_cfg.telemetryMs);
}

void TelemetryStop() {
    if (g_stop) SetEvent(g_stop);
    if (g_thread) {
        WaitForSingleObject(g_thread, 2000);
        CloseHandle(g_thread);
        g_thread = nullptr;
    }
    if (g_stop) { CloseHandle(g_stop); g_stop = nullptr; }
}

} // namespace ckperf
