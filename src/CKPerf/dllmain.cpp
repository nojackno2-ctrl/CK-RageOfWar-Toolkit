// dllmain.cpp — entry point for the injected diagnostic layer.
//
// Load order matters here. The vectored exception handler goes in during DllMain, as
// early as it possibly can, because a fault that happens before it is installed is a
// fault we lose. Everything that could touch the loader lock -- LoadLibrary for
// dbghelp, walking the module list, patching the import table -- is pushed onto a
// separate init thread, because DllMain already holds that lock and re-entering it
// through an unrelated path is the classic way to deadlock a process at startup.

#include "ckperf.h"

namespace ckperf {

static HANDLE g_initThread = nullptr;

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
    NullStoreSelfTest();
    GuardInstall();
    FrameTimingInstall();
    TelemetryStart();

    Logf("ckperf ready.");
    return 0;
}

static void Startup(HMODULE self) {
    DisableThreadLibraryCalls(self);

    LoadConfig(self);
    LogInit();

    SYSTEMTIME st;
    GetLocalTime(&st);
    Logf("ckperf attached to pid %u at %04d-%02d-%02d %02d:%02d:%02d",
         (unsigned)GetCurrentProcessId(), st.wYear, st.wMonth, st.wDay,
         st.wHour, st.wMinute, st.wSecond);
    Logf("log file: %S  (flushed after every line)", LogFilePath());
    Logf("options: crash=%d dump=%d telemetry=%d frames=%d guard=%d repair=%d maxreports=%d telemetryms=%d",
         g_cfg.crashReports ? 1 : 0, g_cfg.miniDumps ? 1 : 0,
         g_cfg.telemetry ? 1 : 0, g_cfg.frameTiming ? 1 : 0, g_cfg.nullGuard ? 1 : 0, g_cfg.nullStoreRepair ? 1 : 0,
         g_cfg.maxReports, g_cfg.telemetryMs);

    // Installed before anything else can fault.
    NullStoreInit();
    CrashInstall();

    g_initThread = CreateThread(nullptr, 0, InitThread, nullptr, 0, nullptr);
}

static void Shutdown(bool processExiting) {
    if (processExiting) {
        // The process is on its way out and other threads are already dead. Touching
        // synchronisation objects here can hang the exit, so only flush and leave.
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
