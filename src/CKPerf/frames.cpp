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
    if (!g_cfg.frameTiming) return;

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
}

bool FrameTimingDrainAndLog(double periodSeconds) {
    if (!g_iatSlot) return false;

    LONG   frames = InterlockedExchange(&g_frameCount, 0);
    LONG64 blit   = InterlockedExchange64(&g_blitTicks, 0);
    LONG64 total  = InterlockedExchange64(&g_frameTicks, 0);
    LONG64 worst  = InterlockedExchange64(&g_worstFrame, 0);
    LONG   hitch  = InterlockedExchange(&g_hitches, 0);

    if (frames == 0) return true;   // menus and loading screens legitimately draw nothing

    double qpf      = (double)g_qpf.QuadPart;
    double blitMs   = qpf ? (blit  / qpf) * 1000.0 / frames : 0.0;
    double frameMs  = qpf ? (total / qpf) * 1000.0 / frames : 0.0;
    double worstMs  = qpf ? (worst / qpf) * 1000.0 : 0.0;
    double fps      = periodSeconds > 0 ? frames / periodSeconds : 0.0;
    double blitPct  = frameMs > 0 ? blitMs / frameMs * 100.0 : 0.0;

    Logf("frames: %.1f fps | frame %.2f ms | blit %.2f ms (%.0f%% of the frame) | worst %.1f ms | hitches(>50ms) %d",
         fps, frameMs, blitMs, blitPct, worstMs, (int)hitch);
    return true;
}

} // namespace ckperf
