// hires.cpp -- CVXVisible repairs for native resolutions above the engine's design limits.
//
// Two independent repairs live here, one per axis of the same structure:
//
//   * ROW axis (the crash): the 75 inline 16-byte slots overflow into the object tail
//     once the viewport is taller than 75 * 16 = 1200 px. Repaired by redirecting every
//     slot-address calculation to an external sidecar array.
//   * COLUMN axis (the scrolling smear): one slot is 4 dwords = 128 bits and one bit is
//     one 16x16-pixel cell, so the grid is only 128 * 16 = 2048 px wide and no column at
//     x >= 2048 can ever be marked dirty or repainted. Repaired by kCellSites, which
//     enlarges the cell to 32 px so the same 128 x 75 grid covers 4096 x 2400 px.
//
// CVXVisible embeds 75 16-byte visibility slots at this+0x10..this+0x4BF.
// Live bounds and three owning containers immediately follow at +0x4C0..+0x50F,
// so writing slot 75 corrupts the object. A producer clamp prevents the crash but
// is not correct: consumers still need every viewport column and display repeated
// tiles after camera scrolling.
//
// Moving the tail would require rewriting hundreds of unrelated call sites. This
// runtime-only patch instead redirects every verified slot-address calculation to
// an external sidecar array sized dynamically at runtime from the configured width,
// while leaving the CVXVisible layout untouched. The existing consumer bounds checks
// are replaced with detour caves performing cmp reg, imm32 against the runtime-derived
// slot count, and the normal per-frame clear is redirected to clear the entire sidecar.
// Every changed instruction is checked against the Steam build before any byte is
// written; the patch disappears with the process.

#include "ckperf.h"
#include <stdio.h>
#include <stdint.h>
#include <string.h>
#include <tlhelp32.h>

