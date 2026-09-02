// frames.cpp — passive frame and blit-cost measurement.
//
// The engine has no GPU path at all: no ddraw, no d3d, nothing. It software-rasterises
// into a memory buffer and pushes the whole screen out through exactly one GDI call,
// GDI32!SetDIBitsToDevice, from exactly one call site (.text VA 0x0044F536). That makes
// that single import the perfect frame boundary AND the perfect measurement point:
//
//   * time spent inside the call is the cost of getting the frame onto the screen;
//   * time between calls is everything else -- simulation, AI, rasterising.
//
// The existing sampling profiler cannot see the first number at all, because it throws
// away every sample whose EIP falls outside the game module. That is roughly 40% of the
// main thread and it is the single biggest unknown in the performance picture.
//
// This hook is measure-only. It calls straight through and changes no arguments.

#include "ckperf.h"
#include <stdio.h>

namespace ckperf {

typedef int (WINAPI* PFN_SetDIBitsToDevice)(HDC, int, int, DWORD, DWORD, int, int,
                                            UINT, UINT, const void*, const BITMAPINFO*, UINT);

static PFN_SetDIBitsToDevice g_origBlit = nullptr;
static void**                g_iatSlot  = nullptr;

// Accumulators. Written only by the render thread, drained by the telemetry thread.
// Interlocked on the 64-bit sums would cost more than the numbers are worth, so the
// drain tolerates a torn read once per second; the error is far below the noise floor.
static volatile LONG64 g_blitTicks   = 0;
static volatile LONG64 g_frameTicks  = 0;
static volatile LONG   g_frameCount  = 0;
static volatile LONG64 g_worstFrame  = 0;
static volatile LONG   g_hitches     = 0;   // frames over 50 ms
static LARGE_INTEGER   g_qpf         = {};
static LONG64          g_lastFrameQpc = 0;

// ------------------------------------------------------------- blit geometry tracking
//
// High-resolution rendering at 2560x1440 produces a corrupted band beginning at x ≈ 2048.
// SetDIBitsToDevice is the engine's sole output path, so intercepting its arguments gives
// the ground truth of what width the rasteriser actually produced and blitted to screen.

struct BlitGeometry {
    int      xDest;
    int      yDest;
    uint32_t w;
    uint32_t h;
    int      xSrc;
    int      ySrc;
    uint32_t startScan;
    uint32_t scanLines;
    int32_t  biWidth;
    int32_t  biHeight;
    uint16_t biBitCount;
    uint32_t biCompression;
};

struct BlitGeoEntry {
    BlitGeometry geom;
    uint32_t     count;
};

constexpr int kMaxTrackedGeometries = 16;

static volatile int      g_maxDestWidth    = 0;
static volatile int      g_maxDestRight    = 0;
static volatile int      g_maxDestHeight   = 0;
static volatile int      g_maxDestBottom   = 0;
static volatile int32_t  g_maxBiWidth      = 0;
static volatile int32_t  g_maxBiHeight     = 0;
static volatile uint32_t g_totalBlits      = 0;
static volatile uint32_t g_distinctGeosSeen = 0;
static volatile int      g_geoCount        = 0;
static BlitGeoEntry      g_geos[kMaxTrackedGeometries] = {};

struct BlitLogState {
    int          maxDestWidth;
    int          maxDestRight;
    int          maxDestHeight;
    int          maxDestBottom;
    int32_t      maxBiWidth;
    int32_t      maxBiHeight;
    int          geoCount;
    uint32_t     distinctSeen;
    BlitGeometry geos[kMaxTrackedGeometries];
};

// True only when the user actually asked for frame statistics. The hook itself may be
// installed purely to give the runtime script channel a main-thread call site, and in
// that case the log must stay silent about frames.
static bool         g_timingEnabled = false;

static BlitLogState g_prevLogState = {};
static bool         g_blitReported = false;

static int WINAPI HookedSetDIBitsToDevice(HDC hdc, int xDest, int yDest, DWORD w, DWORD h,
                                          int xSrc, int ySrc, UINT startScan, UINT scanLines,
                                          const void* bits, const BITMAPINFO* bmi, UINT colorUse) {
    LARGE_INTEGER t0, t1;
    QueryPerformanceCounter(&t0);
    int r = g_origBlit(hdc, xDest, yDest, w, h, xSrc, ySrc, startScan, scanLines, bits, bmi, colorUse);
    QueryPerformanceCounter(&t1);

    LONG64 blit = t1.QuadPart - t0.QuadPart;
    g_blitTicks += blit;

    if (g_lastFrameQpc) {
        LONG64 frame = t1.QuadPart - g_lastFrameQpc;
        g_frameTicks += frame;
        if (frame > g_worstFrame) g_worstFrame = frame;
        if (g_qpf.QuadPart && frame * 1000 / g_qpf.QuadPart >= 50) InterlockedIncrement(&g_hitches);
    }
    g_lastFrameQpc = t1.QuadPart;
    InterlockedIncrement(&g_frameCount);

    // The runtime script channel piggybacks on this hook because it is the one place in
    // the process that is guaranteed to be the engine's main thread, which is where the
    // engine itself runs key-bound scripts (script.cpp). Draining after the blit means a
    // script never delays the frame that is already on its way to the screen. The pump
    // returns immediately when nothing is queued, which is the overwhelmingly common case.
    ScriptChannelPump();

    // Record blit geometry. Read the BITMAPINFO header safely without assuming readable memory.
    int32_t  biWidth = 0;
    int32_t  biHeight = 0;
    uint16_t biBitCount = 0;
    uint32_t biCompression = 0;

    if (bmi) {
        BITMAPINFOHEADER hdr = {};
        if (SafeRead((uintptr_t)bmi, &hdr, sizeof(hdr))) {
            if (hdr.biSize == sizeof(BITMAPCOREHEADER)) {
                const auto* core = reinterpret_cast<const BITMAPCOREHEADER*>(&hdr);
                biWidth = core->bcWidth;
                biHeight = core->bcHeight;
                biBitCount = core->bcBitCount;
                biCompression = 0;
            } else {
                biWidth = hdr.biWidth;
                biHeight = hdr.biHeight;
                biBitCount = hdr.biBitCount;
                biCompression = hdr.biCompression;
            }
        }
    }

    int destRight = xDest + static_cast<int>(w);
    int destBottom = yDest + static_cast<int>(h);
    int32_t absBiW = biWidth < 0 ? -biWidth : biWidth;
    int32_t absBiH = biHeight < 0 ? -biHeight : biHeight;

    if (static_cast<int>(w) > g_maxDestWidth) g_maxDestWidth = static_cast<int>(w);
    if (destRight > g_maxDestRight) g_maxDestRight = destRight;
    if (static_cast<int>(h) > g_maxDestHeight) g_maxDestHeight = static_cast<int>(h);
    if (destBottom > g_maxDestBottom) g_maxDestBottom = destBottom;
    if (absBiW > g_maxBiWidth) g_maxBiWidth = absBiW;
    if (absBiH > g_maxBiHeight) g_maxBiHeight = absBiH;

    g_totalBlits++;

    int count = g_geoCount;
    int found = -1;
    for (int i = 0; i < count; ++i) {
        const auto& g = g_geos[i].geom;
        if (g.xDest == xDest && g.yDest == yDest &&
            g.w == w && g.h == h &&
            g.xSrc == xSrc && g.ySrc == ySrc &&
            g.startScan == startScan && g.scanLines == scanLines &&
            g.biWidth == biWidth && g.biHeight == biHeight &&
            g.biBitCount == biBitCount && g.biCompression == biCompression) {
            found = i;
            break;
        }
    }

    if (found >= 0) {
        g_geos[found].count++;
    } else {
        g_distinctGeosSeen++;
        if (count < kMaxTrackedGeometries) {
            g_geos[count].geom = { xDest, yDest, w, h, xSrc, ySrc, startScan, scanLines, biWidth, biHeight, biBitCount, biCompression };
            g_geos[count].count = 1;
            g_geoCount = count + 1;
        } else {
            // Keep the largest blits by area so full-frame blits are never displaced by small tiles.
            uint64_t currentArea = static_cast<uint64_t>(w) * h;
            int smallestIdx = -1;
            uint64_t smallestArea = currentArea;
            for (int i = 0; i < kMaxTrackedGeometries; ++i) {
                uint64_t a = static_cast<uint64_t>(g_geos[i].geom.w) * g_geos[i].geom.h;
                if (a < smallestArea) {
                    smallestArea = a;
                    smallestIdx = i;
                }
            }
            if (smallestIdx >= 0) {
                g_geos[smallestIdx].geom = { xDest, yDest, w, h, xSrc, ySrc, startScan, scanLines, biWidth, biHeight, biBitCount, biCompression };
                g_geos[smallestIdx].count = 1;
            }
        }
    }

    return r;
}

// --------------------------------------------------------------------- IAT patching

static void** FindIatSlot(HMODULE mod, const char* dllName, const char* funcName) {
    uintptr_t base = (uintptr_t)mod;
    IMAGE_DOS_HEADER* dos = (IMAGE_DOS_HEADER*)base;
    if (dos->e_magic != IMAGE_DOS_SIGNATURE) return nullptr;
    IMAGE_NT_HEADERS32* nt = (IMAGE_NT_HEADERS32*)(base + dos->e_lfanew);
    if (nt->Signature != IMAGE_NT_SIGNATURE) return nullptr;

    IMAGE_DATA_DIRECTORY& dir = nt->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_IMPORT];
    if (!dir.VirtualAddress || !dir.Size) return nullptr;

