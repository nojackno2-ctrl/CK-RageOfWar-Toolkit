using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace CKToolkit.Core.Perf;

/// <summary>
/// Keeps the ordered first-chance/second-chance crash candidates observed before exit.
/// A first-chance AV can be repaired by the engine or CKPerf, so the first item is not
/// automatically the fatal one. The exit report deliberately uses the latest candidate
/// and labels it as a candidate rather than claiming that this tracker proved root cause.
/// </summary>
internal sealed class CrashCandidateTracker
{
    public int Count { get; private set; }
    public string? LatestSummary { get; private set; }

    public void Record(string kind, string detail, ulong address, uint threadId)
    {
        Count++;
        LatestSummary = $"{kind}{detail} @ 0x{address:X8} (tid {threadId})";
    }
}

/// <summary>
/// 崩潰攔截 —— 在例外發生的那一瞬間把現場凍住並寫出來。
///
/// 為什麼非得掛偵錯器：引擎自己呼叫 SetErrorMode 並裝了 SetUnhandledExceptionFilter，
/// 崩潰永遠走不到 WER，所以事後沒有 dump、沒有對話框、沒有事件記錄，使用者只看到
/// 遊戲憑空消失。偵錯器是唯一能在引擎自己的處理常式「之前」看到例外的位置：
/// 第一手 (first chance) 例外會先送到偵錯器，我們抓完現場再原封不動放行
/// (DBG_EXCEPTION_NOT_HANDLED)，引擎後續的行為完全不變。
///
/// 抓到之後會寫出兩種可分析的檔案：
///   *.dmp   —— 標準 minidump，WinDbg / Visual Studio 直接開得起來；
///              勾了「完整記憶體」就是可以完整還原當下行程狀態的快照。
///   *.json  —— 結構化狀態快照：例外、暫存器、模組表、記憶體分佈、堆疊掃描，
///              另外附上 EIP 周邊機器碼與堆疊原始位元組 (Base64)，
///              所以離線也能重建現場、也方便丟給程式或 AI 分析。
///
/// 偵錯器一律用 DebugSetProcessKillOnExit(FALSE) —— 分析器自己被關掉時，遊戲要活著。
/// </summary>
internal sealed partial class CrashCatcher : IDisposable
{
    private readonly CrashCandidateTracker _crashCandidates = new();

    #region Win32

    private const uint DebugEventException = 1;
    private const uint DebugEventCreateThread = 2;
    private const uint DebugEventCreateProcess = 3;
    private const uint DebugEventExitThread = 4;
    private const uint DebugEventExitProcess = 5;
    private const uint DebugEventLoadDll = 6;
    private const uint DebugEventUnloadDll = 7;
    private const uint DebugEventOutputDebugString = 8;
    private const uint DebugEventRip = 9;

    private const uint DbgContinue = 0x00010002;
    private const uint DbgExceptionNotHandled = 0x80010001;

    private const uint ProcessQueryInformation = 0x0400;
    private const uint ProcessVmRead = 0x0010;
    private const uint ThreadGetContext = 0x0008;
    private const uint ThreadQueryInformation = 0x0040;
    private const uint Wow64ContextFull = 0x00010007;

    private const uint WaitTimeout = 258;      // WAIT_TIMEOUT
    private const uint ErrorSemTimeout = 121;  // ERROR_SEM_TIMEOUT —— WaitForDebugEvent 逾時實際回報的值

    // MINIDUMP_TYPE
    private const uint MiniDumpWithDataSegs = 0x0001;
    private const uint MiniDumpWithFullMemory = 0x0002;
    private const uint MiniDumpWithHandleData = 0x0004;
    private const uint MiniDumpWithUnloadedModules = 0x0020;
    private const uint MiniDumpWithIndirectlyReferencedMemory = 0x0040;
    private const uint MiniDumpWithProcessThreadData = 0x0100;
    private const uint MiniDumpWithFullMemoryInfo = 0x0800;
    private const uint MiniDumpWithThreadInfo = 0x1000;

