// vmlvalue.cpp -- narrow repair for corrupted script-VM assignment lvalues.
//
// The VM represents an lvalue as six packed bytes:
//
//     { uint16_t objectId; uint32_t byteOffset; }
//
// Two independent field sessions captured the same corruption pattern in the byte
// assignment handler at 0x005D9BB0:
//
//   2026-08-22: id resolved, offset 0x4A8800E4 -> write AV at 0x5DCB10AC
//   2026-08-23: id 0x00DA, offset 0x428800F6 -> write AV at 0x5886E3B6
//
// The low halves (0x00E4 / 0x00F6) look like real object-field offsets; the high halves
// are stale data. Guessing a maximum legal offset would risk suppressing a legitimate
// assignment. This repair therefore waits for Windows to prove the destination invalid:
// it acts only on a write AV at one of eight exact, byte-verified assignment stores.
// Valid stores never fault and never enter this code.

#include "ckperf.h"

namespace ckperf {

namespace {

enum class RepairKind { SkipStore, RedirectEax };
enum class TargetKind { Eax, EaxPlusEdx };

struct Site {
    uintptr_t eip;
    unsigned char bytes[3];
    unsigned char length;
    RepairKind repair;
    TargetKind target;
    volatile LONG hits;
};

// Success-path stores and their null-path siblings. The four RedirectEax sites begin
// multi-store sequences; redirecting EAX lets the whole sequence finish against an
// isolated zero-filled page and preserves its original pop/epilogue discipline.
Site g_sites[] = {
    { 0x005D9998, {0x89,0x34,0x10}, 3, RepairKind::SkipStore,   TargetKind::EaxPlusEdx, 0 },
    { 0x005D99A4, {0x89,0x30,0x00}, 2, RepairKind::SkipStore,   TargetKind::Eax,        0 },
    { 0x005D9BE6, {0x88,0x1C,0x10}, 3, RepairKind::SkipStore,   TargetKind::EaxPlusEdx, 0 },
    { 0x005D9BF2, {0x88,0x18,0x00}, 2, RepairKind::SkipStore,   TargetKind::Eax,        0 },
    { 0x005DB1AA, {0x89,0x30,0x00}, 2, RepairKind::RedirectEax, TargetKind::Eax,        0 },
    { 0x005DB458, {0x89,0x18,0x00}, 2, RepairKind::RedirectEax, TargetKind::Eax,        0 },
    { 0x005DB68E, {0x89,0x30,0x00}, 2, RepairKind::RedirectEax, TargetKind::Eax,        0 },
    { 0x005DB69D, {0x89,0x30,0x00}, 2, RepairKind::RedirectEax, TargetKind::Eax,        0 },
};

constexpr size_t kSiteCount = sizeof(g_sites) / sizeof(g_sites[0]);
constexpr size_t kScratchStride = 4096;

unsigned char* g_scratch = nullptr;
volatile LONG g_total = 0;
bool g_enabled = false;

Site* FindSite(uintptr_t eip) {
    for (size_t i = 0; i < kSiteCount; ++i) {
        if (g_sites[i].eip == eip) return &g_sites[i];
    }
    return nullptr;
}

uintptr_t ExpectedTarget(const Site& site, const CONTEXT& cx) {
    if (site.target == TargetKind::EaxPlusEdx) {
        return (uintptr_t)(DWORD)(cx.Eax + cx.Edx); // x86 address arithmetic wraps at 32 bits
    }
    return (uintptr_t)cx.Eax;
}

bool VerifySiteBytes(const Site& site) {
    unsigned char actual[3] = {};
    if (!SafeRead(site.eip, actual, site.length)) return false;
    return memcmp(actual, site.bytes, site.length) == 0;
}

bool RunSelfTest() {
    // VmLvalueTryRepair validates the actual Steam instructions, target equation,
    // context mutation, resume EIP, and first-hit accounting for every registered site.
    for (size_t i = 0; i < kSiteCount; ++i) {
        Site& site = g_sites[i];
        CONTEXT cx = {};
        cx.Eip = (DWORD)site.eip;
        cx.Eax = 0x12340000u;
        cx.Edx = 0x00005678u;

        EXCEPTION_RECORD er = {};
        er.ExceptionCode = EXCEPTION_ACCESS_VIOLATION;
        er.NumberParameters = 2;
        er.ExceptionInformation[0] = 1; // write
        er.ExceptionInformation[1] = ExpectedTarget(site, cx);
        EXCEPTION_POINTERS ep = { &er, &cx };

        bool first = false;
        unsigned resume = 0;
        if (!VmLvalueTryRepair(&ep, first, resume) || !first) return false;

        if (site.repair == RepairKind::SkipStore) {
            if (resume != site.eip + site.length) return false;
            if (cx.Eax != 0x12340000u || cx.Edx != 0x00005678u) return false;
        } else {
            uintptr_t expectedScratch = (uintptr_t)g_scratch + i * kScratchStride;
            if (resume != site.eip || cx.Eax != (DWORD)expectedScratch) return false;
        }
    }

    for (size_t i = 0; i < kSiteCount; ++i) InterlockedExchange(&g_sites[i].hits, 0);
    InterlockedExchange(&g_total, 0);
    return true;
}

} // namespace

void VmLvalueInit() {
    if (!g_cfg.nullStoreRepair) return;

    const ModuleEntry* game = GameModule();
    if (!game || game->base != 0x00400000) {
        Logf("vm lvalue repair: game module/build identity unavailable; repair disabled.");
        return;
    }

    for (size_t i = 0; i < kSiteCount; ++i) {
        if (!VerifySiteBytes(g_sites[i])) {
            Logf("vm lvalue repair: original bytes mismatch at 0x%08X; repair disabled.",
                 (unsigned)g_sites[i].eip);
            return;
        }
    }

    g_scratch = (unsigned char*)VirtualAlloc(nullptr, kSiteCount * kScratchStride,
                                             MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
    if (!g_scratch) {
        Logf("vm lvalue repair: scratch allocation failed; repair disabled.");
        return;
    }

    g_enabled = true;
    if (!RunSelfTest()) {
        g_enabled = false;
        Logf("vm lvalue repair: self-test FAILED; repair disabled for this session.");
        return;
    }

    Logf("vm lvalue repair: self-test passed -- %u exact assignment stores verified; "
         "invalid destinations will be suppressed only after a real write AV.",
         (unsigned)kSiteCount);
}

bool VmLvalueTryRepair(EXCEPTION_POINTERS* ep, bool& firstTimeAtThisSite, unsigned& resumeEip) {
    firstTimeAtThisSite = false;
    resumeEip = 0;
    if (!g_enabled || !ep || !ep->ExceptionRecord || !ep->ContextRecord) return false;

    EXCEPTION_RECORD* er = ep->ExceptionRecord;
    CONTEXT* cx = ep->ContextRecord;
    if (er->ExceptionCode != EXCEPTION_ACCESS_VIOLATION || er->NumberParameters < 2) return false;
    if (er->ExceptionInformation[0] != 1) return false; // write AV only

    Site* site = FindSite((uintptr_t)cx->Eip);
    if (!site || !VerifySiteBytes(*site)) return false;

    const ModuleEntry* game = GameModule();
    const ModuleEntry* owner = ModuleForAddress(site->eip);
    if (!game || game->base != 0x00400000 || !owner || owner->base != game->base) return false;

    uintptr_t reportedTarget = (uintptr_t)er->ExceptionInformation[1];
    if (reportedTarget != ExpectedTarget(*site, *cx)) return false;

    size_t index = (size_t)(site - g_sites);
    if (site->repair == RepairKind::SkipStore) {
        resumeEip = (unsigned)(site->eip + site->length);
    } else {
        cx->Eax = (DWORD)((uintptr_t)g_scratch + index * kScratchStride);
        resumeEip = (unsigned)site->eip; // re-execute the multi-store sequence on scratch
    }

    LONG hits = InterlockedIncrement(&site->hits);
    firstTimeAtThisSite = hits == 1;
    InterlockedIncrement(&g_total);
    return true;
}

long VmLvalueRepairCount() { return InterlockedCompareExchange(&g_total, 0, 0); }

void VmLvalueLogSites() {
    for (size_t i = 0; i < kSiteCount; ++i) {
        LONG hits = InterlockedCompareExchange(&g_sites[i].hits, 0, 0);
        if (hits > 0) Logf("vm lvalue site  0x%08X  x%ld", (unsigned)g_sites[i].eip, hits);
    }
}

int VmLvalueDescribeSites(char* buf, int cap, int pos) {
    bool any = false;
    for (size_t i = 0; i < kSiteCount; ++i) {
        LONG hits = InterlockedCompareExchange(&g_sites[i].hits, 0, 0);
        if (!hits) continue;
        if (!any) {
            pos = Append(buf, cap, pos, "\r\n  invalid VM lvalue stores repaired so far, by site\r\n");
            any = true;
        }
        pos = Append(buf, cap, pos, "    0x%08X  x%ld\r\n", (unsigned)g_sites[i].eip, hits);
    }
    return pos;
}

} // namespace ckperf