namespace ckperf {
extern "C" __declspec(dllexport) volatile LONG CKPerfHighResolutionStep = 0;
extern "C" __declspec(dllexport) volatile LONG CKPerfRedirectHits[38] = {};
namespace {

constexpr uintptr_t kCapacityImmediate = 0x00456A84;
constexpr unsigned  kSlotBytes = 16;
constexpr unsigned  kMaxSupportedWidth = 3840;
constexpr uintptr_t kTextStart = 0x00478000;
constexpr size_t    kTextLength = 0x00004000;

struct RedirectSpec {
    uintptr_t site;
    const char* originalHex; // span ends with the two-byte `add indexReg,thisReg`
};

// Verified with dumpbin against the Steam executable. The middle instructions in
// longer spans are replayed byte-for-byte in the cave before the base ADD changes.
// A JMP (not CALL) preserves every original stack offset and push order.
constexpr RedirectSpec kRedirects[] = {
    {0x004788D2, "C1E6045203F7"},
    {0x0047891C, "C1E204894C242403D7"},
    {0x00478A3A, "C1E6045103F7"},
    {0x00478A86, "C1E10403CF"},
    {0x00479767, "C1E00403C6"},
    {0x00479789, "C1E10403CE"},
    {0x004798EA, "C1E00403C6"},
    {0x0047990C, "C1E20403D6"},
    {0x00479981, "C1E00403C6"},
    {0x004799A3, "C1E10403CE"},
    {0x00479A13, "C1E00403C6"},
    {0x00479A35, "C1E10403CE"},
    {0x00479AA5, "C1E00403C6"},
    {0x00479AC7, "C1E10403CE"},
    {0x00479B7C, "C1E00403C6"},
    {0x00479B9E, "C1E10403CE"},
    {0x0047A0A3, "C1E60403F5"},
    {0x0047A44C, "C1E10403CF"},
    {0x0047B070, "C1E00403C6"},
    {0x0047B08F, "C1E20403D6"},
    {0x0047B125, "C1E00403C6"},
    {0x0047B144, "C1E20403D6"},
    {0x0047B1CB, "C1E00403C6"},
    {0x0047B1EA, "C1E20403D6"},
    {0x0047B27F, "C1E00403C6"},
    {0x0047B29E, "C1E20403D6"},
    {0x0047B394, "C1E6045203F7"},
    {0x0047B3DE, "C1E204894C242403D7"},
};

constexpr uintptr_t kClearSite = 0x004796F0;
constexpr unsigned char kClearOriginal[] = {
    0xB9,0x2C,0x01,0x00,0x00,       // mov ecx,300 dwords
    0x8D,0x7E,0x10                  // lea edi,[esi+10h]
};

// These consumers combine the object base in memory operands instead of ending
// in a two-byte ADD, so each uses a dedicated cave.
constexpr uintptr_t kMergeSite1 = 0x0047A91D;
constexpr unsigned char kMergeOriginal1[] = {
    0x40,                            // inc eax
    0xC1,0xE0,0x04,                 // shl eax,4
    0x8B,0x0C,0x18,                 // mov ecx,[eax+ebx]
    0x8B,0x74,0x18,0x08,            // mov esi,[eax+ebx+8]
    0x03,0xC3                       // add eax,ebx
};
constexpr uintptr_t kMergeResume1 = 0x0047A92A;

constexpr uintptr_t kMergeSite2 = 0x0047AAB2;
constexpr unsigned char kMergeOriginal2[] = {
    0x47,                            // inc edi
    0x8B,0xC7,                      // mov eax,edi
    0xC1,0xE0,0x04,                 // shl eax,4
    0x8B,0x0C,0x30,                 // mov ecx,[eax+esi]
    0x03,0xC6                       // add eax,esi
};
constexpr uintptr_t kMergeResume2 = 0x0047AABD;

constexpr uintptr_t kMergeSite3 = 0x0047A49E;
constexpr unsigned char kMergeOriginal3[] = {
    0xC1,0xE1,0x04,                 // shl ecx,4
    0x8D,0x5C,0x11,0x18             // lea ebx,[ecx+edx+0x18]
};
constexpr uintptr_t kMergeResume3 = 0x0047A4A5;

// Consumer limit check sites: replacing stock `cmp reg, imm8` (75 or 127) with `cmp reg, imm32`
// caves against g_sidecarSlots or g_columnCap to support high resolutions up to 4K (3840x2160).
constexpr uintptr_t kLimitSiteA = 0x0047A097;
constexpr unsigned char kLimitOriginalA[] = {
    0x83,0xFE,0x4B,                 // cmp esi,4Bh
    0x8B,0xFE                       // mov edi,esi
};
constexpr uintptr_t kLimitResumeA = 0x0047A09C;

constexpr uintptr_t kLimitSiteB = 0x0047A0C3;
constexpr unsigned char kLimitOriginalB[] = {
    0x83,0xFF,0x4B,                 // cmp edi,4Bh
    0x7D,0x49                       // jge short 0x0047A111
};
constexpr uintptr_t kLimitBranchB = 0x0047A111;
constexpr uintptr_t kLimitResumeB = 0x0047A0C8;

constexpr uintptr_t kLimitSiteC = 0x0047A115;
constexpr unsigned char kLimitOriginalC[] = {
    0x83,0xFF,0x4B,                 // cmp edi,4Bh
    0x0F,0x84,0x76,0x07,0x00,0x00   // je near 0x0047A894
};
constexpr uintptr_t kLimitBranchC = 0x0047A894;
constexpr uintptr_t kLimitResumeC = 0x0047A11E;

constexpr uintptr_t kLimitSiteD = 0x0047A489;
constexpr unsigned char kLimitOriginalD[] = {
    0x83,0xF9,0x4B,                 // cmp ecx,4Bh
    0x89,0x5C,0x24,0x40             // mov [esp+40h],ebx
};
constexpr uintptr_t kLimitResumeD = 0x0047A490;

constexpr uintptr_t kLimitSiteE = 0x0047A7A4;
constexpr unsigned char kLimitOriginalE[] = {
    0x83,0xF9,0x4B,                 // cmp ecx,4Bh
    0x89,0x4C,0x24,0x10             // mov [esp+10h],ecx
};
constexpr uintptr_t kLimitResumeE = 0x0047A7AB;

constexpr uintptr_t kLimitSiteF = 0x0047A122;
constexpr unsigned char kLimitOriginalF[] = {
    0x83,0xFE,0x7F,                 // cmp esi,7Fh
    0x89,0x7C,0x24,0x24             // mov [esp+24h],edi
};
constexpr uintptr_t kLimitResumeF = 0x0047A129;

// 2048x2048 Block Surface constructor argument patch sites:
// Site 1 (0x00479E49 / 0x00479E4E): Master surface constructor in 0x00479DC0.
// Pushes height argument (0x00479E49) and width argument (0x00479E4E) to ctor 0x0047D9E0.
constexpr uintptr_t kSurfaceCtorHeightSite1 = 0x00479E49;
constexpr uintptr_t kSurfaceCtorWidthSite1  = 0x00479E4E;
constexpr unsigned char kSurfaceCtorOriginal1[] = {
    0x68, 0x00, 0x08, 0x00, 0x00,  // push 800h (height)
    0x68, 0x00, 0x08, 0x00, 0x00   // push 800h (width)
};

// Site 2 (0x004780EA / 0x004780EF): Temporary shadow surface constructor in 0x004780D0.
// Pushes height argument (0x004780EA) and width argument (0x004780EF) to ctor 0x004784E0.
constexpr uintptr_t kSurfaceCtorHeightSite2 = 0x004780EA;
constexpr uintptr_t kSurfaceCtorWidthSite2  = 0x004780EF;
constexpr unsigned char kSurfaceCtorOriginal2[] = {
    0x68, 0x00, 0x08, 0x00, 0x00,  // push 800h (height)
    0x68, 0x00, 0x08, 0x00, 0x00   // push 800h (width)
};

// ------------------------------------------------------- dirty-cell size (16 -> 32 px)
//
// CVXVisible+0x10 is a 75-row x 128-column bit grid. One row is one 16-byte slot,
// i.e. 4 dwords = 128 bits, and one bit is one 16x16-pixel screen cell. 128 * 16 is
// exactly 2048, so no screen column at x >= 2048 has a bit to be marked dirty with,
// is therefore never repainted, and smears as the camera scrolls. This is the whole
// cause of the 2560x1440 smear; see docs/reverse-engineering-notes.md, section
// "2560x1440 捲動塗抹 — 根因已定位" for the full evidence chain.
//
// Raising the column comparison (kLimitSiteF) cannot help: a 16-byte slot has no
// bit 128 to scan. Widening the row mask would mean rewriting three fully unrolled
// 4-dword cascades plus every slot stride. Enlarging the *cell* instead costs nine
// bytes: with 32x32 cells the stock 128x75 grid covers 4096x2400 px, past 4K.
//
// Producer 0x0047ABF0 is the only writer (all 15 call sites funnel through it) and
// converts pixels to cells; consumer 0x0047A020 converts cells back to pixels. Both
// directions must move together, which is exactly what these nine bytes do. Every
// other `shl/sar reg,4` in 0x00478000..0x0047C600 is 16-byte slot or rectangle
// addressing and must stay as it is.
constexpr unsigned kStockCellPixels  = 16;
constexpr unsigned kWideCellPixels   = 32;
// 128 columns x 32 px, and 75 rows x 32 px. Beyond this the grid is out of bits again.
constexpr unsigned kWideCellMaxWidth = 128 * kWideCellPixels;

struct ByteRewrite {
    uintptr_t     site;
    unsigned      length;
    unsigned char original[4];
    unsigned char patched[4];
    const char*   what;
};

constexpr ByteRewrite kCellSites[] = {
    // producer 0x0047ABF0 -- pixels to cells
    {0x0047AC64, 3, {0xC1,0xF8,0x04}, {0xC1,0xF8,0x05}, "sar eax,N startCol"},
    {0x0047AC78, 3, {0xC1,0xF9,0x04}, {0xC1,0xF9,0x05}, "sar ecx,N endCol"},
    {0x0047AEE6, 3, {0xC1,0xFA,0x04}, {0xC1,0xFA,0x05}, "sar edx,N firstRow"},
    {0x0047AF07, 3, {0xC1,0xFA,0x04}, {0xC1,0xFA,0x05}, "sar edx,N lastRow"},
    // consumer 0x0047A020 -- cells back to pixels
    {0x0047A7F1, 3, {0xC1,0xE3,0x04}, {0xC1,0xE3,0x05}, "shl ebx,N left"},
    {0x0047A802, 3, {0xC1,0xE3,0x04}, {0xC1,0xE3,0x05}, "shl ebx,N right"},
    {0x0047A805, 4, {0x8D,0x5C,0x2B,0x0F}, {0x8D,0x5C,0x2B,0x1F}, "lea ebx,[ebx+ebp+cell-1]"},
    {0x0047A814, 3, {0xC1,0xE3,0x04}, {0xC1,0xE3,0x05}, "shl ebx,N top"},
    {0x0047A822, 3, {0xC1,0xE1,0x04}, {0xC1,0xE1,0x05}, "shl ecx,N bottom"},
};

bool CellGridValidate() {
    for (const auto& site : kCellSites) {
        if (memcmp(reinterpret_cast<const void*>(site.site), site.original, site.length) != 0)
            return false;
    }
    return true;
}

void CellGridWrite() {
    for (const auto& site : kCellSites)
        memcpy(reinterpret_cast<void*>(site.site), site.patched, site.length);
}

enum class InstallState {
    NotNeeded, Prepared, Installed, RefusedCapacity, ByteMismatch,
    AllocationFailed, ProtectFailed, DeferredTimeout
};

InstallState g_state = InstallState::NotNeeded;
// The nine-byte cell-size rewrite is gated and reported independently of the sidecar:
// the sidecar repairs the row axis (the crash), this repairs the column axis (the smear),
// and the two have different capacity thresholds.
InstallState g_cellState = InstallState::NotNeeded;
unsigned g_cellPixels = kStockCellPixels;
unsigned g_capacity = 1600;
unsigned g_sidecarSlots = 127;
unsigned g_columnCap = 127;
unsigned g_surfaceWidth = 2048;
unsigned g_surfaceHeight = 2048;
unsigned char* g_sidecar = nullptr;
unsigned char* g_caves = nullptr;
unsigned g_redirectCount = 0;
LONG g_prevHits[sizeof(CKPerfRedirectHits) / sizeof(CKPerfRedirectHits[0])] = {};
bool g_hitsReported = false;

constexpr uintptr_t kCVXVisibleGlobal         = 0x00798C64;
constexpr uintptr_t kCVXVisibleBeginOffset    = 0x4C8;
constexpr uintptr_t kCVXVisibleEndOffset      = 0x4CC;
constexpr uintptr_t kCVXVisibleCount500Offset = 0x50C;

// 16-byte CVXVisible rectangle element in the +0x4C8 dynamic container.
// Verified from engine assembly at 0x0047A7E9..0x0047A86F and 0x0047B420:
// field 0: left (min X), field 1: top (min Y), field 2: right (max X), field 3: bottom (max Y).
struct VisibleRect {
    int32_t left;
    int32_t top;
    int32_t right;
    int32_t bottom;
};

constexpr uint32_t kMaxVisibleWalk = 4096;

uint32_t g_maxVisibleCount = 0;
uint32_t g_prevVisibleCount = 0;
uint32_t g_prevMaxVisibleCount = 0;
int32_t  g_prevMaxRight = 0;
int32_t  g_prevMaxBottom = 0;
int32_t  g_prevMinLeft = 0;
int32_t  g_prevMinTop = 0;
uint32_t g_prevCount500 = 0;
bool     g_prevCapped = false;
bool     g_visibleReported = false;
uint32_t g_prevSuspectCount = 0;
bool     g_suspectReported = false;

// Maps a CKPerfRedirectHits index back to the code site a human can look up:
// 0..27 are kRedirects[index].site, 28/29/31 are the three merge caves, 30 is the
// per-frame clear, and 32..37 are the six consumer limit caves. Matches the index
// assignments made in HighResolutionInstallDeferred PutHitCounter calls.
const char* HitLabel(unsigned index, char* buf, size_t cch) {
    if (index < sizeof(kRedirects) / sizeof(kRedirects[0])) {
        _snprintf_s(buf, cch, _TRUNCATE, "r%u@0x%08X", index,
                    static_cast<unsigned>(kRedirects[index].site));
    } else switch (index) {
        case 28: _snprintf_s(buf, cch, _TRUNCATE, "merge1@0x%08X", static_cast<unsigned>(kMergeSite1)); break;
        case 29: _snprintf_s(buf, cch, _TRUNCATE, "merge2@0x%08X", static_cast<unsigned>(kMergeSite2)); break;
        case 30: _snprintf_s(buf, cch, _TRUNCATE, "clear@0x%08X",  static_cast<unsigned>(kClearSite));  break;
        case 31: _snprintf_s(buf, cch, _TRUNCATE, "merge3@0x%08X", static_cast<unsigned>(kMergeSite3)); break;
        case 32: _snprintf_s(buf, cch, _TRUNCATE, "limitA@0x%08X", static_cast<unsigned>(kLimitSiteA)); break;
        case 33: _snprintf_s(buf, cch, _TRUNCATE, "limitB@0x%08X", static_cast<unsigned>(kLimitSiteB)); break;
        case 34: _snprintf_s(buf, cch, _TRUNCATE, "limitC@0x%08X", static_cast<unsigned>(kLimitSiteC)); break;
        case 35: _snprintf_s(buf, cch, _TRUNCATE, "limitD@0x%08X", static_cast<unsigned>(kLimitSiteD)); break;
        case 36: _snprintf_s(buf, cch, _TRUNCATE, "limitE@0x%08X", static_cast<unsigned>(kLimitSiteE)); break;
        case 37: _snprintf_s(buf, cch, _TRUNCATE, "limitF@0x%08X", static_cast<unsigned>(kLimitSiteF)); break;
        default: _snprintf_s(buf, cch, _TRUNCATE, "?%u", index); break;
    }
    return buf;
}

int HexNibble(char c) {
    if (c >= '0' && c <= '9') return c - '0';
    if (c >= 'A' && c <= 'F') return c - 'A' + 10;
    if (c >= 'a' && c <= 'f') return c - 'a' + 10;
    return -1;
}

size_t DecodeHex(const char* hex, unsigned char* out, size_t capacity) {
    const size_t chars = strlen(hex);
    if ((chars & 1) != 0 || chars / 2 > capacity) return 0;
    for (size_t i = 0; i < chars / 2; ++i) {
        int hi = HexNibble(hex[i * 2]);
        int lo = HexNibble(hex[i * 2 + 1]);
        if (hi < 0 || lo < 0) return 0;
        out[i] = static_cast<unsigned char>((hi << 4) | lo);
    }
    return chars / 2;
}

void Put32(unsigned char*& p, uint32_t value) {
    memcpy(p, &value, sizeof(value));
    p += sizeof(value);
}

void PutRelJmp(unsigned char*& p, uintptr_t target) {
    uintptr_t instruction = reinterpret_cast<uintptr_t>(p);
    *p++ = 0xE9;
    Put32(p, static_cast<uint32_t>(target - (instruction + 5)));
}

void PutRelJcc(unsigned char*& p, unsigned char op2, uintptr_t target) {
    uintptr_t instruction = reinterpret_cast<uintptr_t>(p);
    *p++ = 0x0F;
    *p++ = op2;
    Put32(p, static_cast<uint32_t>(target - (instruction + 6)));
}

void PutHitCounter(unsigned char*& p, unsigned index) {
    *p++ = 0xFF; *p++ = 0x05; // inc dword ptr [absolute]
    Put32(p, static_cast<uint32_t>(reinterpret_cast<uintptr_t>(&CKPerfRedirectHits[index])));
}

void WriteSiteJmp(uintptr_t site, size_t length, uintptr_t cave) {
    auto* p = reinterpret_cast<unsigned char*>(site);
    p[0] = 0xE9;
    uint32_t rel = static_cast<uint32_t>(cave - (site + 5));
    memcpy(p + 1, &rel, sizeof(rel));
    for (size_t i = 5; i < length; ++i) p[i] = 0x90;
}

void WriteSiteImm32(uintptr_t pushSite, uint32_t value) {
    auto* p = reinterpret_cast<unsigned char*>(pushSite + 1);
    memcpy(p, &value, sizeof(value));
}

bool ValidateAll() {
    unsigned char expected[32];
    for (const auto& spec : kRedirects) {
        size_t length = DecodeHex(spec.originalHex, expected, sizeof(expected));
        if (length < 5 || memcmp(reinterpret_cast<const void*>(spec.site), expected, length) != 0)
            return false;
        unsigned reg = expected[1] & 7;
        if (expected[0] != 0xC1 || (expected[1] & 0xF8) != 0xE0 || expected[2] != 0x04 ||
            expected[length - 2] != 0x03 ||
            static_cast<unsigned>((expected[length - 1] >> 3) & 7) != reg)
            return false;
    }
    return memcmp(reinterpret_cast<const void*>(kClearSite), kClearOriginal, sizeof(kClearOriginal)) == 0 &&
           memcmp(reinterpret_cast<const void*>(kMergeSite1), kMergeOriginal1, sizeof(kMergeOriginal1)) == 0 &&
           memcmp(reinterpret_cast<const void*>(kMergeSite2), kMergeOriginal2, sizeof(kMergeOriginal2)) == 0 &&
           memcmp(reinterpret_cast<const void*>(kMergeSite3), kMergeOriginal3, sizeof(kMergeOriginal3)) == 0 &&
           memcmp(reinterpret_cast<const void*>(kLimitSiteA), kLimitOriginalA, sizeof(kLimitOriginalA)) == 0 &&
           memcmp(reinterpret_cast<const void*>(kLimitSiteB), kLimitOriginalB, sizeof(kLimitOriginalB)) == 0 &&
           memcmp(reinterpret_cast<const void*>(kLimitSiteC), kLimitOriginalC, sizeof(kLimitOriginalC)) == 0 &&
           memcmp(reinterpret_cast<const void*>(kLimitSiteD), kLimitOriginalD, sizeof(kLimitOriginalD)) == 0 &&
           memcmp(reinterpret_cast<const void*>(kLimitSiteE), kLimitOriginalE, sizeof(kLimitOriginalE)) == 0 &&
           memcmp(reinterpret_cast<const void*>(kLimitSiteF), kLimitOriginalF, sizeof(kLimitOriginalF)) == 0 &&
           memcmp(reinterpret_cast<const void*>(kSurfaceCtorHeightSite1), kSurfaceCtorOriginal1, sizeof(kSurfaceCtorOriginal1)) == 0 &&
           memcmp(reinterpret_cast<const void*>(kSurfaceCtorHeightSite2), kSurfaceCtorOriginal2, sizeof(kSurfaceCtorOriginal2)) == 0;
}

const char* StateName(InstallState state) {
    switch (state) {
        case InstallState::NotNeeded:        return "not needed";
        case InstallState::Prepared:         return "prepared; waiting for game window";
        case InstallState::Installed:        return "installed";
        case InstallState::RefusedCapacity:  return "refused capacity";
        case InstallState::ByteMismatch:     return "refused byte mismatch";
        case InstallState::AllocationFailed: return "allocation failed";
        case InstallState::ProtectFailed:    return "VirtualProtect failed";
        case InstallState::DeferredTimeout:  return "deferred window timeout";
    }
    return "unknown";
}

} // namespace

void HighResolutionInstallEarly() {
    CKPerfHighResolutionStep = 1;
    g_capacity = *reinterpret_cast<volatile uint32_t*>(kCapacityImmediate);

    // Derive sidecar slot count dynamically from configured resolution width (g_capacity).
    // Runtime measurements anchor at 1171 rectangles for capacity = 2560.
    // For capacity = 3840 (4K):
    //   - Linear scaling (width-only): 1171 * (3840 / 2560) ≈ 1757 rectangles.
    //   - Area scaling (width squared): 1171 * (3840 / 2560)^2 ≈ 2635 rectangles.
    // Both projections bracket 2048, showing a fixed 2048-slot allocation is insufficient for 4K.
    // We scale by (capacity / 2560)^2 to conservatively model area growth, then apply an
    // additional >= 2x safety headroom margin (yielding ~5270 slots at 3840px), and round up.
    // Clamped with a floor of 127 (original minimum) and a hard cap of 8192 slots (128 KB storage)
    // for defense-in-depth against corrupt width readings.
    double ratio = static_cast<double>(g_capacity) / 2560.0;
    double projected = 1171.0 * ratio * ratio * 2.0; // area-based scaling with 2x safety margin
    unsigned slots = static_cast<unsigned>(projected + 0.999); // round up
    if (slots < 127) slots = 127;
    if (slots > 8192) slots = 8192;
    g_sidecarSlots = slots;

    // Derive column limit (g_columnCap) for Site F dynamically from configured width (g_capacity).
    //
    // CORRECTION (2026-08-21): raising this cap does NOT fix the column axis, and the
    // original reasoning recorded here was wrong. 0x0047A122 caps the *bit index* inside
    // a row mask, and a row mask is one 16-byte slot = 4 dwords = 128 bits. There is no
    // bit 128 to scan, so a higher cap has nothing to find; the site fires and changes
    // nothing, which is exactly what the play-session hit counters showed. The column
    // axis is repaired by kCellSites (16 -> 32 px per cell) instead.
    //
    // The cap is still widened here because it is harmless and keeps the run-end value
    // from being pinned at 127 when the cell rewrite is active: the emitted right edge is
    // clamped back to the view rect at 0x0047A846 either way. It is floored at the stock
    // 127 so low resolutions never regress, and clamped to g_sidecarSlots.
    unsigned columnsNeeded = static_cast<unsigned>((static_cast<double>(g_capacity) / 16.0) + 0.999);
    unsigned colCap = columnsNeeded + 8; // additive margin for scroll/rounding edge cases
    if (colCap < 127) colCap = 127;
    if (colCap > g_sidecarSlots) colCap = g_sidecarSlots;
    g_columnCap = colCap;

    // Derive surface width (g_surfaceWidth) for block surface constructors at Sites 1 and 2.
    // The surface constructor divides width by 512 via `shr edx, 9` at 0x0047DA2B / 0x00478533
    // to determine the number of 512-pixel (32-column) blocks per row stored at [esi+0x18].
    // Any width that is not a multiple of 512 truncates this stride and corrupts indexing in
    // consumer function 0x00478280. We round g_capacity up to the nearest multiple of 512,
    // enforce a minimum floor of the stock 2048, and respect the kMaxSupportedWidth cap.
    // Examples:
    //   - capacity = 1600 -> 2048 (stock minimum)
    //   - capacity = 2560 -> 2560 (5 * 512)
    //   - capacity = 3840 -> 4096 (8 * 512, rounded up from 3840 = 7.5 * 512)
    unsigned surfWidth = ((g_capacity + 511) / 512) * 512;
    if (surfWidth < 2048) surfWidth = 2048;
    g_surfaceWidth = surfWidth;

    // Derive surface height (g_surfaceHeight) for block surface constructors at Sites 1 and 2.
    // Unlike width, height is not divided by 512 for row strides; the constructor computes
    // `height >> 4` at 0x0047DA35 / 0x0047853D to store `(height/16) - 1` at [esi+0x20] as the
    // row limit, and total backing buffer allocation is `(width * height) / 2048 + 32` bytes.
    // The height must adequately cover the vertical display resolution (e.g. up to 1440 for
    // 2560x1440, and 2160 for 3840x2160 4K). Assuming up to 16:9 / 16:10 aspect ratios:
    //   - At capacity = 1600 (1200p): 1200 <= 2048 -> stock 2048 covers it with ample headroom.
    //   - At capacity = 2560 (1440p / 1600p): 1440 <= 2048 -> stock 2048 covers it completely.
    //   - At capacity = 3840 (2160p / 2400p): 2160 > 2048 -> stock 2048 is exceeded!
    // We compute estimated vertical height = (g_capacity * 10) / 16 (16:10 aspect), round up to
    // a 512-pixel boundary for clean alignment, and floor at stock 2048. This yields 2048 for
    // capacity <= 2560 and 2560 for 3840 (4K), providing +400 px of vertical safety margin.
    unsigned estimatedHeight = (g_capacity * 10) / 16;
    unsigned surfHeight = ((estimatedHeight + 511) / 512) * 512;
    if (surfHeight < 2048) surfHeight = 2048;
    g_surfaceHeight = surfHeight;

    // Column axis. The stock 128-bit row mask covers 128 * 16 = 2048 px, so anything
    // wider needs the 32-pixel cell. This is decided independently of the sidecar
    // because the two failures have different thresholds: 2048x1152 is clean without
    // either, and a 2240-wide setting smears while still being under the sidecar's
    // 2400 threshold. 32-pixel cells top out at 128 * 32 = 4096 px wide.
    if (g_capacity > kWideCellMaxWidth)   g_cellState = InstallState::RefusedCapacity;
    else if (g_capacity > 2048)           g_cellState = CellGridValidate()
                                              ? InstallState::Prepared
                                              : InstallState::ByteMismatch;

    if (g_capacity <= 2400) { g_state = InstallState::NotNeeded; return; }
    if (g_capacity > kMaxSupportedWidth) { g_state = InstallState::RefusedCapacity; return; }
    if (!ValidateAll()) { g_state = InstallState::ByteMismatch; CKPerfHighResolutionStep = 2; return; }
    CKPerfHighResolutionStep = 3;

    g_sidecar = static_cast<unsigned char*>(VirtualAlloc(
        nullptr, g_sidecarSlots * kSlotBytes, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE));
    g_caves = static_cast<unsigned char*>(VirtualAlloc(
        nullptr, 4096, MEM_COMMIT | MEM_RESERVE, PAGE_EXECUTE_READWRITE));
    if (!g_sidecar || !g_caves) {
        if (g_sidecar) VirtualFree(g_sidecar, 0, MEM_RELEASE);
        if (g_caves) VirtualFree(g_caves, 0, MEM_RELEASE);
        g_sidecar = nullptr; g_caves = nullptr;
        g_state = InstallState::AllocationFailed;
        return;
    }
    CKPerfHighResolutionStep = 4;

    g_state = InstallState::Prepared;
    CKPerfHighResolutionStep = 5;
}

namespace {

BOOL CALLBACK FindProcessWindow(HWND hwnd, LPARAM parameter) {
    DWORD pid = 0;
    GetWindowThreadProcessId(hwnd, &pid);
    if (pid == GetCurrentProcessId() && IsWindowVisible(hwnd)) {
        *reinterpret_cast<HWND*>(parameter) = hwnd;
        return FALSE;
    }
    return TRUE;
}

size_t SuspendOtherThreads(HANDLE* handles, size_t capacity) {
    size_t count = 0;
    DWORD selfThread = GetCurrentThreadId();
    HANDLE snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPTHREAD, 0);
    if (snapshot == INVALID_HANDLE_VALUE) return 0;
    THREADENTRY32 entry = { sizeof(entry) };
    if (Thread32First(snapshot, &entry)) do {
        if (entry.th32OwnerProcessID != GetCurrentProcessId() || entry.th32ThreadID == selfThread)
            continue;
        HANDLE thread = OpenThread(THREAD_SUSPEND_RESUME, FALSE, entry.th32ThreadID);
        if (!thread) continue;
        if (SuspendThread(thread) == static_cast<DWORD>(-1)) {
            CloseHandle(thread);
            continue;
        }
        if (count < capacity) handles[count++] = thread;
        else { ResumeThread(thread); CloseHandle(thread); }
    } while (Thread32Next(snapshot, &entry));
    CloseHandle(snapshot);
    return count;
}

void ResumeThreads(HANDLE* handles, size_t count) {
    for (size_t i = 0; i < count; ++i) {
        ResumeThread(handles[i]);
        CloseHandle(handles[i]);
    }
}

// Writes the sidecar redirects, caves and surface sizes. The caller owns finding the
// window, re-validating the bytes, suspending the other threads and opening .text for
// writing, so that this and CellGridWrite() share one suspend/protect window.
void InstallSidecarPatches() {
    unsigned char* cave = g_caves;
    const uint32_t slotBaseMinus10 =
        static_cast<uint32_t>(reinterpret_cast<uintptr_t>(g_sidecar) - kSlotBytes);
    unsigned char original[32];

    // Function 0x00478840 loads the CVXVisible global with mov edi,[0x798c64],
    // calls the slot producer at 0x0047ABF0 with ecx=edi, and writes
    // mov dword ptr [edi+0x4C0],1. These first four kRedirects sites write into
    // that same object's inline array and must be redirected to the sidecar to
    // prevent producer/consumer split-brain.
    for (size_t redirectIndex = 0; redirectIndex < sizeof(kRedirects) / sizeof(kRedirects[0]); ++redirectIndex) {
        const auto& spec = kRedirects[redirectIndex];
        size_t length = DecodeHex(spec.originalHex, original, sizeof(original));
        unsigned reg = original[1] & 7;
        uintptr_t caveAddress = reinterpret_cast<uintptr_t>(cave);
        PutHitCounter(cave, static_cast<unsigned>(redirectIndex));
        memcpy(cave, original, length - 2); cave += length - 2;
        *cave++ = 0x81; *cave++ = static_cast<unsigned char>(0xC0 + reg);
        Put32(cave, slotBaseMinus10);
        PutRelJmp(cave, spec.site + length);
        WriteSiteJmp(spec.site, length, caveAddress);
        ++g_redirectCount;
    }

    // Redirect the stock per-frame clear to all contiguous sidecar slots.
    {
        uintptr_t caveAddress = reinterpret_cast<uintptr_t>(cave);
        PutHitCounter(cave, 30);
        *cave++ = 0xB9; Put32(cave, g_sidecarSlots * 4);
        *cave++ = 0xBF; Put32(cave, static_cast<uint32_t>(reinterpret_cast<uintptr_t>(g_sidecar)));
        PutRelJmp(cave, kClearSite + sizeof(kClearOriginal));
        WriteSiteJmp(kClearSite, sizeof(kClearOriginal), caveAddress);
    }

    {
        uintptr_t caveAddress = reinterpret_cast<uintptr_t>(cave);
        PutHitCounter(cave, 28);
        *cave++ = 0x40;
        *cave++ = 0xC1; *cave++ = 0xE0; *cave++ = 0x04;
        *cave++ = 0x05; Put32(cave, slotBaseMinus10);
        *cave++ = 0x8B; *cave++ = 0x08;
        *cave++ = 0x8B; *cave++ = 0x70; *cave++ = 0x08;
        PutRelJmp(cave, kMergeResume1);
        WriteSiteJmp(kMergeSite1, sizeof(kMergeOriginal1), caveAddress);
        ++g_redirectCount;
    }

    {
        uintptr_t caveAddress = reinterpret_cast<uintptr_t>(cave);
        PutHitCounter(cave, 29);
        *cave++ = 0x47;
        *cave++ = 0x8B; *cave++ = 0xC7;
        *cave++ = 0xC1; *cave++ = 0xE0; *cave++ = 0x04;
        *cave++ = 0x05; Put32(cave, slotBaseMinus10);
        *cave++ = 0x8B; *cave++ = 0x08;
        PutRelJmp(cave, kMergeResume2);
        WriteSiteJmp(kMergeSite2, sizeof(kMergeOriginal2), caveAddress);
        ++g_redirectCount;
    }

    {
        uintptr_t caveAddress = reinterpret_cast<uintptr_t>(cave);
        PutHitCounter(cave, 31);
        *cave++ = 0xC1; *cave++ = 0xE1; *cave++ = 0x04;
        *cave++ = 0x8D; *cave++ = 0x99;
        Put32(cave, static_cast<uint32_t>(reinterpret_cast<uintptr_t>(g_sidecar) + 8));
        PutRelJmp(cave, kMergeResume3);
        WriteSiteJmp(kMergeSite3, sizeof(kMergeOriginal3), caveAddress);
        ++g_redirectCount;
    }

    // Consumer Limit Site A (0x0047A097): cmp esi, g_sidecarSlots
    // Replaces `cmp esi, 4Bh` (3) + `mov edi, esi` (2). Overwritten length = 5 bytes.
    // Flags set by cmp survive through replayed mov and original `mov [esp+10h],edi` into `jge short 0x0047A115`.
    {
        uintptr_t caveAddress = reinterpret_cast<uintptr_t>(cave);
        PutHitCounter(cave, 32);
        *cave++ = 0x81; *cave++ = 0xFE; Put32(cave, g_sidecarSlots);
        *cave++ = 0x8B; *cave++ = 0xFE; // mov edi, esi (flag-preserving)
        PutRelJmp(cave, kLimitResumeA);
        WriteSiteJmp(kLimitSiteA, sizeof(kLimitOriginalA), caveAddress);
        ++g_redirectCount;
    }

    // Consumer Limit Site B (0x0047A0C3): cmp edi, g_sidecarSlots
    // Replaces `cmp edi, 4Bh` (3) + `jge short 0x0047A111` (2). Overwritten length = 5 bytes.
    // The displaced jge is re-encoded as a near conditional jump in the cave; not-taken falls through to jmp.
    {
        uintptr_t caveAddress = reinterpret_cast<uintptr_t>(cave);
        PutHitCounter(cave, 33);
        *cave++ = 0x81; *cave++ = 0xFF; Put32(cave, g_sidecarSlots);
        PutRelJcc(cave, 0x8D, kLimitBranchB); // jge near 0x0047A111 (0F 8D rel32)
        PutRelJmp(cave, kLimitResumeB);       // not-taken -> jmp 0x0047A0C8 (lands on original EB DE)
        WriteSiteJmp(kLimitSiteB, sizeof(kLimitOriginalB), caveAddress);
        ++g_redirectCount;
    }

    // Consumer Limit Site C (0x0047A115): cmp edi, g_sidecarSlots
    // Replaces `cmp edi, 4Bh` (3) + `je near 0x0047A894` (6). Overwritten length = 9 bytes (NOP-padded 5..8).
    // The displaced je is re-encoded as a near conditional jump in the cave.
    {
        uintptr_t caveAddress = reinterpret_cast<uintptr_t>(cave);
        PutHitCounter(cave, 34);
        *cave++ = 0x81; *cave++ = 0xFF; Put32(cave, g_sidecarSlots);
        PutRelJcc(cave, 0x84, kLimitBranchC); // je near 0x0047A894 (0F 84 rel32)
        PutRelJmp(cave, kLimitResumeC);       // not-taken -> jmp 0x0047A11E (lands on mov esi,[esp+18h])
        WriteSiteJmp(kLimitSiteC, sizeof(kLimitOriginalC), caveAddress);
        ++g_redirectCount;
    }

    // Consumer Limit Site D (0x0047A489): cmp ecx, g_sidecarSlots
    // Replaces `cmp ecx, 4Bh` (3) + `mov [esp+40h], ebx` (4). Overwritten length = 7 bytes (NOP-padded 5..6).
    // Flags set by cmp survive through replayed mov and original `mov [esp+10h],ecx` into `jge near 0x0047A7D9`.
    {
        uintptr_t caveAddress = reinterpret_cast<uintptr_t>(cave);
        PutHitCounter(cave, 35);
        *cave++ = 0x81; *cave++ = 0xF9; Put32(cave, g_sidecarSlots);
        *cave++ = 0x89; *cave++ = 0x5C; *cave++ = 0x24; *cave++ = 0x40; // mov [esp+40h], ebx (flag-preserving)
        PutRelJmp(cave, kLimitResumeD);
        WriteSiteJmp(kLimitSiteD, sizeof(kLimitOriginalD), caveAddress);
        ++g_redirectCount;
    }

    // Consumer Limit Site E (0x0047A7A4): cmp ecx, g_sidecarSlots
    // Replaces `cmp ecx, 4Bh` (3) + `mov [esp+10h], ecx` (4). Overwritten length = 7 bytes (NOP-padded 5..6).
    // Flags set by cmp survive through replayed mov into original `jl near 0x0047A724`.
    {
        uintptr_t caveAddress = reinterpret_cast<uintptr_t>(cave);
        PutHitCounter(cave, 36);
        *cave++ = 0x81; *cave++ = 0xF9; Put32(cave, g_sidecarSlots);
        *cave++ = 0x89; *cave++ = 0x4C; *cave++ = 0x24; *cave++ = 0x10; // mov [esp+10h], ecx (flag-preserving)
        PutRelJmp(cave, kLimitResumeE);
        WriteSiteJmp(kLimitSiteE, sizeof(kLimitOriginalE), caveAddress);
        ++g_redirectCount;
    }

    // Consumer Limit Site F (0x0047A122): cmp esi, g_columnCap
    // Replaces `cmp esi, 7Fh` (3) + `mov [esp+24h], edi` (4). Overwritten length = 7 bytes (NOP-padded 5..6).
    // Flags set by cmp survive through replayed mov into original `jge short 0x0047A12C`.
    // NOTE: this does NOT fix the column axis on its own -- see the correction next to
    // g_columnCap. 0x0047A122 caps a bit index within a 128-bit row mask, so widening the
    // cap finds no extra bits. kCellSites is what repairs the column axis.
    {
        uintptr_t caveAddress = reinterpret_cast<uintptr_t>(cave);
        PutHitCounter(cave, 37);
        *cave++ = 0x81; *cave++ = 0xFE; Put32(cave, g_columnCap);
        *cave++ = 0x89; *cave++ = 0x7C; *cave++ = 0x24; *cave++ = 0x24; // mov [esp+24h], edi (flag-preserving)
        PutRelJmp(cave, kLimitResumeF);
        WriteSiteJmp(kLimitSiteF, sizeof(kLimitOriginalF), caveAddress);
        ++g_redirectCount;
    }

    // Patch 2048x2048 Block Surface constructor arguments at Sites 1 and 2.
    // Site 1 (0x00479E49 / 0x00479E4E): Master surface constructor in 0x00479DC0.
    // Overwrites push imm32 height (pushed first at 0x00479E49) and width (pushed second at 0x00479E4E).
    WriteSiteImm32(kSurfaceCtorHeightSite1, g_surfaceHeight);
    WriteSiteImm32(kSurfaceCtorWidthSite1,  g_surfaceWidth);

    // Site 2 (0x004780EA / 0x004780EF): Temporary shadow surface constructor in 0x004780D0.
    // Overwrites push imm32 height (pushed second at 0x004780EA) and width (pushed third at 0x004780EF).
    WriteSiteImm32(kSurfaceCtorHeightSite2, g_surfaceHeight);
    WriteSiteImm32(kSurfaceCtorWidthSite2,  g_surfaceWidth);
}

} // namespace