    IMAGE_IMPORT_DESCRIPTOR* imp = (IMAGE_IMPORT_DESCRIPTOR*)(base + dir.VirtualAddress);
    for (; imp->Name; ++imp) {
        const char* name = (const char*)(base + imp->Name);
        if (_stricmp(name, dllName) != 0) continue;

        // OriginalFirstThunk keeps the names even after the loader has overwritten
        // FirstThunk with resolved addresses; walk both in lockstep.
        IMAGE_THUNK_DATA32* names = (IMAGE_THUNK_DATA32*)(base + (imp->OriginalFirstThunk ? imp->OriginalFirstThunk : imp->FirstThunk));
        IMAGE_THUNK_DATA32* addrs = (IMAGE_THUNK_DATA32*)(base + imp->FirstThunk);
        for (; names->u1.AddressOfData; ++names, ++addrs) {
            if (names->u1.Ordinal & IMAGE_ORDINAL_FLAG32) continue;
            IMAGE_IMPORT_BY_NAME* ibn = (IMAGE_IMPORT_BY_NAME*)(base + names->u1.AddressOfData);
            if (strcmp((const char*)ibn->Name, funcName) == 0) return (void**)&addrs->u1.Function;
        }
    }
    return nullptr;
}

void FrameTimingInstall() {
    // The script channel needs this hook even when the user has timing switched off: it
    // is the only main-thread call site available to it. Installing the hook for the
    // channel does not switch timing on -- g_timingEnabled still gates the log lines, so
    // "frames=0" keeps meaning "no frame statistics in the log".
    if (!g_cfg.frameTiming && !g_cfg.scriptChannel) return;
    g_timingEnabled = g_cfg.frameTiming;

    QueryPerformanceFrequency(&g_qpf);

    HMODULE game = GetModuleHandleW(nullptr);
    g_iatSlot = FindIatSlot(game, "GDI32.dll", "SetDIBitsToDevice");
    if (!g_iatSlot) {
        Logf("frame timing: SetDIBitsToDevice IAT slot not found; timing disabled.");
        return;
    }

    DWORD old = 0;
    if (!VirtualProtect(g_iatSlot, sizeof(void*), PAGE_READWRITE, &old)) {
        Logf("frame timing: VirtualProtect on the IAT slot failed (%u); timing disabled.", GetLastError());
        g_iatSlot = nullptr;
        return;
    }
    g_origBlit = (PFN_SetDIBitsToDevice)*g_iatSlot;
    *g_iatSlot = (void*)&HookedSetDIBitsToDevice;
    VirtualProtect(g_iatSlot, sizeof(void*), old, &old);

    Logf("frame timing installed (IAT slot 0x%08X, original GDI32!SetDIBitsToDevice = 0x%08X)",
         (unsigned)(uintptr_t)g_iatSlot, (unsigned)(uintptr_t)g_origBlit);
}

