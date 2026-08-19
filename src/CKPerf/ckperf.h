// ckperf.h — shared declarations for the CK-RageOfWar runtime diagnostic layer.
//
// This DLL is injected into the 32-bit "Celtic kings.exe" by CKToolkit. It never
// modifies a single byte on disk; everything here lives and dies with the process.
//
// Phase 0 scope is DIAGNOSIS ONLY:
//   * a vectored exception handler that records the real fault address, because the
//     engine installs its own unhandled-exception filter and calls SetErrorMode, so
//     nothing ever reaches WER and the user just sees the game vanish;
//   * memory telemetry, to settle the "is the crash address-space exhaustion?" question;
//   * passive timing of the one and only GDI blit call site, to find out how much of
//     the main thread's time is spent outside the game module.
//
// Every diagnostic hook is measure-only and every exception is re-thrown with
// EXCEPTION_CONTINUE_SEARCH, so observation alone never alters engine behaviour.
//
// The ONE exception is guard.cpp, which does change behaviour on purpose: it null-guards
// the script write-back stores that produced the first captured crash. It verifies the
// original bytes before patching, refuses anything it does not recognise, counts every
// store it suppresses, and can be switched off with the guard=0 option.

#pragma once

#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <psapi.h>
#include <stdint.h>
#include <string.h>

namespace ckperf {

// ---------------------------------------------------------------- configuration

struct Config {
    wchar_t outDir[MAX_PATH];   // where reports land; never the game directory (Program Files is read-only)
    bool    crashReports;       // vectored exception handler + text report
    bool    miniDumps;          // also write a .dmp next to the text report
    bool    telemetry;          // background memory/handle sampler
    bool    frameTiming;        // IAT hook on SetDIBitsToDevice for frame + blit cost
    bool    nullGuard;          // site-specific code cave on the first crash found
    bool    nullStoreRepair;    // generic: skip engine stores that target the null page
    int     maxReports;         // stop writing after this many, so a repeating fault cannot fill the disk
    int     telemetryMs;        // telemetry sample period
};

extern Config g_cfg;

// selfModule is this DLL's own handle; the settings file lives beside it. That matters
// for attach mode, where we inject into a process someone else started (Steam, usually)
// and therefore cannot hand anything in through the environment.
void LoadConfig(HMODULE selfModule);

// ---------------------------------------------------------------------- logging
//
// Line-buffered append to <outDir>\ckperf.log. Safe to call from any thread.
// Deliberately not safe to call from inside the exception handler; the crash path
// writes its own file with raw WriteFile instead.

void LogInit();
void LogShutdown();
void Logf(const char* fmt, ...);

// Every line is flushed to disk as it is written. A process that dies mid-frame must
// not take the last few seconds of telemetry with it -- those are exactly the seconds
// that explain the death.
const wchar_t* LogFilePath();
// 0 when the log opened; otherwise the Win32 error. Reported inside crash reports so a
// silent logging failure can never again cost a whole play session.
unsigned LogOpenError();

// ------------------------------------------------------------------ module table
//
// Resolving an address to "module+offset" normally means GetModuleHandleEx, which
// takes the loader lock. Taking the loader lock inside a crash handler deadlocks the
// process if the fault happened while the lock was already held, and then we lose the
// very report we came for. So the module list is snapshotted up front and refreshed
// from the telemetry thread; the handler does nothing but a table lookup.

struct ModuleEntry {
    uintptr_t base;
    uintptr_t size;
    uintptr_t textStart;   // executable range, for return-address plausibility checks
    uintptr_t textEnd;
    char      name[64];
};

void  ModulesRefresh();
// Returns the entry containing addr, or nullptr. Lock-free read of a stable snapshot.
const ModuleEntry* ModuleForAddress(uintptr_t addr);
// Formats "name+0x1234" (or "0x00000000" when unknown) into buf.
void  DescribeAddress(uintptr_t addr, char* buf, size_t cch);
// The game image itself, i.e. "Celtic kings.exe". Null before ModulesRefresh().
const ModuleEntry* GameModule();

// ------------------------------------------------------------------- subsystems

void CrashInstall();
// Loads dbghelp for MiniDumpWriteDump. Split out of CrashInstall because it calls
// LoadLibrary, which must not run while DllMain still holds the loader lock.
void CrashLoadSymbolHelper();
void CrashUninstall();

void TelemetryStart();
void TelemetryStop();

// Installs the runtime null-guard described in guard.cpp. Verifies the original
// bytes first and refuses to patch anything it does not recognise.
void GuardInstall();
long GuardSuppressedCount();

// Generic null-store repair; see nullstore.cpp for why this is narrow on purpose.
void NullStoreInit();
// Executes a real null store from a scratch page and verifies the handler skipped it.
// Disables the repair if it cannot be proven to work. Returns true on success.
bool NullStoreSelfTest();
bool NullStoreTryRepair(EXCEPTION_POINTERS* ep, bool& firstTimeAtThisSite, unsigned& resumeEip);
long NullStoreCount();
void NullStoreLogSites();
int  NullStoreDescribeSites(char* buf, int cap, int pos);

void FrameTimingInstall();
void FrameTimingUninstall();
// Called by the telemetry thread once per period; drains the frame counters and
// appends one line to the log. Returns false when frame timing is not installed.
bool FrameTimingDrainAndLog(double periodSeconds);

// ----------------------------------------------------------------------- helpers

// Reads sizeof(T) bytes at addr, but only if the page is committed and readable.
// Never faults, never installs a SEH frame in the caller.
bool SafeRead(uintptr_t addr, void* dst, size_t len);

// Appends to a fixed buffer without ever overflowing it. Returns chars written.
int  Append(char* buf, int cap, int pos, const char* fmt, ...);

} // namespace ckperf