void HighResolutionInstallDeferred() {
    bool wantSidecar = (g_state == InstallState::Prepared);
    bool wantCells   = (g_cellState == InstallState::Prepared);
    if (!wantSidecar && !wantCells) return;

    HWND gameWindow = nullptr;
    for (unsigned attempt = 0; attempt < 200 && !gameWindow; ++attempt) {
        EnumWindows(FindProcessWindow, reinterpret_cast<LPARAM>(&gameWindow));
        if (!gameWindow) Sleep(100);
    }
    if (!gameWindow) {
        if (wantSidecar) g_state = InstallState::DeferredTimeout;
        if (wantCells)   g_cellState = InstallState::DeferredTimeout;
        HighResolutionLogStatus();
        return;
    }

    // The executable can initialise or verify its code before creating the main
    // window. Re-check immediately before writing, then stop all other threads for
    // the few microseconds in which the multi-site patch becomes visible.
    // The two patches are validated and refused independently: a byte mismatch in
    // one of them must not silently disable the other.
    if (wantSidecar && !ValidateAll()) { g_state = InstallState::ByteMismatch; wantSidecar = false; }
    if (wantCells && !CellGridValidate()) { g_cellState = InstallState::ByteMismatch; wantCells = false; }
    if (!wantSidecar && !wantCells) {
        HighResolutionLogStatus();
        return;
    }

    HANDLE suspended[128] = {};
    size_t suspendedCount = SuspendOtherThreads(suspended, sizeof(suspended) / sizeof(suspended[0]));

    DWORD oldProtect = 0;
    if (!VirtualProtect(reinterpret_cast<void*>(kTextStart), kTextLength,
                        PAGE_EXECUTE_READWRITE, &oldProtect)) {
        ResumeThreads(suspended, suspendedCount);
        if (wantSidecar) g_state = InstallState::ProtectFailed;
        if (wantCells)   g_cellState = InstallState::ProtectFailed;
        HighResolutionLogStatus();
        return;
    }

    if (wantSidecar) {
        InstallSidecarPatches();
        g_state = InstallState::Installed;
    }
    if (wantCells) {
        CellGridWrite();
        g_cellPixels = kWideCellPixels;
        g_cellState = InstallState::Installed;
    }

    DWORD ignored = 0;
    VirtualProtect(reinterpret_cast<void*>(kTextStart), kTextLength, oldProtect, &ignored);
    FlushInstructionCache(GetCurrentProcess(), reinterpret_cast<void*>(kTextStart), kTextLength);
    ResumeThreads(suspended, suspendedCount);
    CKPerfHighResolutionStep = 6;
    HighResolutionLogStatus();
}

