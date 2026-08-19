// nullstore.cpp — generic repair for "engine stores through a null pointer".
//
// TWO CRASHES, ONE BUG CLASS
//
// Both faults captured so far are a script-VM command writing its result back through
// a pointer the engine computed as NULL, under the VM execution loop at 0x005DF460:
//
//   1) 0x0068FDA6  mov [ecx], eax    ecx = 0
//      0x0068F9E0 resolves three script references, records NULL for any that fail,
//      then dereferences all three unconditionally on both exit paths.
//
//   2) 0x005D99A4  mov [eax], esi    eax = 0
//      0x005D9960 is even blunter -- the compiler folded the failure branch into a
//      guaranteed fault:
//          005D9988  call 0x481a20        ; resolve the reference
//          005D9990  test eax, eax
//          005D9992  je   0x5D99A2        ; resolution failed
//          005D9994  mov  edx, [esp+6]
//          005D9998  mov  [eax+edx], esi  ; success: store into the object field
//          ...
//          005D99A2  xor  eax, eax        ; failure: destination pointer = NULL
//          005D99A4  mov  [eax], esi      ; ...and store through it anyway
//      That is source of the shape `dst = ok ? &obj->field : NULL; *dst = value;`
//      compiled faithfully. Any failed resolution here kills the process outright.
//
// Patching each site with its own code cave does not scale: the pattern is systemic,
// and every play session finds another one. So instead of guarding sites, this repairs
// the FAULT: when a store to the null page traps, the store is skipped and execution
// resumes at the next instruction.
//
// WHY THAT IS THE RIGHT SEMANTIC HERE. For both observed sites, skipping the store
// leaves the function doing exactly what its success path does apart from the write
// itself -- site 2 continues straight into `pop esi; add esp,8; ret` returning 0, the
// same value the success path returns. The engine wanted to write into an object that
// no longer exists; nothing needs that write to have happened.
//
// WHAT THIS IS NOT. It is not a licence to ignore faults. It is deliberately narrow:
//
//   * writes only, never reads -- a skipped read would leave a register holding
//     garbage, and that corrupts quietly instead of crashing loudly;
//   * the target address must be inside the null page (below 0x10000), so a wild
//     pointer into real memory still crashes and still gets a full report;
//   * the faulting instruction must be inside the game image;
//   * only plain MOV stores are decoded. Read-modify-write forms, string ops and
//     anything the decoder is not certain about are left to crash.
//
// Every repaired site is recorded with a hit count, and the first fault at each new
// site still produces a full crash report. The site table is the real deliverable:
// it is a map of every place the engine does this, which is what a proper fix needs.

#include "ckperf.h"
#include <stdio.h>