void FrameTimingUninstall() {
    if (!g_iatSlot || !g_origBlit) return;
    DWORD old = 0;
    if (VirtualProtect(g_iatSlot, sizeof(void*), PAGE_READWRITE, &old)) {
        *g_iatSlot = (void*)g_origBlit;
        VirtualProtect(g_iatSlot, sizeof(void*), old, &old);
    }
    g_iatSlot = nullptr;
    g_timingEnabled = false;
}

void BlitGeometryLog() {
    int curMaxW      = g_maxDestWidth;
    int curMaxRight  = g_maxDestRight;
    int curMaxH      = g_maxDestHeight;
    int curMaxBot    = g_maxDestBottom;
    int32_t curBiW   = g_maxBiWidth;
    int32_t curBiH   = g_maxBiHeight;
    int curCount     = g_geoCount;
    uint32_t curDist = g_distinctGeosSeen;

    if (curCount == 0 && curMaxW == 0) return;

    bool changed = !g_blitReported ||
                   curMaxW != g_prevLogState.maxDestWidth ||
                   curMaxRight != g_prevLogState.maxDestRight ||
                   curMaxH != g_prevLogState.maxDestHeight ||
                   curMaxBot != g_prevLogState.maxDestBottom ||
                   curBiW != g_prevLogState.maxBiWidth ||
                   curBiH != g_prevLogState.maxBiHeight ||
                   curCount != g_prevLogState.geoCount ||
                   curDist != g_prevLogState.distinctSeen;

    if (!changed) {
        for (int i = 0; i < curCount && i < kMaxTrackedGeometries; ++i) {
            if (memcmp(&g_geos[i].geom, &g_prevLogState.geos[i], sizeof(BlitGeometry)) != 0) {
                changed = true;
                break;
            }
        }
    }

    if (!changed) return;

    // Snapshot geometries for sorting by area descending
    BlitGeoEntry sorted[kMaxTrackedGeometries];
    int n = curCount < kMaxTrackedGeometries ? curCount : kMaxTrackedGeometries;
    for (int i = 0; i < n; ++i) {
        sorted[i] = g_geos[i];
    }

    for (int i = 0; i < n - 1; ++i) {
        for (int j = i + 1; j < n; ++j) {
            uint64_t a1 = static_cast<uint64_t>(sorted[i].geom.w) * sorted[i].geom.h;
            uint64_t a2 = static_cast<uint64_t>(sorted[j].geom.w) * sorted[j].geom.h;
            if (a2 > a1) {
                BlitGeoEntry tmp = sorted[i];
                sorted[i] = sorted[j];
                sorted[j] = tmp;
            }
        }
    }

    char detail[768] = "";
    int dpos = 0;
    int showCount = n > 3 ? 3 : n;
    for (int i = 0; i < showCount; ++i) {
        const auto& g = sorted[i].geom;
        dpos = Append(detail, sizeof(detail), dpos,
                      "%s[dest(%d,%d %ux%u) src(%d,%d scan %u len %u) bmp(%dx%d %ubpp c%u) x%u]",
                      dpos ? " " : "",
                      g.xDest, g.yDest, g.w, g.h,
                      g.xSrc, g.ySrc, g.startScan, g.scanLines,
                      g.biWidth, g.biHeight, (unsigned)g.biBitCount, g.biCompression,
                      sorted[i].count);
    }

    if (curDist > static_cast<uint32_t>(showCount)) {
        Logf("blit geom: max dest w %d (x+w %d), max src biWidth %d | %u distinct (showing top %d): %s",
             curMaxW, curMaxRight, curBiW, curDist, showCount, detail);
    } else {
        Logf("blit geom: max dest w %d (x+w %d), max src biWidth %d | %u distinct: %s",
             curMaxW, curMaxRight, curBiW, curDist, detail);
    }

    g_prevLogState.maxDestWidth  = curMaxW;
    g_prevLogState.maxDestRight  = curMaxRight;
    g_prevLogState.maxDestHeight = curMaxH;
    g_prevLogState.maxDestBottom = curMaxBot;
    g_prevLogState.maxBiWidth    = curBiW;
    g_prevLogState.maxBiHeight   = curBiH;
    g_prevLogState.geoCount      = curCount;
    g_prevLogState.distinctSeen  = curDist;
    for (int i = 0; i < n; ++i) {
        g_prevLogState.geos[i] = g_geos[i].geom;
    }
    g_blitReported = true;
}