void HighResolutionLogStatus() {
    Logf("CVXVisible sidecar: %s (HiRes capacity %u, surface %ux%u, slots %u, redirects %u, storage 0x%08X)",
         StateName(g_state), g_capacity, g_surfaceWidth, g_surfaceHeight, g_sidecarSlots, g_redirectCount,
         static_cast<unsigned>(reinterpret_cast<uintptr_t>(g_sidecar)));
    // The dirty-cell size is the column-axis repair and is reported separately because it
    // installs, refuses and fails independently of the sidecar's row-axis repair.
    Logf("CVXVisible dirty cell: %s (%u px per cell, grid covers %ux%u px, %u sites)",
         StateName(g_cellState), g_cellPixels, 128 * g_cellPixels, 75 * g_cellPixels,
         static_cast<unsigned>(sizeof(kCellSites) / sizeof(kCellSites[0])));
}

// Drains CKPerfRedirectHits into the log. Silent when the sidecar was never
// installed (nothing fired, there is nothing to say), and silent on every tick
// where nothing changed, so a play session log gains one clear entry per state
// change instead of the same numbers repeated once a second.
// The zero-hit list is the point of this function: a redirect that never fires is
// the one piece of evidence that tells us which storage path is still unaccounted for.
void HighResolutionLogHitCounts() {
    if (g_state != InstallState::Installed) return;

    constexpr size_t kHitCount = sizeof(CKPerfRedirectHits) / sizeof(CKPerfRedirectHits[0]);
    LONG snapshot[kHitCount];
    bool changed = !g_hitsReported;
    for (unsigned i = 0; i < kHitCount; ++i) {
        snapshot[i] = CKPerfRedirectHits[i];
        if (snapshot[i] != g_prevHits[i]) changed = true;
    }
    if (!changed) return;

    char zeros[768] = "";
    int zpos = 0;
    unsigned zeroCount = 0;
    for (unsigned i = 0; i < kHitCount; ++i) {
        if (snapshot[i] != 0) continue;
        char label[32];
        HitLabel(i, label, sizeof(label));
        zpos = Append(zeros, sizeof(zeros), zpos, "%s%s", zpos ? ", " : "", label);
        ++zeroCount;
    }

    if (zeroCount == 0) {
        Logf("redirect hits: all %u sites have fired at least once.", static_cast<unsigned>(kHitCount));
    } else {
        Logf("redirect hits: %u of %u sites have NEVER fired -- %s", zeroCount, static_cast<unsigned>(kHitCount), zeros);
    }

    char detail[1024] = "";
    int dpos = 0;
    for (unsigned i = 0; i < kHitCount; ++i) {
        char label[32];
        HitLabel(i, label, sizeof(label));
        dpos = Append(detail, sizeof(detail), dpos, "%s%s=%ld", dpos ? " " : "", label, snapshot[i]);
    }
    Logf("redirect hits detail: %s", detail);

    memcpy(g_prevHits, snapshot, sizeof(snapshot));
    g_hitsReported = true;
}

