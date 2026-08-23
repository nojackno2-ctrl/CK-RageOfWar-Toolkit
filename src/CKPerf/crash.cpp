// crash.cpp — vectored exception handler and fault report writer.
//
// Why a VEH and not WER LocalDumps or SetUnhandledExceptionFilter:
//
//   * The engine imports SetErrorMode and SetUnhandledExceptionFilter and uses both.
//     By the time an exception would reach WER it has already been swallowed, which
//     is exactly why the user sees the game vanish with no dialog and no dump.
//   * A vectored handler registered with First=1 runs BEFORE any frame-based SEH and
//     before any previously registered VEH, so it sees the fault at first chance --
//     while the faulting frame is still intact and the registers still mean something.
//
// This handler is strictly an observer. It always returns EXCEPTION_CONTINUE_SEARCH,
// so the engine handles (or fails to handle) the exception exactly as it would have
// without us. A report being written is not proof the game died: the engine may well
// recover. The report that matters is the LAST one before the process exits.

#include "ckperf.h"
#include <stdio.h>
#include <dbghelp.h>

namespace ckperf {

static PVOID         g_veh = nullptr;
static volatile LONG g_reportCount = 0;
static volatile LONG g_inHandler = 0;

// Counters for the exceptions we deliberately do not report, so the log can still
// show that the engine is, say, throwing thousands of C++ exceptions per second.
static volatile LONG g_benignCount = 0;

typedef BOOL (WINAPI* PFN_MiniDumpWriteDump)(HANDLE, DWORD, HANDLE, MINIDUMP_TYPE,
                                             PMINIDUMP_EXCEPTION_INFORMATION,
                                             PMINIDUMP_USER_STREAM_INFORMATION,
                                             PMINIDUMP_CALLBACK_INFORMATION);
static PFN_MiniDumpWriteDump g_miniDump = nullptr;

// ------------------------------------------------------------- exception triage

static bool IsFatalLooking(DWORD code) {
    switch (code) {
        case EXCEPTION_ACCESS_VIOLATION:
        case EXCEPTION_ARRAY_BOUNDS_EXCEEDED:
        case EXCEPTION_DATATYPE_MISALIGNMENT:
        case EXCEPTION_FLT_DIVIDE_BY_ZERO:
        case EXCEPTION_ILLEGAL_INSTRUCTION:
        case EXCEPTION_IN_PAGE_ERROR:
        case EXCEPTION_INT_DIVIDE_BY_ZERO:
        case EXCEPTION_PRIV_INSTRUCTION:
        case EXCEPTION_STACK_OVERFLOW:
        case EXCEPTION_NONCONTINUABLE_EXCEPTION:
            return true;
        default:
            return false;
    }
}

static const char* CodeName(DWORD code) {
    switch (code) {
        case EXCEPTION_ACCESS_VIOLATION:      return "ACCESS_VIOLATION";
        case EXCEPTION_ARRAY_BOUNDS_EXCEEDED: return "ARRAY_BOUNDS_EXCEEDED";
        case EXCEPTION_DATATYPE_MISALIGNMENT: return "DATATYPE_MISALIGNMENT";
        case EXCEPTION_FLT_DIVIDE_BY_ZERO:    return "FLT_DIVIDE_BY_ZERO";
        case EXCEPTION_ILLEGAL_INSTRUCTION:   return "ILLEGAL_INSTRUCTION";
        case EXCEPTION_IN_PAGE_ERROR:         return "IN_PAGE_ERROR";
        case EXCEPTION_INT_DIVIDE_BY_ZERO:    return "INT_DIVIDE_BY_ZERO";
        case EXCEPTION_PRIV_INSTRUCTION:      return "PRIV_INSTRUCTION";
        case EXCEPTION_STACK_OVERFLOW:        return "STACK_OVERFLOW";
        case EXCEPTION_NONCONTINUABLE_EXCEPTION: return "NONCONTINUABLE_EXCEPTION";
        case 0xE06D7363:                      return "C++ exception";
        default:                              return "unknown";
    }
}

// -------------------------------------------------------- return-address heuristic
//
// The engine is a 2004 MSVC build with mixed frame-pointer omission, so an EBP chain
// walk alone loses whole stretches of the stack. Scanning the raw stack for values
// that (a) land in a known module and (b) are preceded by something that actually
// encodes a call instruction recovers the frames FPO hid. It over-reports slightly --
// a stale slot from an earlier deeper call still looks like a return address -- which
// is the right trade for a first look at an unknown fault.

static bool LooksLikeReturnAddress(uintptr_t v) {
    const ModuleEntry* m = ModuleForAddress(v);
    if (!m || v < m->textStart || v >= m->textEnd) return false;

    unsigned char b[8];
    if (!SafeRead(v - 7, b, sizeof(b))) return false;
    // b[i] holds the byte at v-7+i, so the byte at v-N is b[7-N].
    if (b[2] == 0xE8) return true;                                  // call rel32
    if (b[1] == 0xFF && b[2] == 0x15) return true;                  // call dword ptr [imm32]
    if (b[5] == 0xFF && (b[6] & 0xF8) == 0xD0) return true;         // call reg
    if (b[4] == 0xFF && (b[5] & 0xF8) == 0x50) return true;         // call [reg+disp8]
    if (b[1] == 0xFF && (b[2] & 0xF8) == 0x90) return true;         // call [reg+disp32]
    if (b[4] == 0xFF && b[5] == 0x14) return true;                  // call [reg+reg*s]
    if (b[0] == 0xFF && b[1] == 0x94) return true;                  // call [reg+reg*s+disp32]
    if (b[5] == 0xFF && b[6] == 0x10) return true;                  // call [reg]  (vtable-ish)
    return false;
}

static bool ReadCodeWindow(uintptr_t eip, unsigned char* code, size_t len) {
    // The report prints eight bytes before EIP. For EIP 0..7, subtraction would wrap
    // into 0xFFFFFFF8..0xFFFFFFFF. SafeRead also rejects wrapping ranges, but keep the
    // semantic boundary here so a report never asks the helper an invalid question.
    if (!code || len == 0 || eip < 8) return false;
    return SafeRead(eip - 8, code, len);
}

// -------------------------------------------------------------- report formatting

static int DescribeFaultRegion(char* buf, int cap, int pos, uintptr_t addr) {
    MEMORY_BASIC_INFORMATION mbi;
    if (!VirtualQuery((LPCVOID)addr, &mbi, sizeof(mbi))) {
        return Append(buf, cap, pos, "  region        : VirtualQuery failed (address outside the address space)\r\n");
    }

    const char* state = mbi.State == MEM_COMMIT  ? "COMMIT"
                      : mbi.State == MEM_RESERVE ? "RESERVE"
                      : mbi.State == MEM_FREE    ? "FREE" : "?";
    pos = Append(buf, cap, pos, "  region        : base 0x%08X  size 0x%X  state %s  protect 0x%X  type 0x%X\r\n",
                 (unsigned)(uintptr_t)mbi.BaseAddress, (unsigned)mbi.RegionSize,
                 state, (unsigned)mbi.Protect, (unsigned)mbi.Type);

    // The distinguishing question for this project: is this a null/garbage pointer, or
    // is it a walk off the end of a real allocation? The latter is what a fixed-size
    // object pool overflowing looks like, and the engine has at least one of those
    // (the FSPtrPool diagnostic string at .data:0x00725C80).
    if (mbi.State == MEM_FREE) {
        MEMORY_BASIC_INFORMATION prev;
        uintptr_t probe = (uintptr_t)mbi.BaseAddress;
        if (probe >= 0x10000 && VirtualQuery((LPCVOID)(probe - 1), &prev, sizeof(prev)) && prev.State == MEM_COMMIT) {
            uintptr_t prevEnd = (uintptr_t)prev.BaseAddress + prev.RegionSize;
            pos = Append(buf, cap, pos,
                         "  >> the previous region ends at 0x%08X, %u bytes below the fault.\r\n"
                         "     A short overshoot past a committed block is the signature of a\r\n"
                         "     fixed-capacity pool or array being indexed past its end.\r\n",
                         (unsigned)prevEnd, (unsigned)(addr - prevEnd));
        }
    }
    if (addr < 0x10000) {
        pos = Append(buf, cap, pos,
                     "  >> fault address is in the null page; this is a null or small-offset-from-null\r\n"
                     "     dereference, i.e. an object pointer that was never assigned or was freed.\r\n");
    }
    return pos;
}

static int DescribeMemoryPressure(char* buf, int cap, int pos) {
    PROCESS_MEMORY_COUNTERS_EX pmc = {};
    pmc.cb = sizeof(pmc);
    if (K32GetProcessMemoryInfo(GetCurrentProcess(), (PROCESS_MEMORY_COUNTERS*)&pmc, sizeof(pmc))) {
        pos = Append(buf, cap, pos, "  working set   : %u MB   private %u MB   peak WS %u MB\r\n",
                     (unsigned)(pmc.WorkingSetSize >> 20),
                     (unsigned)(pmc.PrivateUsage >> 20),
                     (unsigned)(pmc.PeakWorkingSetSize >> 20));
    }

    // Largest free block matters more than total free bytes: a 32-bit process with LAA
    // can have plenty of free address space and still fail a big contiguous request.
    SYSTEM_INFO si;
    GetSystemInfo(&si);
    uintptr_t p = (uintptr_t)si.lpMinimumApplicationAddress;
    uintptr_t hi = (uintptr_t)si.lpMaximumApplicationAddress;
    SIZE_T freeTotal = 0, freeLargest = 0;
    MEMORY_BASIC_INFORMATION mbi;
    while (p < hi && VirtualQuery((LPCVOID)p, &mbi, sizeof(mbi))) {
        if (mbi.State == MEM_FREE) {
            freeTotal += mbi.RegionSize;
            if (mbi.RegionSize > freeLargest) freeLargest = mbi.RegionSize;
        }
        uintptr_t next = (uintptr_t)mbi.BaseAddress + mbi.RegionSize;
        if (next <= p) break;
        p = next;
    }
    pos = Append(buf, cap, pos, "  address space : %u MB free total, largest free block %u MB\r\n",
                 (unsigned)(freeTotal >> 20), (unsigned)(freeLargest >> 20));
    if (freeLargest < (16u << 20)) {
        pos = Append(buf, cap, pos,
                     "  >> largest free block is under 16 MB: this process is out of contiguous\r\n"
                     "     address space, and the crash is an allocation failure, not a bad pointer.\r\n");
    }
    return pos;
}

// withDump is false for repaired faults. Nine repaired sites in one and a half seconds
// meant nine half-megabyte minidumps, and the I/O alone dropped the game to 9 fps. The
// text report already carries everything a null-page access needs; the dump does not
// earn its cost when the diagnosis is "this pointer was zero".
static void WriteReport(EXCEPTION_POINTERS* ep, LONG index, bool withDump) {
    static char buf[64 * 1024];
    int cap = (int)sizeof(buf);
    int pos = 0;

    EXCEPTION_RECORD* er = ep->ExceptionRecord;
    CONTEXT*          cx = ep->ContextRecord;
    uintptr_t         eip = cx->Eip;

    SYSTEMTIME st;
    GetLocalTime(&st);

    char where[160];
    DescribeAddress(eip, where, sizeof(where));

    pos = Append(buf, cap, pos, "CKPerf fault report #%d\r\n", (int)index);
    pos = Append(buf, cap, pos, "%04d-%02d-%02d %02d:%02d:%02d.%03d   thread %u\r\n\r\n",
                 st.wYear, st.wMonth, st.wDay, st.wHour, st.wMinute, st.wSecond,
                 st.wMilliseconds, (unsigned)GetCurrentThreadId());

    // The report has to stand on its own. The first real crash this tool caught came
    // with no telemetry log at all, because the log had failed to open and said
    // nothing about it -- so the state of logging is now part of every report.
    if (LogOpenError() != 0) {
        pos = Append(buf, cap, pos,
                     "  !! the telemetry log FAILED to open (Win32 error %u), so there is no\r\n"
                     "     memory or framerate history for this session. This report is all\r\n"
                     "     there is.\r\n\r\n", LogOpenError());
    } else {
        // %s with an explicit UTF-8 conversion, never %S: a non-ASCII diagnostics
        // directory used to make this one line take the entire report down with it.
        char logPath[MAX_PATH * 3];
        pos = Append(buf, cap, pos, "  telemetry log : %s\r\n\r\n",
                     WideToUtf8(LogFilePath(), logPath, (int)sizeof(logPath)));
    }

    // Uptime separates "died during loading" from "died an hour into a battle", and
    // that distinction changes which hypotheses are even worth testing.
    if (const ModuleEntry* g = GameModule()) {
        pos = Append(buf, cap, pos, "  game module   : %s base 0x%08X size 0x%X\r\n",
                     g->name, (unsigned)g->base, (unsigned)g->size);
    }

    // Battle scale at the moment of the fault. This whole class of bug only shows up
    // when the map is busy, so a report without this number is missing its main clue.
    long live = CensusLiveObjects();
    if (live >= 0) {
        pos = Append(buf, cap, pos, "  live objects  : %ld  (peak this session %ld)\r\n",
                     live, CensusPeakObjects());
    }
    pos = Append(buf, cap, pos, "  guard         : %ld null write-backs suppressed before this fault\r\n", GuardSuppressedCount());
    pos = Append(buf, cap, pos, "  arrayguard    : %ld out-of-range grid cells rejected before this fault\r\n", ArrayGuardSuppressedCount());
    pos = Append(buf, cap, pos, "  vm lvalue     : %ld invalid assignment stores repaired so far (including this fault)\r\n", VmLvalueRepairCount());
    pos = Append(buf, cap, pos, "  exception     : 0x%08X  %s%s\r\n",
                 (unsigned)er->ExceptionCode, CodeName(er->ExceptionCode),
                 (er->ExceptionFlags & EXCEPTION_NONCONTINUABLE) ? "  (noncontinuable)" : "");
    pos = Append(buf, cap, pos, "  faulting eip  : 0x%08X   %s\r\n", (unsigned)eip, where);

    if (er->ExceptionCode == EXCEPTION_ACCESS_VIOLATION || er->ExceptionCode == EXCEPTION_IN_PAGE_ERROR) {
        ULONG_PTR op = er->NumberParameters > 0 ? er->ExceptionInformation[0] : 0;
        uintptr_t addr = er->NumberParameters > 1 ? (uintptr_t)er->ExceptionInformation[1] : 0;
        const char* what = op == 0 ? "read from" : op == 1 ? "write to" : op == 8 ? "execute at" : "access";
        pos = Append(buf, cap, pos, "  fault address : 0x%08X   (%s)\r\n", (unsigned)addr, what);
        pos = DescribeFaultRegion(buf, cap, pos, addr);
    }

    pos = Append(buf, cap, pos, "\r\n  registers\r\n");
    pos = Append(buf, cap, pos, "    eax %08X  ebx %08X  ecx %08X  edx %08X\r\n",
                 (unsigned)cx->Eax, (unsigned)cx->Ebx, (unsigned)cx->Ecx, (unsigned)cx->Edx);
    pos = Append(buf, cap, pos, "    esi %08X  edi %08X  ebp %08X  esp %08X\r\n",
                 (unsigned)cx->Esi, (unsigned)cx->Edi, (unsigned)cx->Ebp, (unsigned)cx->Esp);
    pos = Append(buf, cap, pos, "    eip %08X  eflags %08X\r\n", (unsigned)cx->Eip, (unsigned)cx->EFlags);

    // Raw bytes at the faulting instruction. With this and the .text VA the exact
    // instruction can be identified offline against the untouched Steam binary.
    unsigned char code[32];
    if (ReadCodeWindow(eip, code, sizeof(code))) {
        pos = Append(buf, cap, pos, "\r\n  code at eip-8 (fault is at +8)\r\n    ");
        for (int i = 0; i < 32; ++i) {
            pos = Append(buf, cap, pos, "%02X ", code[i]);
            if (i == 15) pos = Append(buf, cap, pos, "\r\n    ");
        }
        pos = Append(buf, cap, pos, "\r\n");
    }

    pos = Append(buf, cap, pos, "\r\n  memory\r\n");
    pos = DescribeMemoryPressure(buf, cap, pos);

    // Frame-pointer chain first: when it works it is exact and ordered.
    pos = Append(buf, cap, pos, "\r\n  ebp chain\r\n");
    uintptr_t ebp = cx->Ebp;
    for (int depth = 0; depth < 32; ++depth) {
        uintptr_t frame[2];
        if (!SafeRead(ebp, frame, sizeof(frame))) break;
        if (frame[1] == 0) break;
        char d[160];
        DescribeAddress(frame[1], d, sizeof(d));
        pos = Append(buf, cap, pos, "    [%02d] 0x%08X  %s\r\n", depth, (unsigned)frame[1], d);
        if (frame[0] <= ebp) break;   // chain must climb, or it is not a chain
        ebp = frame[0];
    }

    // Then the raw scan, which catches everything FPO dropped.
    pos = Append(buf, cap, pos, "\r\n  stack scan from esp (plausible return addresses)\r\n");
    int found = 0;
    for (uintptr_t p = cx->Esp; p < cx->Esp + 8192 && found < 48; p += 4) {
        uintptr_t v;
        if (!SafeRead(p, &v, sizeof(v))) break;
        if (!LooksLikeReturnAddress(v)) continue;
        char d[160];
        DescribeAddress(v, d, sizeof(d));
        pos = Append(buf, cap, pos, "    +%04X  0x%08X  %s\r\n", (unsigned)(p - cx->Esp), (unsigned)v, d);
        ++found;
    }
    if (found == 0) pos = Append(buf, cap, pos, "    (none -- the stack itself may be corrupted)\r\n");

    pos = NullStoreDescribeSites(buf, cap, pos);
    pos = VmLvalueDescribeSites(buf, cap, pos);

    pos = Append(buf, cap, pos,
                 "\r\n  note: first-chance faults may be repaired by the narrowly supported guards above.\r\n"
                 "        If the game kept running, this report is informational; the report that\r\n"
                 "        explains an exit is the highest-numbered fault that was not repaired.\r\n");

    wchar_t path[MAX_PATH];
    swprintf_s(path, L"%s\\ckcrash-%04d%02d%02d-%02d%02d%02d-%02d.txt",
               g_cfg.outDir, st.wYear, st.wMonth, st.wDay, st.wHour, st.wMinute, st.wSecond, (int)index);
    HANDLE h = CreateFileW(path, GENERIC_WRITE, FILE_SHARE_READ, nullptr,
                           CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (h != INVALID_HANDLE_VALUE) {
        DWORD written = 0;
        WriteFile(h, buf, (DWORD)pos, &written, nullptr);
        FlushFileBuffers(h);
        CloseHandle(h);
    }

    if (withDump && g_cfg.miniDumps && g_miniDump) {
        swprintf_s(path, L"%s\\ckcrash-%04d%02d%02d-%02d%02d%02d-%02d.dmp",
                   g_cfg.outDir, st.wYear, st.wMonth, st.wDay, st.wHour, st.wMinute, st.wSecond, (int)index);
        HANDLE hd = CreateFileW(path, GENERIC_WRITE, 0, nullptr,
                                CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
        if (hd != INVALID_HANDLE_VALUE) {
            MINIDUMP_EXCEPTION_INFORMATION mei = {};
            mei.ThreadId          = GetCurrentThreadId();
            mei.ExceptionPointers = ep;
            mei.ClientPointers    = FALSE;
            // Indirectly-referenced memory pulls in what the registers point at, which
            // is what makes a pool-overflow dump actually inspectable, without paying
            // for a full-memory dump of a process that may be holding gigabytes.
            MINIDUMP_TYPE type = (MINIDUMP_TYPE)(MiniDumpWithIndirectlyReferencedMemory |
                                                 MiniDumpScanMemory |
                                                 MiniDumpWithProcessThreadData);
            g_miniDump(GetCurrentProcess(), GetCurrentProcessId(), hd, type, &mei, nullptr, nullptr);
            CloseHandle(hd);
        }
    }

    Logf("FAULT #%d  code=0x%08X (%s)  eip=0x%08X %s  -> report written",
         (int)index, (unsigned)er->ExceptionCode, CodeName(er->ExceptionCode), (unsigned)eip, where);
}

// ------------------------------------------------------------------- the handler

static LONG CALLBACK VehHandler(EXCEPTION_POINTERS* ep) {
    DWORD code = ep->ExceptionRecord->ExceptionCode;

    if (!IsFatalLooking(code)) {
        InterlockedIncrement(&g_benignCount);
        return EXCEPTION_CONTINUE_SEARCH;
    }

    // A fault inside our own reporting code would recurse forever; bail on reentry.
    if (InterlockedCompareExchange(&g_inHandler, 1, 0) != 0) return EXCEPTION_CONTINUE_SEARCH;

    // Null-page stores from engine code are the one fault class this tool repairs
    // instead of merely recording: the engine writes script results back through
    // pointers it computed as NULL, and skipping the write is both survivable and
    // semantically what the engine's own success path would have left behind.
    //
    // The first fault at each distinct site still gets a full report, because the site
    // table is the point -- it maps every place the engine does this, which is what a
    // proper per-site fix would need.
    //
    // Snapshot the CONTEXT before NullStoreTryRepair() modifies registers in
    // ep->ContextRecord (e.g. strategy 1 redirects base registers to scratch page).
    // WriteReport needs pre-repair registers so that the fault address and register
    // state match. Use function-scope static buffers to keep the handler survivable
    // even when stack is nearly exhausted. g_inHandler guarantees single-threaded entry.
    static CONTEXT s_preRepairContext;
    static EXCEPTION_POINTERS s_preRepairEp;
    if (ep->ContextRecord) {
        s_preRepairContext = *ep->ContextRecord;
        s_preRepairEp.ExceptionRecord = ep->ExceptionRecord;
        s_preRepairEp.ContextRecord = &s_preRepairContext;
    }

    bool firstAtSite = false;
    unsigned resumeEip = 0;
    if (VmLvalueTryRepair(ep, firstAtSite, resumeEip)) {
        uintptr_t faultEip = ep->ContextRecord->Eip;
        if (firstAtSite) {
            char where[160];
            DescribeAddress(faultEip, where, sizeof(where));
            LONG idx = InterlockedIncrement(&g_reportCount);
            EXCEPTION_POINTERS* reportEp = ep->ContextRecord ? &s_preRepairEp : ep;
            if (idx <= g_cfg.maxReports) WriteReport(reportEp, idx, /*withDump=*/false);
            Logf("REPAIRED an invalid VM lvalue assignment: eip 0x%08X %s -- "
                 "faulting store suppressed, normal epilogue resumed.",
                 (unsigned)faultEip, where);
        }
        ep->ContextRecord->Eip = resumeEip;
        InterlockedExchange(&g_inHandler, 0);
        return EXCEPTION_CONTINUE_EXECUTION;
    }

    firstAtSite = false;
    resumeEip = 0;
    if (NullStoreTryRepair(ep, firstAtSite, resumeEip)) {
        uintptr_t faultEip = ep->ContextRecord->Eip;
        if (firstAtSite) {
            char where[160];
            DescribeAddress(faultEip, where, sizeof(where));
            LONG idx = InterlockedIncrement(&g_reportCount);
            EXCEPTION_POINTERS* reportEp = ep->ContextRecord ? &s_preRepairEp : ep;
            if (idx <= g_cfg.maxReports) WriteReport(reportEp, idx, /*withDump=*/false);
            Logf("REPAIRED a new null-access site: eip 0x%08X %s -- supported recovery selected, execution resumed.",
                 (unsigned)faultEip, where);
        }
        ep->ContextRecord->Eip = resumeEip;
        InterlockedExchange(&g_inHandler, 0);
        // The only place this handler ever resumes execution rather than observing.
        return EXCEPTION_CONTINUE_EXECUTION;
    }

    LONG index = InterlockedIncrement(&g_reportCount);
    if (index <= g_cfg.maxReports) {
        // A stack overflow leaves almost no stack to work in, and the report path uses
        // a 64 KB static buffer precisely so it needs none -- but the formatting calls
        // still need a few pages. Nothing to do about it beyond keeping frames small.
        WriteReport(ep, index, /*withDump=*/true);
    } else if (index == g_cfg.maxReports + 1) {
        Logf("FAULT: report limit (%d) reached; further faults are counted only.", g_cfg.maxReports);
    }

    InterlockedExchange(&g_inHandler, 0);
    return EXCEPTION_CONTINUE_SEARCH;   // never swallow: engine behaviour stays identical
}

bool CrashSelfTest() {
    unsigned char code[32] = {};
    for (uintptr_t eip = 0; eip < 8; ++eip) {
        if (ReadCodeWindow(eip, code, sizeof(code))) return false;
    }

    unsigned char source[48];
    for (unsigned i = 0; i < sizeof(source); ++i) source[i] = (unsigned char)(0x40u + i);
    uintptr_t eip = (uintptr_t)source + 8;
    if (!ReadCodeWindow(eip, code, sizeof(code))) return false;
    return memcmp(code, source, sizeof(code)) == 0;
}

void CrashLoadSymbolHelper() {
    if (!g_cfg.crashReports || !g_cfg.miniDumps) return;
    HMODULE dbg = LoadLibraryW(L"dbghelp.dll");
    if (dbg) g_miniDump = (PFN_MiniDumpWriteDump)GetProcAddress(dbg, "MiniDumpWriteDump");
    if (!g_miniDump) Logf("dbghelp.dll unavailable; text reports only.");
}

void CrashInstall() {
    if (!g_cfg.crashReports) return;

    // First = 1: ahead of every frame-based handler the engine installs.
    g_veh = AddVectoredExceptionHandler(1, VehHandler);
    Logf("crash handler installed (veh=%p, maxreports=%d, minidump=%d)",
         g_veh, g_cfg.maxReports, g_cfg.miniDumps ? 1 : 0);
}

void CrashUninstall() {
    if (g_veh) {
        RemoveVectoredExceptionHandler(g_veh);
        g_veh = nullptr;
    }
    Logf("crash handler removed. fatal reports=%d, non-fatal exceptions seen=%d",
         (int)g_reportCount, (int)g_benignCount);
}

} // namespace ckperf
