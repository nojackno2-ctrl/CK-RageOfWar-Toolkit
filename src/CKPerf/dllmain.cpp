// dllmain.cpp — entry point for the injected diagnostic layer.
//
// Load order matters here. The vectored exception handler goes in during DllMain, as
// early as it possibly can, because a fault that happens before it is installed is a
// fault we lose. Everything that could touch the loader lock -- LoadLibrary for
// dbghelp, walking the module list, patching the import table -- is pushed onto a
// separate init thread, because DllMain already holds that lock and re-entering it
// through an unrelated path is the classic way to deadlock a process at startup.

#include "ckperf.h"

extern "C" __declspec(dllexport) volatile LONG CKPerfStartupStep = 0;

namespace ckperf {

static HANDLE g_initThread = nullptr;
static BOOL   g_dpiAwareAtAttach = FALSE;
static DWORD  g_dpiAwareError = ERROR_SUCCESS;

static DWORD WINAPI InitThread(LPVOID) {
    // Now that the loader lock is released, the rest is safe.
    ModulesRefresh();

    const ModuleEntry* game = GameModule();
    if (game) {
        Logf("game module: %s base 0x%08X size 0x%X, code 0x%08X-0x%08X",
             game->name, (unsigned)game->base, (unsigned)game->size,
             (unsigned)game->textStart, (unsigned)game->textEnd);
        if (game->base != 0x00400000) {
            // The 2004 build has no relocation directory, so this should never move.
            // If it ever does, every hard-coded VA in this project is off by a delta
            // and the crash reports would silently point at the wrong instructions.
            Logf("game module: WARNING image base is not 0x00400000 -- every recorded VA "
                 "in docs/reverse-engineering-notes.md is shifted by 0x%X.",
                 (unsigned)(game->base - 0x00400000));
        }
    } else {
        Logf("game module: not identified; address descriptions will be module-relative only.");
    }

    CrashLoadSymbolHelper();
    NullPageTryMap();
    VmLvalueInit();
    NullStoreSelfTest();
    GuardInstall();
    ArrayGuardInstall();
    FrameTimingInstall();
    TelemetryStart();

    Logf("ckperf ready.");
    HighResolutionInstallDeferred();
    return 0;
}

static void Startup(HMODULE self) {
    CKPerfStartupStep = 1;
    // CKToolkit injects this DLL while the executable entry point is held in a
    // two-byte self-loop.  This is therefore earlier than any engine window or
    // GDI surface creation, which is the only reliable moment to opt this 2004
    // non-manifested process out of Windows DPI virtualisation.  Without it a
    // 3840x2160 desktop at 150% scaling is exposed to the engine as 2560x1440.
    g_dpiAwareAtAttach = SetProcessDPIAware();
    g_dpiAwareError = GetLastError();
    CKPerfStartupStep = 2;

    DisableThreadLibraryCalls(self);

    LoadConfig(self);
    CKPerfStartupStep = 3;
    LogInit();
    CKPerfStartupStep = 4;
    Logf("DPI awareness requested before entry point: %s (GetLastError=%u)",
         g_dpiAwareAtAttach ? "applied" : "already set or refused", g_dpiAwareError);
    // Keep diagnostics available for every refusal or early-install failure. This
    // still runs inside the injected LoadLibrary call while CKToolkit holds the
    // executable entry point, so no engine instruction can observe partial state.
    HighResolutionInstallEarly();
    CKPerfStartupStep = 5;
    HighResolutionLogStatus();

    SYSTEMTIME st;
    GetLocalTime(&st);
    Logf("ckperf attached to pid %u at %04d-%02d-%02d %02d:%02d:%02d",
         (unsigned)GetCurrentProcessId(), st.wYear, st.wMonth, st.wDay,
         st.wHour, st.wMinute, st.wSecond);
    char logPath[MAX_PATH * 3];
    Logf("log file: %s  (flushed after every line)",
         WideToUtf8(LogFilePath(), logPath, (int)sizeof(logPath)));
    Logf("options: crash=%d dump=%d telemetry=%d frames=%d guard=%d repair=%d arrayguard=%d maxreports=%d telemetryms=%d",
         g_cfg.crashReports ? 1 : 0, g_cfg.miniDumps ? 1 : 0,
         g_cfg.telemetry ? 1 : 0, g_cfg.frameTiming ? 1 : 0, g_cfg.nullGuard ? 1 : 0, g_cfg.nullStoreRepair ? 1 : 0,
         g_cfg.arrayGuard ? 1 : 0, g_cfg.maxReports, g_cfg.telemetryMs);

    // Installed before anything else can fault.
    NullStoreInit();
    bool safeReadOk = SafeReadSelfTest();
    bool crashWindowOk = safeReadOk && CrashSelfTest();
    if (!safeReadOk || !crashWindowOk) {
        // Null-store repair and reporting share SafeRead and the same VEH. If either
        // safety proof fails, observing an engine fault could create a nested fault in
        // this DLL, which is worse than leaving the original exception untouched.
        g_cfg.crashReports = false;
        g_cfg.nullStoreRepair = false;
        Logf("diagnostic safety self-test FAILED (SafeRead=%d, code-window=%d). "
             "Crash reporting and null-store repair are DISABLED for this session.",
             safeReadOk ? 1 : 0, crashWindowOk ? 1 : 0);
    } else {
        Logf("diagnostic safety self-test passed -- wrapping reads and EIP 0..7 code windows are rejected.");
    }
    CrashInstall();

    g_initThread = CreateThread(nullptr, 0, InitThread, nullptr, 0, nullptr);
    CKPerfStartupStep = 6;
}

static void Shutdown(bool processExiting) {
    if (processExiting) {
        // The process is on its way out and other threads are already dead. Touching
        // synchronisation objects here can hang the exit, so only flush and leave.
        VmLvalueLogSites();
        NullStoreLogSites();
        Logf("process exiting.");
        LogShutdown();
        return;
    }

    TelemetryStop();
    FrameTimingUninstall();
    CrashUninstall();
    if (g_initThread) {
        WaitForSingleObject(g_initThread, 2000);
        CloseHandle(g_initThread);
        g_initThread = nullptr;
    }
    LogShutdown();
}

} // namespace ckperf

BOOL APIENTRY DllMain(HMODULE hModule, DWORD reason, LPVOID lpReserved) {
    switch (reason) {
        case DLL_PROCESS_ATTACH: ckperf::Startup(hModule); break;
        case DLL_PROCESS_DETACH: ckperf::Shutdown(lpReserved != nullptr); break;
        default: break;
    }
    return TRUE;
}

// Exported so the injector can confirm the DLL is the build it expects rather than a
// stale copy left in the output directory by an earlier run.
extern "C" __declspec(dllexport) unsigned int CKPerfVersion() {
    return 0x00000100;   // 0.1.0 -- diagnostic only, no engine behaviour is modified
}
