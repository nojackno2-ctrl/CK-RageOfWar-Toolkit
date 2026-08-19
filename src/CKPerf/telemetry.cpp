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
    }

    Logf("telemetry stopped. peak private bytes this session: %d MB", (int)g_peakPrivateMb);
    return 0;
}

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