bool FrameTimingDrainAndLog(double periodSeconds) {
    if (!g_iatSlot) return false;
    if (!g_timingEnabled) return false;

    LONG   frames = InterlockedExchange(&g_frameCount, 0);
    LONG64 blit   = InterlockedExchange64(&g_blitTicks, 0);
    LONG64 total  = InterlockedExchange64(&g_frameTicks, 0);
    LONG64 worst  = InterlockedExchange64(&g_worstFrame, 0);
    LONG   hitch  = InterlockedExchange(&g_hitches, 0);

    if (frames > 0) {
        double qpf      = (double)g_qpf.QuadPart;
        double blitMs   = qpf ? (blit  / qpf) * 1000.0 / frames : 0.0;
        double frameMs  = qpf ? (total / qpf) * 1000.0 / frames : 0.0;
        double worstMs  = qpf ? (worst / qpf) * 1000.0 : 0.0;
        double fps      = periodSeconds > 0 ? frames / periodSeconds : 0.0;
        double blitPct  = frameMs > 0 ? blitMs / frameMs * 100.0 : 0.0;

        Logf("frames: %.1f fps | frame %.2f ms | blit %.2f ms (%.0f%% of the frame) | worst %.1f ms | hitches(>50ms) %d",
             fps, frameMs, blitMs, blitPct, worstMs, (int)hitch);
    }

    BlitGeometryLog();
    return true;
}

} // namespace ckperf
