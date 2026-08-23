// common.cpp — configuration, logging, module table and the safe-read helpers.

#include "ckperf.h"
#include <stdio.h>
#include <stdarg.h>
#include <shlobj.h>

namespace ckperf {

Config g_cfg = {};

// ---------------------------------------------------------------- configuration
//
// Two sources, in this order of precedence:
//
//   1. ckperf.ini sitting next to this DLL. This is the only channel that works for
//      ATTACH mode, where the game was started by Steam and we inject afterwards --
//      by then its environment block is fixed and nothing we do can change it.
//   2. The CKPERF_OUT / CKPERF_OPTS environment variables, which the injector sets on
//      the child in LAUNCH mode. They win when present, so a launch can override the
//      file without rewriting it.
//
// Either way, nothing is read from or written to the game directory: the game lives
// under Program Files and is not writable without elevation.

static void EnvW(const wchar_t* name, wchar_t* dst, size_t cch, const wchar_t* fallback) {
    DWORD n = GetEnvironmentVariableW(name, dst, (DWORD)cch);
    if (n == 0 || n >= cch) wcsncpy_s(dst, cch, fallback, _TRUNCATE);
}

static bool OptFlag(const wchar_t* opts, const wchar_t* key, bool dflt) {
    const wchar_t* p = wcsstr(opts, key);
    if (!p) return dflt;
    p += wcslen(key);
    if (*p != L'=') return dflt;
    return p[1] != L'0';
}

static int OptInt(const wchar_t* opts, const wchar_t* key, int dflt) {
    const wchar_t* p = wcsstr(opts, key);
    if (!p) return dflt;
    p += wcslen(key);
    if (*p != L'=') return dflt;
    int v = _wtoi(p + 1);
    return v > 0 ? v : dflt;
}

// Locates ckperf.ini beside this DLL. Returns false when the path cannot be built.
static bool SettingsPath(HMODULE self, wchar_t* dst, size_t cch) {
    wchar_t modulePath[MAX_PATH];
    DWORD n = GetModuleFileNameW(self, modulePath, MAX_PATH);
    if (n == 0 || n >= MAX_PATH) return false;
    wchar_t* slash = wcsrchr(modulePath, L'\\');
    if (!slash) return false;
    *slash = 0;
    return swprintf_s(dst, cch, L"%s\\ckperf.ini", modulePath) > 0;
}

void LoadConfig(HMODULE selfModule) {
    wchar_t fallbackDir[MAX_PATH] = L".";
    wchar_t* local = nullptr;
    if (SUCCEEDED(SHGetKnownFolderPath(FOLDERID_LocalAppData, 0, nullptr, &local)) && local) {
        swprintf_s(fallbackDir, L"%s\\CKToolkit\\diag", local);
        CoTaskMemFree(local);
    }

    // File first, environment second. GetPrivateProfileStringW copes with a UTF-16
    // ini, which is what the injector writes so non-ASCII user names survive.
    wchar_t iniPath[MAX_PATH];
    wchar_t fileOut[MAX_PATH]  = L"";
    wchar_t fileOpts[512]      = L"";
    if (SettingsPath(selfModule, iniPath, MAX_PATH)) {
        GetPrivateProfileStringW(L"ckperf", L"out",  L"", fileOut,  MAX_PATH, iniPath);
        GetPrivateProfileStringW(L"ckperf", L"opts", L"", fileOpts, 512,      iniPath);
    }

    EnvW(L"CKPERF_OUT", g_cfg.outDir, MAX_PATH, fileOut[0] ? fileOut : fallbackDir);

    wchar_t opts[512] = L"";
    EnvW(L"CKPERF_OPTS", opts, 512, fileOpts);

    g_cfg.crashReports = OptFlag(opts, L"crash",      true);
    g_cfg.miniDumps    = OptFlag(opts, L"dump",       true);
    g_cfg.telemetry    = OptFlag(opts, L"telemetry",  true);
    g_cfg.frameTiming  = OptFlag(opts, L"frames",     true);
    g_cfg.nullGuard    = OptFlag(opts, L"guard",      true);
    g_cfg.nullStoreRepair = OptFlag(opts, L"repair",  true);
    g_cfg.arrayGuard   = OptFlag(opts, L"arrayguard", true);
    g_cfg.maxReports   = OptInt (opts, L"maxreports",   20);
    g_cfg.telemetryMs  = OptInt (opts, L"telemetryms", 1000);

    // SHCreateDirectoryExW builds the whole chain and is happy if it already exists.
    SHCreateDirectoryExW(nullptr, g_cfg.outDir, nullptr);
}

// ---------------------------------------------------------------------- logging

static HANDLE           g_log = INVALID_HANDLE_VALUE;
static CRITICAL_SECTION g_logLock;
static bool             g_logLockReady = false;
static wchar_t          g_logPath[MAX_PATH] = L"";
static unsigned         g_logOpenError = 0;

void LogInit() {
    InitializeCriticalSection(&g_logLock);
    g_logLockReady = true;

    // One file per session, named by pid and start time.
    //
    // The previous design used a single fixed ckperf.log opened with CREATE_ALWAYS,
    // and it cost a whole play session: a second game run truncated the first run's
    // log, and when one run failed to open the file at all it failed SILENTLY -- the
    // crash report was written, but every telemetry line leading up to the crash was
    // gone. A per-session name removes the truncation hazard entirely, and the open
    // error is now recorded so a failure can never again be invisible.
    SYSTEMTIME st;
    GetLocalTime(&st);
    swprintf_s(g_logPath, L"%s\\ckperf-%04d%02d%02d-%02d%02d%02d-pid%u.log",
               g_cfg.outDir, st.wYear, st.wMonth, st.wDay,
               st.wHour, st.wMinute, st.wSecond, (unsigned)GetCurrentProcessId());

    // GENERIC_WRITE, not FILE_APPEND_DATA: append-only access is not sufficient for
    // every create disposition, and the failure mode is an unhelpful ACCESS_DENIED.
    g_log = CreateFileW(g_logPath, GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE,
                        nullptr, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (g_log == INVALID_HANDLE_VALUE) g_logOpenError = GetLastError();
}

const wchar_t* LogFilePath() { return g_logPath; }
unsigned       LogOpenError() { return g_logOpenError; }

void LogShutdown() {
    if (g_log != INVALID_HANDLE_VALUE) {
        FlushFileBuffers(g_log);
        CloseHandle(g_log);
        g_log = INVALID_HANDLE_VALUE;
    }
    if (g_logLockReady) {
        DeleteCriticalSection(&g_logLock);
        g_logLockReady = false;
    }
}

void Logf(const char* fmt, ...) {
    if (g_log == INVALID_HANDLE_VALUE) return;

    char line[1024];
    SYSTEMTIME st;
    GetLocalTime(&st);
    int n = _snprintf_s(line, _TRUNCATE, "[%02d:%02d:%02d.%03d] ",
                        st.wHour, st.wMinute, st.wSecond, st.wMilliseconds);
    va_list ap;
    va_start(ap, fmt);
    int m = _vsnprintf_s(line + n, sizeof(line) - n - 3, _TRUNCATE, fmt, ap);
    va_end(ap);
    if (m < 0) m = (int)strlen(line + n);
    n += m;
    line[n++] = 13;
    line[n++] = 10;

    EnterCriticalSection(&g_logLock);
    DWORD written = 0;
    WriteFile(g_log, line, (DWORD)n, &written, nullptr);
    // Flush every line. Telemetry runs about twice a second, so this costs nothing
    // measurable, and it means an abrupt process death loses no history at all --
    // which is the entire point of writing the history in the first place.
    FlushFileBuffers(g_log);
    LeaveCriticalSection(&g_logLock);
}

// ------------------------------------------------------------------ module table
//
// Double-buffered so a refresh never tears a lookup running concurrently: the writer
// fills the inactive slot, then flips the index with a single interlocked store.

static const int     kMaxModules = 192;
static ModuleEntry   g_mods[2][kMaxModules];
static volatile LONG g_modCount[2] = { 0, 0 };
static volatile LONG g_modActive = 0;
static const ModuleEntry* g_game = nullptr;

static void FillFromImage(ModuleEntry& e) {
    // Default to "the whole image is code" and narrow it if the PE headers are readable.
    e.textStart = e.base;
    e.textEnd   = e.base + e.size;

    IMAGE_DOS_HEADER dos;
    if (!SafeRead(e.base, &dos, sizeof(dos)) || dos.e_magic != IMAGE_DOS_SIGNATURE) return;

    IMAGE_NT_HEADERS32 nt;
    if (!SafeRead(e.base + dos.e_lfanew, &nt, sizeof(nt)) || nt.Signature != IMAGE_NT_SIGNATURE) return;

    uintptr_t secs = e.base + dos.e_lfanew + offsetof(IMAGE_NT_HEADERS32, OptionalHeader)
                   + nt.FileHeader.SizeOfOptionalHeader;
    uintptr_t lo = 0, hi = 0;
    for (WORD i = 0; i < nt.FileHeader.NumberOfSections && i < 96; ++i) {
        IMAGE_SECTION_HEADER sh;
        if (!SafeRead(secs + i * sizeof(sh), &sh, sizeof(sh))) return;
        if (!(sh.Characteristics & IMAGE_SCN_MEM_EXECUTE)) continue;
        uintptr_t s = e.base + sh.VirtualAddress;
        uintptr_t x = s + sh.Misc.VirtualSize;
        if (lo == 0 || s < lo) lo = s;
        if (x > hi) hi = x;
    }
    if (lo && hi > lo) { e.textStart = lo; e.textEnd = hi; }
}

void ModulesRefresh() {
    LONG target = 1 - InterlockedCompareExchange(&g_modActive, 0, 0);
    ModuleEntry* dst = g_mods[target];
    int n = 0;

    HMODULE mods[kMaxModules];
    DWORD needed = 0;
    // K32EnumProcessModules is the kernel32 forwarder, so there is no psapi.dll
    // load-order question to worry about inside DllMain.
    if (K32EnumProcessModules(GetCurrentProcess(), mods, sizeof(mods), &needed)) {
        int count = (int)(needed / sizeof(HMODULE));
        if (count > kMaxModules) count = kMaxModules;
        for (int i = 0; i < count; ++i) {
            MODULEINFO mi = {};
            if (!K32GetModuleInformation(GetCurrentProcess(), mods[i], &mi, sizeof(mi))) continue;

            ModuleEntry& e = dst[n];
            e.base = (uintptr_t)mi.lpBaseOfDll;
            e.size = mi.SizeOfImage;

            char baseName[MAX_PATH] = "";
            K32GetModuleBaseNameA(GetCurrentProcess(), mods[i], baseName, MAX_PATH);
            strncpy_s(e.name, baseName, _TRUNCATE);

            FillFromImage(e);
            ++n;
        }
    }

    InterlockedExchange(&g_modCount[target], n);
    InterlockedExchange(&g_modActive, target);

    // Cache the game image. Comparing against the main module handle is more robust
    // than a name compare, which would break the moment anyone renames the exe.
    HMODULE self = GetModuleHandleW(nullptr);
    for (int i = 0; i < n; ++i) {
        if (dst[i].base == (uintptr_t)self) { g_game = &dst[i]; break; }
    }
}

const ModuleEntry* ModuleForAddress(uintptr_t addr) {
    LONG active = InterlockedCompareExchange(&g_modActive, 0, 0);
    const ModuleEntry* tab = g_mods[active];
    LONG n = InterlockedCompareExchange(&g_modCount[active], 0, 0);
    for (LONG i = 0; i < n; ++i) {
        if (addr >= tab[i].base && addr < tab[i].base + tab[i].size) return &tab[i];
    }
    return nullptr;
}

const ModuleEntry* GameModule() { return g_game; }

void DescribeAddress(uintptr_t addr, char* buf, size_t cch) {
    const ModuleEntry* m = ModuleForAddress(addr);
    if (m) _snprintf_s(buf, cch, _TRUNCATE, "%s+0x%X", m->name, (unsigned)(addr - m->base));
    else   _snprintf_s(buf, cch, _TRUNCATE, "0x%08X (unmapped)", (unsigned)addr);
}

// ----------------------------------------------------------------------- helpers

bool SafeRead(uintptr_t addr, void* dst, size_t len) {
    if (!addr || !dst || !len) return false;

    // The 2026-08-23 field crash reached this helper with addr=0xFFFFFFF8 and len=32
    // after WriteReport computed eip-8 for EIP 0. Without an overflow check `end`
    // wrapped to 0x18, the validation loop ran zero times, and memcpy faulted inside
    // ckperf.dll while the crash handler was already active.
    if (len > UINTPTR_MAX - addr) return false;

    // VirtualQuery rather than a SEH probe: the crash handler must not raise a nested
    // exception, and a nested exception inside a VEH is exactly how a diagnostic tool
    // turns a recoverable fault into an unrecoverable one.
    MEMORY_BASIC_INFORMATION mbi;
    uintptr_t end = addr + len;
    uintptr_t p = addr;
    while (p < end) {
        if (!VirtualQuery((LPCVOID)p, &mbi, sizeof(mbi))) return false;
        if (mbi.State != MEM_COMMIT) return false;
        DWORD prot = mbi.Protect & 0xFF;
        if (prot == PAGE_NOACCESS || (mbi.Protect & PAGE_GUARD)) return false;
        uintptr_t regionBase = (uintptr_t)mbi.BaseAddress;
        if (mbi.RegionSize > UINTPTR_MAX - regionBase) return false;
        uintptr_t next = regionBase + mbi.RegionSize;
        if (next <= p) return false;  // overflow or a malformed/non-progressing region
        p = next;
    }
    memcpy(dst, (const void*)addr, len);
    return true;
}

bool SafeReadSelfTest() {
    unsigned char src[32];
    unsigned char dst[32] = {};
    for (unsigned i = 0; i < sizeof(src); ++i) src[i] = (unsigned char)(i ^ 0xA5u);

    if (!SafeRead((uintptr_t)src, dst, sizeof(src))) return false;
    if (memcmp(src, dst, sizeof(src)) != 0) return false;

    // Exact shape from the field failure: (EIP 0 - 8) + 32 wraps to 0x18.
    if (SafeRead(UINTPTR_MAX - 7u, dst, sizeof(dst))) return false;
    if (SafeRead((uintptr_t)src, nullptr, sizeof(src))) return false;
    return true;
}

int Append(char* buf, int cap, int pos, const char* fmt, ...) {
    if (pos >= cap - 1) return pos;
    va_list ap;
    va_start(ap, fmt);
    int n = _vsnprintf_s(buf + pos, cap - pos, _TRUNCATE, fmt, ap);
    va_end(ap);

    // _vsnprintf_s returns -1 for two very different reasons, and conflating them
    // cost a whole fault report. The old code mapped -1 to `cap - 1`, which is right
    // for truncation (the buffer really is full) but catastrophic for a formatting
    // failure: on 2026-08-22 the diagnostics directory was C:\...\Desktop\紀錄, the
    // "%S" conversion of that path failed in the "C" locale, this returned 65535, and
    // every later Append in the report saw pos >= cap - 1 and wrote nothing. What
    // landed on disk was three lines followed by 64 KB of NULs from the static buffer.
    //
    // Both cases leave the buffer null-terminated (a failed conversion leaves it
    // empty), so measure what actually got written instead of guessing. Truncation
    // still walks pos to the end of what fit; a failed append now leaves pos alone and
    // the rest of the report still gets written.
    if (n < 0) {
        buf[cap - 1] = 0;
        return pos + (int)strlen(buf + pos);
    }
    return pos + n;
}

// The narrow "%S" conversion goes through the C locale, which is "C" unless someone
// calls setlocale -- so it handles ASCII and nothing else. Every path in this DLL comes
// from the user's own directory names, so treating non-ASCII as an error case is not a
// theoretical concern; it is the normal case for this user. Convert explicitly instead,
// to UTF-8: the rest of a report is ASCII, so the file stays valid UTF-8 throughout.
const char* WideToUtf8(const wchar_t* src, char* dst, int cap) {
    if (!dst || cap <= 0) return "";
    dst[0] = 0;
    if (src) {
        int n = WideCharToMultiByte(CP_UTF8, 0, src, -1, dst, cap, nullptr, nullptr);
        if (n > 0) return dst;
    }
    // Never return an empty string: a blank line in a fault report reads as "there was
    // nothing to say", which is exactly the wrong thing to believe about a lost path.
    strncpy_s(dst, (size_t)cap, "(path could not be converted)", _TRUNCATE);
    return dst;
}

} // namespace ckperf
