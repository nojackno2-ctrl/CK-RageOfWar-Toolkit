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
// The exceptions are guard.cpp and arrayguard.cpp, which change behaviour on purpose:
// guard.cpp null-guards the script write-back stores that produced the first captured
// crash; arrayguard.cpp page-validity-guards the grid-slot read that an oversized attack
// order was found to hit (0x004AA5C9). Both verify the original bytes before patching,
// refuse anything they do not recognise, count every access they suppress, and can be
// switched off individually (guard=0, arrayguard=0).

#pragma once

#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <psapi.h>
#include <stdint.h>
#include <string.h>

namespace ckperf {

// ---------------------------------------------------------------- configuration

// Length of the script-channel authentication token, in characters. CKToolkit generates
// a fresh one per injection and passes it through the same options channel as every
// other setting; script.cpp compares it on every request.
enum { kScriptTokenChars = 32 };

struct Config {
    wchar_t outDir[MAX_PATH];   // where reports land; never the game directory (Program Files is read-only)
    bool    crashReports;       // vectored exception handler + text report
    bool    miniDumps;          // also write a .dmp next to the text report
    bool    telemetry;          // background memory/handle sampler
    bool    frameTiming;        // IAT hook on SetDIBitsToDevice for frame + blit cost
    bool    nullGuard;          // site-specific code cave on the first crash found
    bool    nullStoreRepair;    // generic: skip engine stores that target the null page
    bool    arrayGuard;         // site-specific code cave for the 0x004AA5C9 grid-slot read
    bool    scriptChannel;      // runtime VS script execution channel (see script.cpp)
    char    scriptToken[kScriptTokenChars + 1];   // shared secret for that channel
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
// Exercises the bounded EIP code-window reader used by WriteReport. In particular,
// EIP 0..7 must be rejected rather than underflowing to the top of the address space.
bool CrashSelfTest();
void CrashUninstall();

// High-resolution runtime safety. Installed from DllMain while CKToolkit still
// holds the game entry point, before any engine window or CVXVisible::View call.
void HighResolutionInstallEarly();
void HighResolutionInstallDeferred();
void HighResolutionLogStatus();
// Drains hires.cpp per-redirect hit counters into the log; no-op when the sidecar is not installed; safe to call every telemetry tick because it only writes when something changed.
void HighResolutionLogHitCounts();
// Measures the live CVXVisible dynamic rectangle count to validate whether the unverified 127-slot consumer loop cap ever drops rectangles.
void HighResolutionLogVisibleCount();

// Live handle-table occupancy: battle scale. -1 when the census is unavailable.
long CensusLiveObjects();
long CensusPeakObjects();

void TelemetryStart();
void TelemetryStop();

// Installs the runtime null-guard described in guard.cpp. Verifies the original
// bytes first and refuses to patch anything it does not recognise.
void GuardInstall();
long GuardSuppressedCount();

// Installs the runtime grid-slot-read guard described in arrayguard.cpp (the
// 0x004AA5C9 out-of-range read hit by an oversized attack order). Same verify-first,
// refuse-if-unrecognised discipline as GuardInstall.
void ArrayGuardInstall();
long ArrayGuardSuppressedCount();

// Generic null-store repair; see nullstore.cpp for why this is narrow on purpose.
void NullStoreInit();
// Asks the OS for a zero-filled page at address 0. If granted, the whole fault-repair
// path becomes dormant because nothing faults any more. Returns true on success.
bool NullPageTryMap();
// Executes a real null store from a scratch page and verifies the handler skipped it.
// Disables the repair if it cannot be proven to work. Returns true on success.
bool NullStoreSelfTest();
bool NullStoreTryRepair(EXCEPTION_POINTERS* ep, bool& firstTimeAtThisSite, unsigned& resumeEip);
long NullStoreCount();
void NullStoreLogSites();
int  NullStoreDescribeSites(char* buf, int cap, int pos);

// Narrow repair for script-VM assignment stores whose packed lvalue contains a valid
// objectId but a corrupted byteOffset. It acts only after an exact verified store AVs.
void VmLvalueInit();
bool VmLvalueTryRepair(EXCEPTION_POINTERS* ep, bool& firstTimeAtThisSite, unsigned& resumeEip);
long VmLvalueRepairCount();
void VmLvalueLogSites();
int  VmLvalueDescribeSites(char* buf, int cap, int pos);

// ------------------------------------------------------- runtime script channel
//
// See script.cpp for the full rationale and the reverse-engineering table. In short:
// the engine's 20 hard-coded scdebug keys are not enough to reach 18 cheats, so the
// trainer stops going through keys and hands script text straight to the engine's own
// compiler on the engine's own thread.
//
// These status codes cross the process boundary; CKToolkit's ScriptChannel.cs mirrors
// them exactly, so the numbers are part of the contract and must not be renumbered.
enum ScriptStatus {
    kScriptOk              = 0,   // compiled and ran synchronously
    kScriptScheduled       = 1,   // latent script handed to the VM scheduler
    kScriptCompileError    = 2,   // the engine's compiler rejected the source
    kScriptNotInGame       = 3,   // no live session; nothing was executed
    kScriptChannelDisabled = 4,   // signature mismatch, self-test failure, or not requested
    kScriptBusy            = 5,   // a previous script is still in flight
    kScriptTimedOut        = 6,   // no frame was drawn, so the pump never ran
    kScriptRejected        = 7,   // malformed or unauthenticated request
    kScriptFaulted         = 8,   // the engine faulted; the exception was contained
};

// Verifies every engine entry point, proves the compiler works, then starts the pipe
// listener. Any mismatch disables the channel permanently and logs why.
void ScriptChannelInstall();
void ScriptChannelUninstall();
// Compiles a harmless probe script and releases it without running it.
bool ScriptChannelSelfTest();
// Drains the single pending request. MUST be called on the engine's main thread; the
// frame hook in frames.cpp is the call site.
void ScriptChannelPump();
bool ScriptChannelEnabled();
long ScriptChannelExecutedCount();
long ScriptChannelRejectedCount();

void FrameTimingInstall();
void FrameTimingUninstall();
// Called by the telemetry thread once per period; drains the frame counters and
// appends one line to the log. Returns false when frame timing is not installed.
bool FrameTimingDrainAndLog(double periodSeconds);
// Logs the maximum blit dimensions and distinct blit geometries observed; only writes when numbers change.
void BlitGeometryLog();

// ----------------------------------------------------------------------- helpers

// Reads sizeof(T) bytes at addr, but only if the page is committed and readable.
// Never faults, never installs a SEH frame in the caller.
bool SafeRead(uintptr_t addr, void* dst, size_t len);
// Proves SafeRead accepts an ordinary committed buffer and rejects a wrapping range.
bool SafeReadSelfTest();

// Appends to a fixed buffer without ever overflowing it. Returns chars written.
int  Append(char* buf, int cap, int pos, const char* fmt, ...);

// Converts a wide string to UTF-8 for use with the narrow %s. Never fails silently:
// an unconvertible path yields a visible placeholder rather than an empty line.
// Do not reach for %S instead -- see the comment on the implementation.
const char* WideToUtf8(const wchar_t* src, char* dst, int cap);

} // namespace ckperf