namespace ckperf {

namespace {

constexpr int  kMaxSites       = 64;
constexpr LONG kMaxPerSite     = 200000;   // beyond this a site is pathological, not incidental
constexpr uintptr_t kNullPage  = 0x10000;

struct Site {
    uintptr_t eip;
    LONG      hits;
    bool      reported;
};

Site         g_sites[kMaxSites];
volatile LONG g_siteCount = 0;
volatile LONG g_totalRepairs = 0;
CRITICAL_SECTION g_lock;
bool g_lockReady = false;

// ------------------------------------------------------------ instruction decoding
//
// Just enough x86 to measure the length of a plain MOV store. Anything unrecognised
// returns 0, which means "do not touch this fault".

int ModRmLength(const unsigned char* p, int opSizeOverride, int immBytes) {
    unsigned char modrm = p[0];
    int mod = modrm >> 6;
    int rm  = modrm & 7;
    if (mod == 3) return 0;         // register destination: cannot fault, not our business

    int len = 1;                    // the ModR/M byte itself
    bool hasSib = (rm == 4);
    if (hasSib) {
        len += 1;
        unsigned char base = p[1] & 7;
        if (mod == 0 && base == 5) len += 4;
    } else if (mod == 0 && rm == 5) {
        len += 4;                   // disp32-only addressing
    }
    if (mod == 1) len += 1;
    if (mod == 2) len += 4;

    if (immBytes == -1) immBytes = opSizeOverride ? 2 : 4;   // -1 means "operand sized"
    return len + immBytes;
}

// Returns the total instruction length, or 0 when this is not a store we will skip.
int StoreLength(const unsigned char* code, size_t avail) {
    size_t i = 0;
    int opSizeOverride = 0;

    // Prefixes. Address-size and repeat prefixes appear on instructions we do not
    // handle anyway, but they still have to be counted to reject cleanly.
    while (i < avail && i < 4) {
        unsigned char b = code[i];
        if (b == 0x66) { opSizeOverride = 1; ++i; continue; }
        if (b == 0x67 || b == 0xF2 || b == 0xF3) return 0;   // not decoded
        if (b == 0x26 || b == 0x2E || b == 0x36 || b == 0x3E || b == 0x64 || b == 0x65) { ++i; continue; }
        break;
    }
    if (i >= avail) return 0;

    unsigned char op = code[i];
    size_t modrmAt = i + 1;
    if (modrmAt >= avail) return 0;

    int body = 0;
    switch (op) {
        case 0x88:  // mov r/m8, r8
        case 0x89:  // mov r/m16/32, r16/32
            body = ModRmLength(code + modrmAt, opSizeOverride, 0);
            break;
        case 0xC6:  // mov r/m8, imm8
            if ((code[modrmAt] >> 3 & 7) != 0) return 0;
            body = ModRmLength(code + modrmAt, opSizeOverride, 1);
            break;
        case 0xC7:  // mov r/m16/32, imm16/32
            if ((code[modrmAt] >> 3 & 7) != 0) return 0;
            body = ModRmLength(code + modrmAt, opSizeOverride, -1);
            break;
        default:
            return 0;
    }
    if (body == 0) return 0;
    return (int)(i + 1 + body);
}

} // namespace

// The self-test executes a real null store from a scratch page. That page is not the
// game image, so the module check would normally reject it; this address is the one
// documented exception, and it is cleared the moment the test finishes.
static volatile uintptr_t g_selfTestStoreEip = 0;

void NullStoreInit() {
    InitializeCriticalSection(&g_lock);
    g_lockReady = true;
}

bool NullStoreSelfTest() {
    if (!g_cfg.nullStoreRepair) return true;

    // xor eax, eax ; mov [eax], ecx ; ret
    //
    // Deliberately the exact shape of the second observed crash, so the thing being
    // proven is the thing that will run in the game: the decoder measures `89 08` as
    // two bytes, the handler resumes at the `ret`, and this function returns normally
    // instead of taking the process down.
    const unsigned char stub[] = { 0x33, 0xC0, 0x89, 0x08, 0xC3 };
    void* page = VirtualAlloc(nullptr, 4096, MEM_COMMIT | MEM_RESERVE, PAGE_EXECUTE_READWRITE);
    if (!page) {
        Logf("null-store repair: self-test could not allocate a scratch page; repair left ENABLED "
             "but unverified.");
        return false;
    }
    memcpy(page, stub, sizeof(stub));
    FlushInstructionCache(GetCurrentProcess(), page, sizeof(stub));

    g_selfTestStoreEip = (uintptr_t)page + 2;      // the store instruction
    LONG before = NullStoreCount();

    bool survived = true;
    __try {
        ((void(*)())page)();
    }
    __except (EXCEPTION_EXECUTE_HANDLER) {
        survived = false;
    }

    g_selfTestStoreEip = 0;
    LONG after = NullStoreCount();
    VirtualFree(page, 0, MEM_RELEASE);

    // Start the session's counter from zero so the number the user sees is entirely
    // the engine's doing.
    InterlockedExchange(&g_totalRepairs, 0);

    if (survived && after == before + 1) {
        Logf("null-store repair: self-test passed -- a real null store was decoded, skipped, "
             "and execution resumed correctly.");
        return true;
    }

    // Verification failed, so the mechanism is not trustworthy on this machine. Better
    // to lose the repair than to resume execution at an address we computed wrongly.
    g_cfg.nullStoreRepair = false;
    Logf("null-store repair: self-test FAILED (survived=%d, count %ld -> %ld). Repair has been "
         "DISABLED for this session; faults will be reported but not repaired.",
         survived ? 1 : 0, before, after);
    return false;
}

long NullStoreCount() { return InterlockedCompareExchange(&g_totalRepairs, 0, 0); }

// True when this eip has not been seen before, so the caller knows to write one full
// crash report for it. Called only from inside the exception handler.
static bool RecordSite(uintptr_t eip, bool& firstTime) {
    firstTime = false;
    if (!g_lockReady) return false;

    EnterCriticalSection(&g_lock);
    Site* found = nullptr;
    LONG n = g_siteCount;
    for (LONG i = 0; i < n; ++i) {
        if (g_sites[i].eip == eip) { found = &g_sites[i]; break; }
    }
    if (!found) {
        if (n >= kMaxSites) { LeaveCriticalSection(&g_lock); return false; }
        found = &g_sites[n];
        found->eip = eip;
        found->hits = 0;
        found->reported = false;
        g_siteCount = n + 1;
        firstTime = true;
    }
    bool allowed = found->hits < kMaxPerSite;
    if (allowed) ++found->hits;
    LeaveCriticalSection(&g_lock);
    return allowed;
}

bool NullStoreTryRepair(EXCEPTION_POINTERS* ep, bool& firstTimeAtThisSite, unsigned& resumeEip) {
    // The context is deliberately NOT modified here. The caller may still want to
    // write a crash report, and a report describing an already-advanced eip would
    // point at the wrong instruction. The caller applies resumeEip when it is done.
    firstTimeAtThisSite = false;
    resumeEip = 0;
    if (!g_cfg.nullStoreRepair) return false;

    EXCEPTION_RECORD* er = ep->ExceptionRecord;
    if (er->ExceptionCode != EXCEPTION_ACCESS_VIOLATION) return false;
    if (er->NumberParameters < 2) return false;
    if (er->ExceptionInformation[0] != 1) return false;                  // writes only
    uintptr_t target = (uintptr_t)er->ExceptionInformation[1];
    if (target >= kNullPage) return false;                               // null page only

    uintptr_t eip = ep->ContextRecord->Eip;
    if (eip != g_selfTestStoreEip) {
        const ModuleEntry* m = ModuleForAddress(eip);
        const ModuleEntry* game = GameModule();
        if (!m || !game || m->base != game->base) return false;          // game code only
    }

    unsigned char code[16];
    if (!SafeRead(eip, code, sizeof(code))) return false;
    int len = StoreLength(code, sizeof(code));
    if (len <= 0 || len > 15) return false;

    // The self-test is not a finding. It must not consume a crash-report slot, and it
    // must not appear in the site table -- that table is supposed to be a map of the
    // engine's real bugs, not of our own verification.
    if (eip != g_selfTestStoreEip) {
        if (!RecordSite(eip, firstTimeAtThisSite)) return false;
    }

    InterlockedIncrement(&g_totalRepairs);
    resumeEip = (unsigned)(eip + len);
    return true;
}

void NullStoreLogSites() {
    if (!g_lockReady) return;
    EnterCriticalSection(&g_lock);
    LONG n = g_siteCount;
    for (LONG i = 0; i < n; ++i) {
        char d[160];
        DescribeAddress(g_sites[i].eip, d, sizeof(d));
        Logf("null-store site  0x%08X  %s  x%ld", (unsigned)g_sites[i].eip, d, g_sites[i].hits);
    }
    LeaveCriticalSection(&g_lock);
}

int NullStoreDescribeSites(char* buf, int cap, int pos) {
    if (!g_lockReady) return pos;
    EnterCriticalSection(&g_lock);
    LONG n = g_siteCount;
    if (n > 0) {
        pos = Append(buf, cap, pos, "\r\n  null stores skipped so far, by site\r\n");
        for (LONG i = 0; i < n; ++i) {
            char d[160];
            DescribeAddress(g_sites[i].eip, d, sizeof(d));
            pos = Append(buf, cap, pos, "    0x%08X  %-32s  x%ld\r\n",
                         (unsigned)g_sites[i].eip, d, g_sites[i].hits);
        }
    }
    LeaveCriticalSection(&g_lock);
    return pos;
}

} // namespace ckperf