// Samples the real CVXVisible dynamic rectangle container (+0x4C8 begin, +0x4CC end)
// to inspect live bounding coordinate coverage and detect whether the rectangle producer
// covers the full screen width or remains capped at 2048.
// Like hit logging, only writes when counts or bounding coordinates change, except when
// the count actively overflows g_sidecarSlots (which warns on every tick).
void HighResolutionLogVisibleCount() {
    if (g_state != InstallState::Installed) return;

    uint32_t objPtr = 0;
    if (!SafeRead(kCVXVisibleGlobal, &objPtr, sizeof(objPtr)) || objPtr == 0) return;

    uint32_t begin = 0;
    uint32_t end = 0;
    uint32_t count500 = 0;

    bool hasBegin = SafeRead(objPtr + kCVXVisibleBeginOffset, &begin, sizeof(begin));
    bool hasEnd   = SafeRead(objPtr + kCVXVisibleEndOffset, &end, sizeof(end));
    SafeRead(objPtr + kCVXVisibleCount500Offset, &count500, sizeof(count500));

    if (!hasBegin || !hasEnd) return;

    uint32_t count = 0;
    if (begin != 0 && end != 0 && end >= begin) {
        count = (end - begin) / sizeof(VisibleRect);
    } else {
        count = 0;
    }

    if (count > 1000000) {
        if (!g_suspectReported || count != g_prevSuspectCount) {
            Logf("CVXVisible count: suspect reading (%u rectangles, begin 0x%08X, end 0x%08X) exceeds sanity limit, skipping",
                 count, begin, end);
            g_prevSuspectCount = count;
            g_suspectReported = true;
        }
        return;
    }
    g_suspectReported = false;

    if (count > g_maxVisibleCount) {
        g_maxVisibleCount = count;
    }

    int32_t minLeft = 0;
    int32_t minTop = 0;
    int32_t maxRight = 0;
    int32_t maxBottom = 0;
    bool hasValidRect = false;
    bool capped = false;

    uint32_t walkCount = count;
    if (walkCount > kMaxVisibleWalk) {
        walkCount = kMaxVisibleWalk;
        capped = true;
    }

    if (count > 0 && begin != 0) {
        constexpr size_t kChunkSize = 128;
        VisibleRect chunk[kChunkSize];
        for (uint32_t i = 0; i < walkCount; i += static_cast<uint32_t>(kChunkSize)) {
            uint32_t batch = (walkCount - i < static_cast<uint32_t>(kChunkSize))
                ? (walkCount - i)
                : static_cast<uint32_t>(kChunkSize);
            uintptr_t addr = begin + i * sizeof(VisibleRect);
            if (!SafeRead(addr, chunk, batch * sizeof(VisibleRect))) {
                break;
            }
            for (uint32_t j = 0; j < batch; ++j) {
                const auto& r = chunk[j];
                // Reject absurd coordinates indicative of uninitialized or corrupted entries.
                if (r.left < -100000 || r.left > 100000 ||
                    r.top < -100000 || r.top > 100000 ||
                    r.right < -100000 || r.right > 100000 ||
                    r.bottom < -100000 || r.bottom > 100000) {
                    continue;
                }
                if (!hasValidRect) {
                    minLeft = r.left;
                    minTop = r.top;
                    maxRight = r.right;
                    maxBottom = r.bottom;
                    hasValidRect = true;
                } else {
                    if (r.left < minLeft) minLeft = r.left;
                    if (r.top < minTop) minTop = r.top;
                    if (r.right > maxRight) maxRight = r.right;
                    if (r.bottom > maxBottom) maxBottom = r.bottom;
                }
            }
        }
    }

    bool changed = !g_visibleReported ||
                   (count != g_prevVisibleCount) ||
                   (g_maxVisibleCount != g_prevMaxVisibleCount) ||
                   (maxRight != g_prevMaxRight) ||
                   (maxBottom != g_prevMaxBottom) ||
                   (minLeft != g_prevMinLeft) ||
                   (minTop != g_prevMinTop) ||
                   (count500 != g_prevCount500) ||
                   (capped != g_prevCapped);

    if (changed) {
        if (capped) {
            Logf("CVXVisible: %u rects (peak %u, sidecar cap %u) [walk capped at %u] | MAX RIGHT %d, max bot %d | min left %d, min top %d | +0x500 count %u",
                 count, g_maxVisibleCount, g_sidecarSlots, kMaxVisibleWalk, maxRight, maxBottom, minLeft, minTop, count500);
        } else {
            Logf("CVXVisible: %u rects (peak %u, sidecar cap %u) | MAX RIGHT %d, max bot %d | min left %d, min top %d | +0x500 count %u",
                 count, g_maxVisibleCount, g_sidecarSlots, maxRight, maxBottom, minLeft, minTop, count500);
        }
        g_prevVisibleCount = count;
        g_prevMaxVisibleCount = g_maxVisibleCount;
        g_prevMaxRight = maxRight;
        g_prevMaxBottom = maxBottom;
        g_prevMinLeft = minLeft;
        g_prevMinTop = minTop;
        g_prevCount500 = count500;
        g_prevCapped = capped;
        g_visibleReported = true;
    }

    if (count > g_sidecarSlots) {
        Logf("!!! CVXVisible OVERFLOW: %u rectangles exceed the %u-slot sidecar capacity by %u -- surplus rectangles are being silently dropped !!!",
             count, g_sidecarSlots, count - g_sidecarSlots);
    }
}

} // namespace ckperf