    /// <summary>
    /// DEBUG_EVENT (64 位元呼叫端)。用明確位移取代整份 union 宣告：
    /// 0 事件碼、4 pid、8 tid，16 起是 union。EXCEPTION_DEBUG_INFO 是最大的成員，
    /// 其中 EXCEPTION_RECORD 佔 152 bytes (Code/Flags/Record/Address/NumberParameters
    /// /對齊/15 個 ULONG_PTR 參數)，dwFirstChance 緊接在後 (16+152=168)。
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Size = 192)]
    private struct DebugEvent
    {
        [FieldOffset(0)] public uint DebugEventCode;
        [FieldOffset(4)] public uint ProcessId;
        [FieldOffset(8)] public uint ThreadId;

        [FieldOffset(16)] public uint ExceptionCode;
        [FieldOffset(20)] public uint ExceptionFlags;
        [FieldOffset(32)] public ulong ExceptionAddress;
        [FieldOffset(40)] public uint NumberParameters;
        [FieldOffset(48)] public ulong ExceptionInformation0;
        [FieldOffset(56)] public ulong ExceptionInformation1;
        [FieldOffset(168)] public uint FirstChance;

        // EXIT_PROCESS_DEBUG_INFO
        [FieldOffset(16)] public uint ExitCode;

        // CREATE_PROCESS / LOAD_DLL 都以一個必須關閉的檔案控制代碼開頭。
        [FieldOffset(16)] public IntPtr FileHandle;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DebugActiveProcess(uint processId);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DebugActiveProcessStop(uint processId);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DebugSetProcessKillOnExit([MarshalAs(UnmanagedType.Bool)] bool killOnExit);

    [LibraryImport("kernel32.dll", EntryPoint = "WaitForDebugEventEx", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool WaitForDebugEvent(ref DebugEvent debugEvent, uint milliseconds);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ContinueDebugEvent(uint processId, uint threadId, uint continueStatus);

    [LibraryImport("kernel32.dll")]
    private static partial IntPtr OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint processId);

    [LibraryImport("kernel32.dll")]
    private static partial IntPtr OpenThread(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint threadId);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(IntPtr handle);

    [LibraryImport("dbghelp.dll", EntryPoint = "MiniDumpWriteDump", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool MiniDumpWriteDump(IntPtr hProcess, uint processId, IntPtr hFile, uint dumpType,
                                                 IntPtr exceptionParam, IntPtr userStreamParam, IntPtr callbackParam);

    #endregion

    private readonly uint _pid;
    private readonly string _logBasePath;
    private readonly bool _fullMemory;
    private readonly uint _modBase;
    private readonly uint _modSize;
    private readonly bool _annotate;
    private readonly byte[]? _image;
    private readonly Action<string> _toLog;
    private readonly Action<string> _toConsole;

    private readonly ManualResetEventSlim _captureDone = new(false);
    private readonly ManualResetEventSlim _attached = new(false);
    private volatile bool _stop;
    private Thread? _thread;
    private IntPtr _process = IntPtr.Zero;
    private int _capturesWritten;
    private int _dumpsWritten;
    private int _nullPageDumps;
    private bool _loggedWaitError;

    public string? StartError { get; private set; }
    public bool Captured => _crashCandidates.Count > 0;

    /// <summary>退出前最後攔到的 crash-looking 例外候選。第一手例外可能被修復，
    /// 所以這不是「已證明的致命根因」；完整序列仍以 JSON 與 log 為準。</summary>
    public string? CapturedSummary => _crashCandidates.LatestSummary;
    public string? DumpPath { get; private set; }
    public string? StatePath { get; private set; }
    public bool ExitCodeKnown { get; private set; }
    public uint ExitCode { get; private set; }

    /// <summary>最多寫幾份狀態快照 (.json)。快照約 215 KB，開較大配額以盡可能保留完整故障序列。</summary>
    private const int MaxCaptures = 20;

    /// <summary>最多寫幾份全記憶體傾印 (.dmp)。傾印約 434 MB，配額留給有價值的故障。</summary>
    private const int MaxDumps = 3;

    /// <summary>Null page 故障最多寫幾份傾印。留配額給真正致命的非 Null 故障。</summary>
    private const int MaxNullPageDumps = 1;

    public CrashCatcher(uint pid, string logPath, bool fullMemory, uint modBase, uint modSize,
                        bool annotate, byte[]? image, Action<string> toLog, Action<string> toConsole)
    {
        _pid = pid;
        _logBasePath = System.IO.Path.Combine(
            System.IO.Path.GetDirectoryName(logPath) ?? ".",
            System.IO.Path.GetFileNameWithoutExtension(logPath));
        _fullMemory = fullMemory;
        _modBase = modBase;
        _modSize = modSize;
        _annotate = annotate;
        _image = image;
        _toLog = toLog;
        _toConsole = toConsole;
    }

    /// <summary>啟動偵錯執行緒。回傳 false 表示掛不上去 (權限不足，或已經有偵錯器)。</summary>
    public bool Start()
    {
        _thread = new Thread(DebugLoop)
        {
            IsBackground = true,
            Name = "CKProfiler-CrashCatcher",
            Priority = ThreadPriority.AboveNormal
        };
        _thread.Start();

        // DebugActiveProcess 必須跟 WaitForDebugEvent 在同一條執行緒，所以在執行緒裡
        // 掛載，這裡等它回報結果。
        _attached.Wait(5000);
        return StartError is null;
    }

    /// <summary>
    /// 等偵錯執行緒把事情做完 —— 攔到的例外要寫完 minidump，程序結束事件也要記錄到。
    /// 遊戲已經結束時才呼叫，否則會白等。
    /// </summary>
    public void WaitForCapture(TimeSpan timeout) => _captureDone.Wait(timeout);

    public void Dispose()
    {
        _stop = true;
        try { _thread?.Join(5000); } catch { }
        if (_process != IntPtr.Zero) { CloseHandle(_process); _process = IntPtr.Zero; }
        _captureDone.Dispose();
        _attached.Dispose();
    }

    #region 偵錯迴圈

    private void DebugLoop()
    {
        if (!DebugActiveProcess(_pid))
        {
            uint err = (uint)Marshal.GetLastPInvokeError();
            StartError = err == 5
                ? "存取被拒 (權限不足；請用系統管理員身分執行，或確認遊戲不是以其他帳號啟動)"
                : err == 87
                    ? "程序已經被別的偵錯器掛住了"
                    : $"Win32 錯誤 {err}";
            _attached.Set();
            return;
        }

        // 分析器自己被關掉時，遊戲必須繼續活著。
        DebugSetProcessKillOnExit(false);
        _process = OpenProcess(ProcessQueryInformation | ProcessVmRead, false, _pid);
        _attached.Set();

        var ev = new DebugEvent();
        while (!_stop)
        {
            if (!WaitForDebugEvent(ref ev, 200))
            {
                // 逾時是正常的（那 200 ms 內遊戲沒有丟出任何事件）。
                // 其他錯誤也絕對不能讓偵錯器悄悄脫離 —— 一脫離就再也攔不到閃退了，
                // 而那正是使用者唯一在意的事。真正的結束只由 EXIT_PROCESS 或 Dispose 決定。
                uint err = (uint)Marshal.GetLastPInvokeError();
                if (err is WaitTimeout or ErrorSemTimeout) continue;

                if (!_loggedWaitError)
                {
                    _toLog($"[偵錯器] WaitForDebugEvent 失敗 (Win32 {err})，繼續等待事件。");
                    _loggedWaitError = true;
                }
                Thread.Sleep(50);
                continue;
            }

            uint continueStatus = DbgContinue;

            switch (ev.DebugEventCode)
            {
                case DebugEventException:
                    continueStatus = OnException(ref ev);
                    break;

                case DebugEventExitProcess:
                    ExitCode = ev.ExitCode;
                    ExitCodeKnown = true;
                    _toLog($"[偵錯器] 程序結束，結束代碼 0x{ev.ExitCode:X8}");
                    ContinueDebugEvent(ev.ProcessId, ev.ThreadId, DbgContinue);
                    _captureDone.Set();
                    return;

                case DebugEventCreateProcess:
                case DebugEventLoadDll:
                    // 這兩種事件會交給我們一個檔案控制代碼，不關就是洩漏。
                    if (ev.FileHandle != IntPtr.Zero) CloseHandle(ev.FileHandle);
                    break;

                case DebugEventCreateThread:
                case DebugEventExitThread:
                case DebugEventUnloadDll:
                case DebugEventOutputDebugString:
                case DebugEventRip:
                    break;
            }

            ContinueDebugEvent(ev.ProcessId, ev.ThreadId, continueStatus);
        }

        // 正常收工：把偵錯器拆掉，遊戲不受影響。
        DebugActiveProcessStop(_pid);
        _captureDone.Set();
    }

    private uint OnException(ref DebugEvent ev)
    {
        uint code = ev.ExceptionCode;
        bool fatal = (code & 0xF0000000u) == 0xC0000000u;
        bool first = ev.FirstChance != 0;

        string kind = DescribeException(code);
        string detail = string.Empty;
        if (code == 0xC0000005 || code == 0xC0000006)
        {
            string op = ev.ExceptionInformation0 switch
            {
                0 => "讀取",
                1 => "寫入",
                8 => "執行 (DEP)",
                _ => $"操作碼 {ev.ExceptionInformation0}"
            };
            detail = $"，{op}位址 0x{ev.ExceptionInformation1:X8}";
        }

        _toLog($"[偵錯器] {(first ? "第一手" : "第二手")}例外 {kind} @ 0x{ev.ExceptionAddress:X8}  tid {ev.ThreadId}{detail}");

        // 非致命 (中斷點、C++ 例外的搬運工 0xE06D7363 等) 只留一行紀錄就放行。
        if (!fatal) return DbgExceptionNotHandled;

        bool nullPage = (code == 0xC0000005 || code == 0xC0000006)
                        && ev.ExceptionInformation1 < 0x10000;

        bool writeDump = false;
        string? customStatePath = null;
        string? customDumpPath = null;

        if (_capturesWritten >= MaxCaptures)
        {
            customStatePath = $"{_logBasePath}-crash-latest.json";
            if (!first)
            {
                customDumpPath = $"{_logBasePath}-crash-latest.dmp";
                writeDump = true;
            }
            _toLog($"[偵錯器] 已達 {MaxCaptures} 份上限，本次例外滾動寫入最新候選狀態：{System.IO.Path.GetFileName(customStatePath)}。");
        }
        else
        {
            if (nullPage && _nullPageDumps >= MaxNullPageDumps)
            {
                writeDump = false;
                _toLog("[偵錯器] 這是 Null page 故障，minidump 配額留給非 Null 的故障，本次只寫狀態快照。");
            }
            else if (_dumpsWritten >= MaxDumps)
            {
                writeDump = false;
                _toLog($"[偵錯器] minidump 配額 {MaxDumps} 份已用完，本次只寫狀態快照。");
            }
            else
            {
                writeDump = true;
                _dumpsWritten++;
                if (nullPage) _nullPageDumps++;
            }

            _capturesWritten++;
        }

        try
        {
            Capture(ref ev, kind, detail, writeDump, customStatePath, customDumpPath);
        }
        catch (Exception ex)
        {
            _toLog($"[偵錯器] 寫出崩潰現場時發生錯誤：{ex.Message}");
        }

        // Never freeze this at the first first-chance AV. The engine/CKPerf may repair
        // that event and continue, as pid 27096 did for another fourteen exceptions.
        _crashCandidates.Record(kind, detail, ev.ExceptionAddress, ev.ThreadId);

        // 原封不動放行：引擎自己的處理常式照跑，行為跟沒掛偵錯器時一樣。
        return DbgExceptionNotHandled;
    }

    #endregion

    #region 現場輸出

    private void Capture(ref DebugEvent ev, string kind, string detail, bool writeDump,
                         string? customStatePath = null, string? customDumpPath = null)
    {
        string suffix = _capturesWritten <= 1 ? string.Empty : $"-{_capturesWritten}";
        string dumpPath = customDumpPath ?? $"{_logBasePath}-crash{suffix}.dmp";
        string statePath = customStatePath ?? $"{_logBasePath}-crash{suffix}.json";

        _toConsole($"攔到例外 {kind}，正在寫出崩潰現場…");

        // ---- 出錯執行緒的完整暫存器 ----
        var ctx = new Profiler.Wow64Context { ContextFlags = Wow64ContextFull };
        bool haveContext = false;
        IntPtr thread = OpenThread(ThreadGetContext | ThreadQueryInformation, false, ev.ThreadId);
        if (thread != IntPtr.Zero)
        {
            haveContext = Profiler.TryGetThreadContext(thread, ref ctx);
            CloseHandle(thread);
        }

        // ---- 現場的原始位元組 (可離線重建) ----
        byte[]? stackBytes = null;
        int stackLen = 0;
        byte[]? codeBytes = null;
        int codeLen = 0;
        uint codeStart = 0;

        if (_process != IntPtr.Zero)
        {
            if (haveContext && ctx.Esp != 0)
            {
                stackBytes = new byte[4096];
                stackLen = Profiler.ReadBytes(_process, ctx.Esp, stackBytes, stackBytes.Length);
            }

            uint faultAddr = (uint)ev.ExceptionAddress;
            codeStart = faultAddr >= 128 ? faultAddr - 128 : 0;
            codeBytes = new byte[256];
            codeLen = Profiler.ReadBytes(_process, codeStart, codeBytes, codeBytes.Length);
        }

        // ---- minidump ----
        string? dumpWritten = writeDump ? WriteMiniDump(dumpPath) : null;

        // ---- JSON 狀態快照 ----
        string? stateWritten = WriteStateJson(statePath, ref ev, kind, detail, haveContext, ctx,
                                              stackBytes, stackLen, codeStart, codeBytes, codeLen, dumpWritten);

        // ---- 人看的版本，直接寫進同一個記錄檔 ----
        WriteHumanReadable(ref ev, kind, detail, haveContext, ctx, stackBytes, stackLen, dumpWritten, stateWritten);

        if (dumpWritten is not null) DumpPath = dumpWritten;
        if (stateWritten is not null) StatePath = stateWritten;
    }

    private string? WriteMiniDump(string path)
    {
        if (_process == IntPtr.Zero)
        {
            _toLog("[偵錯器] 沒有可用的程序控制代碼，略過 minidump。");
            return null;
        }

        uint type = MiniDumpWithDataSegs | MiniDumpWithHandleData | MiniDumpWithUnloadedModules
                  | MiniDumpWithIndirectlyReferencedMemory | MiniDumpWithProcessThreadData
                  | MiniDumpWithFullMemoryInfo | MiniDumpWithThreadInfo;
        if (_fullMemory) type |= MiniDumpWithFullMemory;

        try
        {
            using var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
            bool ok = MiniDumpWriteDump(_process, _pid, file.SafeFileHandle.DangerousGetHandle(), type,
                                        IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            if (!ok)
            {
                uint err = (uint)Marshal.GetLastPInvokeError();
                _toLog($"[偵錯器] MiniDumpWriteDump 失敗 (0x{err:X8})。");
                return null;
            }
            file.Flush(true);
            return path;
        }
        catch (Exception ex)
        {
            _toLog($"[偵錯器] 寫 minidump 失敗：{ex.Message}");
            return null;
        }
    }

    private string? WriteStateJson(string path, ref DebugEvent ev, string kind, string detail,
                                   bool haveContext, Profiler.Wow64Context ctx,
                                   byte[]? stackBytes, int stackLen,
                                   uint codeStart, byte[]? codeBytes, int codeLen,
                                   string? dumpPath)
    {
        try
        {
            var state = new
            {
                schema = "ck-toolkit/profiler-crash-state",
                schemaVersion = 1,
                capturedAt = DateTime.Now.ToString("o"),
                note = "由 CK-RageOfWar Toolkit 分析器的偵錯器模式在例外發生當下擷取。位址皆為 32 位元虛擬位址；stackBase64 / codeBase64 是現場的原始位元組，可離線重建。",
                process = new
                {
                    pid = _pid,
                    moduleBase = _modBase,
                    moduleSize = _modSize,
                    moduleBaseHex = $"0x{_modBase:X8}",
                    noAslr = _annotate,
                    imageCached = _image is not null
                },
                exception = new
                {
                    code = ev.ExceptionCode,
                    codeHex = $"0x{ev.ExceptionCode:X8}",
                    name = kind,
                    detail,
                    firstChance = ev.FirstChance != 0,
                    flags = ev.ExceptionFlags,
                    address = ev.ExceptionAddress,
                    addressHex = $"0x{ev.ExceptionAddress:X8}",
                    inGameModule = ev.ExceptionAddress >= _modBase && ev.ExceptionAddress < _modBase + _modSize,
                    numberParameters = ev.NumberParameters,
                    parameter0 = ev.ExceptionInformation0,
                    parameter1 = ev.ExceptionInformation1,
                    parameter1Hex = $"0x{ev.ExceptionInformation1:X8}",
                    threadId = ev.ThreadId
                },
                registers = haveContext
                    ? new Dictionary<string, string>
                    {
                        ["eip"] = $"0x{ctx.Eip:X8}",
                        ["esp"] = $"0x{ctx.Esp:X8}",
                        ["ebp"] = $"0x{ctx.Ebp:X8}",
                        ["eax"] = $"0x{ctx.Eax:X8}",
                        ["ebx"] = $"0x{ctx.Ebx:X8}",
                        ["ecx"] = $"0x{ctx.Ecx:X8}",
                        ["edx"] = $"0x{ctx.Edx:X8}",
                        ["esi"] = $"0x{ctx.Esi:X8}",
                        ["edi"] = $"0x{ctx.Edi:X8}",
                        ["eflags"] = $"0x{ctx.EFlags:X8}",
                        ["cs"] = $"0x{ctx.SegCs:X4}",
                        ["ss"] = $"0x{ctx.SegSs:X4}"
                    }
                    : null,
                stack = new
                {
                    esp = haveContext ? $"0x{ctx.Esp:X8}" : null,
                    length = stackLen,
                    base64 = stackBytes is not null && stackLen > 0
                        ? Convert.ToBase64String(stackBytes, 0, stackLen)
                        : null,
                    frames = haveContext && stackBytes is not null
                        ? Profiler.ScanStackForJson(stackBytes, stackLen, ctx.Esp, _modBase, _modSize, _image, 32)
                        : null
                },
                code = new
                {
                    start = $"0x{codeStart:X8}",
                    length = codeLen,
                    base64 = codeBytes is not null && codeLen > 0
                        ? Convert.ToBase64String(codeBytes, 0, codeLen)
                        : null
                },
                threads = Profiler.SnapshotThreadsForJson(_pid, _modBase, _modSize),
                modules = Profiler.SnapshotModulesForJson(_pid),
                memory = _process != IntPtr.Zero ? Profiler.SnapshotMemoryForJson(_process, _image) : null,
                miniDump = dumpPath
            };

            string json = JsonSerializer.Serialize(state, new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });

            File.WriteAllText(path, json, new UTF8Encoding(true));
            return path;
        }
        catch (Exception ex)
        {
            _toLog($"[偵錯器] 寫 JSON 狀態快照失敗：{ex.Message}");
            return null;
        }
    }

    private void WriteHumanReadable(ref DebugEvent ev, string kind, string detail,
                                    bool haveContext, Profiler.Wow64Context ctx,
                                    byte[]? stackBytes, int stackLen,
                                    string? dumpPath, string? statePath)
    {
        _toLog("");
        _toLog("################################################################################");
        _toLog("###  攔截到例外 —— 這就是崩潰的當下現場");
        _toLog("################################################################################");
        _toLog($"時間        : {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
        _toLog($"例外        : {kind}{detail}");
        _toLog($"發生位址    : 0x{ev.ExceptionAddress:X8}  ({(ev.ExceptionAddress >= _modBase && ev.ExceptionAddress < _modBase + _modSize ? "在遊戲主模組內 —— 可直接對靜態反組譯" : "不在遊戲主模組內")})");
        _toLog($"執行緒      : tid {ev.ThreadId}");
        _toLog($"手次        : {(ev.FirstChance != 0 ? "第一手 (引擎的處理常式還沒看到)" : "第二手 (引擎已經放棄)")}");

        if (haveContext)
        {
            _toLog($"暫存器      : EIP {ctx.Eip:X8}  ESP {ctx.Esp:X8}  EBP {ctx.Ebp:X8}  EFLAGS {ctx.EFlags:X8}");
            _toLog($"              EAX {ctx.Eax:X8} EBX {ctx.Ebx:X8} ECX {ctx.Ecx:X8} EDX {ctx.Edx:X8} ESI {ctx.Esi:X8} EDI {ctx.Edi:X8}");

            if (stackBytes is not null && stackLen >= 4)
            {
                _toLog("堆疊 (掃描法，括號是發出 call 的位址)");
                foreach (string line in Profiler.DescribeStack(stackBytes, stackLen, ctx.Esp, _modBase, _modSize, _image, _annotate, 24))
                {
                    _toLog("   " + line);
                }
            }
        }
        else
        {
            _toLog("暫存器      : 讀不到 (執行緒控制代碼開啟失敗)");
        }

        _toLog($"minidump    : {dumpPath ?? "(未寫出)"}");
        _toLog($"狀態快照    : {statePath ?? "(未寫出)"}");
        _toLog("################################################################################");
        _toLog("");
    }

    #endregion

    private static string DescribeException(uint code) => code switch
    {
        0xC0000005 => "STATUS_ACCESS_VIOLATION 存取違規",
        0xC0000006 => "STATUS_IN_PAGE_ERROR 分頁讀取失敗",
        0xC000001D => "STATUS_ILLEGAL_INSTRUCTION 非法指令",
        0xC0000025 => "STATUS_NONCONTINUABLE_EXCEPTION",
        0xC000008C => "STATUS_ARRAY_BOUNDS_EXCEEDED 陣列越界",
        0xC000008E => "STATUS_FLOAT_DIVIDE_BY_ZERO 浮點除以零",
        0xC0000090 => "STATUS_FLOAT_INVALID_OPERATION 浮點無效運算",
        0xC0000091 => "STATUS_FLOAT_OVERFLOW",
        0xC0000093 => "STATUS_FLOAT_UNDERFLOW",
        0xC0000094 => "STATUS_INTEGER_DIVIDE_BY_ZERO 整數除以零",
        0xC0000095 => "STATUS_INTEGER_OVERFLOW",
        0xC0000096 => "STATUS_PRIVILEGED_INSTRUCTION 特權指令",
        0xC00000FD => "STATUS_STACK_OVERFLOW 堆疊溢位",
        0xC0000374 => "STATUS_HEAP_CORRUPTION 堆積損毀",
        0xC0000409 => "STATUS_STACK_BUFFER_OVERRUN 堆疊被蓋掉",
        0xC0000417 => "STATUS_INVALID_CRUNTIME_PARAMETER CRT 參數檢查失敗",
        0xC000041D => "STATUS_FATAL_USER_CALLBACK_EXCEPTION 視窗回呼裡的致命例外",
        0x80000003 => "STATUS_BREAKPOINT 中斷點 (通常無害)",
        0x80000004 => "STATUS_SINGLE_STEP 單步 (通常無害)",
        0xE06D7363 => "C++ 例外 (throw；通常無害)",
        0x406D1388 => "設定執行緒名稱 (無害)",
        _ => $"例外碼 0x{code:X8}"
    };
}
