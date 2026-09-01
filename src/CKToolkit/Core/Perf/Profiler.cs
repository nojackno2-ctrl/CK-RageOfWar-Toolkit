using System.Runtime.InteropServices;
using System.Text;
using CKToolkit.Core.Common;
using CKToolkit.I18n;

namespace CKToolkit.Core.Perf;

/// <summary>
/// 遊戲取樣分析器 (Sampling Profiler for Celtic kings.exe)
///
/// 移植自前身 C++ profile.cpp / profile.h，保留原始註解與架構設計：
///
/// Celtic kings.exe has no ASLR (the PE has no relocation directory and
/// DYNAMIC_BASE is clear), so it always loads at 0x00400000. Runtime EIPs
/// therefore map 1:1 onto the addresses in a static disassembly -- whatever
/// comes back from a profile can be looked up directly.
///
/// The profiler is read-only with respect to the game: OpenProcess for query +
/// VM read, then suspend / read EIP / resume per sample. Nothing is injected and
/// nothing is written into the game's memory.
///
/// This toolkit builds x64 and the game is 32-bit, so use Wow64SuspendThread and
/// Wow64GetThreadContext reading WOW64_CONTEXT.Eip.
///
/// 2026-08-22 起這裡多了一層「抓閃退」的職責：
///   * 每秒把完整現場寫進記錄檔並立刻 flush (ProfilerTimeline.cs)；
///   * 可選的偵錯器模式，在例外發生的那一瞬間攔下來，寫出 minidump 與 JSON 狀態快照
///     (ProfilerDebugger.cs)；
///   * 可選的遊戲加速器，用引擎自己的 SetSpeed 把重現時間壓短 (GameSpeed.cs)。
/// 一次遊戲執行 = 一個記錄檔，預設寫在桌面。
/// </summary>
public static partial class Profiler
{
    private const string DefaultProcessName = "Celtic kings.exe";
    private const uint Th32CsSnapProcess = 0x00000002;
    private const uint Th32CsSnapThread = 0x00000004;
    private const uint Th32CsSnapModule = 0x00000008;
    private const uint Th32CsSnapModule32 = 0x00000010;

    private const uint ProcessQueryInformation = 0x0400;
    private const uint ThreadSuspendResume = 0x0002;
    private const uint ThreadGetContext = 0x0008;
    private const uint ThreadQueryInformation = 0x0040;
    private const uint Wow64ContextControl = 0x00010001;

    /// <summary>CONTEXT_CONTROL | CONTEXT_INTEGER | CONTEXT_SEGMENTS —— 每秒的詳細取樣用。</summary>
    private const uint Wow64ContextFull = 0x00010007;

    private const int VkEscape = 0x1B;
    private const int VkControl = 0x11;
    private const int VkF12 = 0x7B;

    #region Win32 P/Invoke & Blittable Structs

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private unsafe struct PROCESSENTRY32W
    {
        public uint dwSize;
        public uint cntUsage;
        public uint th32ProcessID;
        public IntPtr th32DefaultHeapID;
        public uint th32ModuleID;
        public uint cntThreads;
        public uint th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;
        public fixed char szExeFile[260];
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct THREADENTRY32
    {
        public uint dwSize;
        public uint cntUsage;
        public uint th32ThreadID;
        public uint th32OwnerProcessID;
        public int tpBasePri;
        public int tpDeltaPri;
        public uint dwFlags;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private unsafe struct MODULEENTRY32W
    {
        public uint dwSize;
        public uint th32ModuleID;
        public uint th32ProcessID;
        public uint GlblcntUsage;
        public uint ProccntUsage;
        public IntPtr modBaseAddr;
        public uint modBaseSize;
        public IntPtr hModule;
        public fixed char szModule[256];
        public fixed char szExePath[260];
    }

    /// <summary>
    /// WOW64_CONTEXT。位移取自 x86 CONTEXT 的標準版面：
    /// Dr0..Dr7 在 4..28、FLOATING_SAVE_AREA 在 28..140、區段暫存器 140..156、
    /// 整數暫存器 156..180、Ebp 180、Eip 184、SegCs 188、EFlags 192、Esp 196、SegSs 200，
    /// 之後是 512 bytes 的 ExtendedRegisters，合計 716。
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Size = 716)]
    public struct Wow64Context
    {
        [FieldOffset(0)]
        public uint ContextFlags;

        [FieldOffset(156)]
        public uint Edi;

        [FieldOffset(160)]
        public uint Esi;

        [FieldOffset(164)]
        public uint Ebx;

        [FieldOffset(168)]
        public uint Edx;

        [FieldOffset(172)]
        public uint Ecx;

        [FieldOffset(176)]
        public uint Eax;

        [FieldOffset(180)]
        public uint Ebp;

        [FieldOffset(184)]
        public uint Eip;

        [FieldOffset(188)]
        public uint SegCs;

        [FieldOffset(192)]
        public uint EFlags;

        [FieldOffset(196)]
        public uint Esp;

        [FieldOffset(200)]
        public uint SegSs;
    }

    [LibraryImport("kernel32.dll")]
    private static partial IntPtr OpenProcess(uint dwDesiredAccess, [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle, uint dwProcessId);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(IntPtr hObject);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWow64Process(IntPtr hProcess, [MarshalAs(UnmanagedType.Bool)] out bool wow64Process);

    [LibraryImport("kernel32.dll", EntryPoint = "CreateToolhelp32Snapshot")]
    private static partial IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

    [LibraryImport("kernel32.dll", EntryPoint = "Process32FirstW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool Process32First(IntPtr hSnapshot, ref PROCESSENTRY32W lppe);

    [LibraryImport("kernel32.dll", EntryPoint = "Process32NextW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool Process32Next(IntPtr hSnapshot, ref PROCESSENTRY32W lppe);

    [LibraryImport("kernel32.dll", EntryPoint = "Module32FirstW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool Module32First(IntPtr hSnapshot, ref MODULEENTRY32W lpme);

    [LibraryImport("kernel32.dll", EntryPoint = "Module32NextW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool Module32Next(IntPtr hSnapshot, ref MODULEENTRY32W lpme);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool Thread32First(IntPtr hSnapshot, ref THREADENTRY32 lpte);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool Thread32Next(IntPtr hSnapshot, ref THREADENTRY32 lpte);

    [LibraryImport("kernel32.dll")]
    private static partial IntPtr OpenThread(uint dwDesiredAccess, [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle, uint dwThreadId);

    [LibraryImport("kernel32.dll")]
    private static partial uint Wow64SuspendThread(IntPtr hThread);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool Wow64GetThreadContext(IntPtr hThread, ref Wow64Context lpContext);

    [LibraryImport("kernel32.dll")]
    private static partial uint ResumeThread(IntPtr hThread);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetThreadTimes(IntPtr hThread, out long lpCreationTime, out long lpExitTime, out long lpKernelTime, out long lpUserTime);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool QueryPerformanceFrequency(out long lpFrequency);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool QueryPerformanceCounter(out long lpPerformanceCount);

    [LibraryImport("kernel32.dll")]
    private static partial void Sleep(uint dwMilliseconds);

    [LibraryImport("kernel32.dll")]
    private static partial uint GetCurrentProcessId();

    [LibraryImport("user32.dll")]
    private static partial short GetAsyncKeyState(int vKey);

    #endregion

    #region Known Hot Regions Table

    private readonly struct Region(uint lo, uint hi, string what)
    {
        public uint Lo { get; } = lo;
        public uint Hi { get; } = hi;
        public string What { get; } = what;
    }

    private static readonly Region[] KnownRegions =
    [
        new(0x00401000, 0x00706000, ".text"),
        new(0x0042CC00, 0x0042D900, "timing / timeGetTime"),
        new(0x00430400, 0x00430600, "MMX 50% blend"),
        new(0x0044C400, 0x0044D100, "window + display mode"),
        new(0x0044F400, 0x0044F560, "GDI present (SetDIBitsToDevice)"),
        new(0x00475C00, 0x00477300, "MMX sprite blend blitters"),
        new(0x00501600, 0x00501820, "settlement tick"),
        new(0x00554100, 0x00554300, "selection subdivision"),
        new(0x0055D000, 0x00560000, "script VM"),
        new(0x005A1900, 0x005A1B00, "feeding / food search"),
        new(0x005E2200, 0x005E2500, "script thread scheduler"),
        new(0x00657D00, 0x00659000, "resolution list"),
        new(0x006AB800, 0x006AB900, "AI squad look"),
        new(0x006BE300, 0x006BE500, "SetVideoMode"),
        new(0x004265F0, 0x00426870, "linked-list container (iterate/insert/remove, vtable @0x707500)"),
        new(0x006BEE30, 0x006BEE70, "window proc? (0x5014-byte stack frame + ClipCursor)"),
        new(0x006DB6B0, 0x006DB6E0, "CRT _chkstk (stack probe -- hot because of large-stack callers, not itself)")
    ];

    private static string? Classify(uint addr, bool annotate)
    {
        if (!annotate) return null;
        string? best = null;
        uint span = 0xFFFFFFFFu;
        foreach (var r in KnownRegions)
        {
            if (addr >= r.Lo && addr < r.Hi && (r.Hi - r.Lo) < span)
            {
                best = r.What;
                span = r.Hi - r.Lo;
            }
        }
        return best;
    }

    #endregion

    public sealed class Options
    {
        public int Seconds { get; set; } = 0; // 0 = 執行直到遊戲結束
        public int Hz { get; set; } = 250;
        public int SegmentSeconds { get; set; } = 60;
        public bool WaitForProcess { get; set; } = false;
        public int DelaySeconds { get; set; } = 0;
        public bool WaitHotkey { get; set; } = false;
        public string ProcessName { get; set; } = DefaultProcessName;

        /// <summary>
        /// 直接指定要記錄的行程 ID；0 代表照 <see cref="ProcessName"/> 去找。
        ///
        /// 整合流程（<c>DiagnosticSession</c>）會把剛啟動／剛掛上的 pid 帶進來。
        /// 同一台機器上可能同時開著兩個遊戲行程，用名稱再找一次會挑錯人。
        /// </summary>
        public uint ProcessId { get; set; }
        public Action<string>? Log { get; set; }
        public Func<bool>? CancelRequested { get; set; }

        /// <summary>記錄檔要放的資料夾。空的話用桌面。一次遊戲執行只產生一個記錄檔。</summary>
        public string? LogDirectory { get; set; }

        /// <summary>直接指定記錄檔完整路徑；設了就蓋過 <see cref="LogDirectory"/> 的自動命名。</summary>
        public string? LogFile { get; set; }

        /// <summary>每秒詳細記錄：每個執行緒的暫存器、堆疊、熱點、記憶體與資源計數。</summary>
        public bool Detailed { get; set; } = true;

        /// <summary>每秒每執行緒最多掃幾層堆疊。0 = 關閉堆疊掃描。</summary>
        public int StackDepth { get; set; } = 16;

        /// <summary>崩潰時要回溯的秒數 (環形緩衝區大小)。</summary>
        public int CrashRingSeconds { get; set; } = 30;

        /// <summary>掛上偵錯器，在例外發生的那一刻攔截並寫出 minidump 與 JSON 狀態快照。</summary>
        public bool CatchCrash { get; set; } = true;

        /// <summary>minidump 是否包含完整記憶體 (檔案會有 1~2 GB)。</summary>
        public bool FullMemoryDump { get; set; } = true;

        /// <summary>遊戲加速倍率 (以引擎原生速度 1000 為基準，10 = 10 倍速)。0 或 1 = 不加速。</summary>
        public int SpeedMultiplier { get; set; } = 0;

        /// <summary>加速的方式。</summary>
        public GameSpeed.Method SpeedMethod { get; set; } = GameSpeed.Method.Hotkey;
    }

    private sealed class Segment
    {
        public int Index { get; set; }
        public double Start { get; set; }
        public double End { get; set; }
        public ulong Samples { get; set; }
        public ulong InModule { get; set; }
        public Dictionary<uint, uint> Hits { get; } = new();
    }

    /// <summary>分析結束後回報給呼叫端的東西 —— 報告文字之外還有落地的檔案路徑。</summary>
    public sealed class RunResult
    {
        public string Report { get; set; } = string.Empty;
        public string? LogPath { get; set; }
        public string? DumpPath { get; set; }
        public string? StatePath { get; set; }
        public bool Crashed { get; set; }
        public uint ExitCode { get; set; }
        public bool ExitCodeKnown { get; set; }
    }

    /// <summary>
    /// 執行取樣分析。
    /// </summary>
    public static Result<RunResult> Run(Options opt)
    {
        void Output(string msg) => opt.Log?.Invoke(msg);

        string target = string.IsNullOrWhiteSpace(opt.ProcessName) ? DefaultProcessName : opt.ProcessName;

        uint pid = opt.ProcessId != 0 ? opt.ProcessId : FindProcess(target);
        if (pid == 0 && opt.WaitForProcess)
        {
            Output($"等待 {target} 啟動中... (按 Esc 取消)");
            while (pid == 0)
            {
                if (opt.CancelRequested?.Invoke() == true || (GetAsyncKeyState(VkEscape) & 0x8000) != 0)
                {
                    return Result<RunResult>.Fail(Strings.Get("Error_ProfilerCancelled"), ExitCodes.GeneralFailure);
                }
                Sleep(200);
                pid = FindProcess(target);
            }
            Output("偵測到了，立刻開始記錄。");
        }

        if (pid == 0)
        {
            return Result<RunResult>.Fail(Strings.Get("Error_TargetProcessNotRunning"), ExitCodes.GeneralFailure);
        }

        // QUERY_INFORMATION 供計數器、VM_READ 供堆疊與程式碼讀取、SYNCHRONIZE 供
        // WaitForSingleObject 精準判定「程序已經死了」(不必靠列舉猜，也不怕 pid 被重用)。
        IntPtr proc = OpenProcess(ProcessQueryInformation | ProcessVmRead | SynchronizeAccess, false, pid);
        if (proc == IntPtr.Zero)
        {
            return Result<RunResult>.Fail(Strings.Get("Error_CannotOpenGameProcess"), ExitCodes.GeneralFailure);
        }

        IsWow64Process(proc, out bool wow64);
        Output($"附加到 {target} (pid {pid}, {(wow64 ? "32-bit" : "64-bit")})");
        if (!wow64)
        {
            // 取樣走的是 Wow64SuspendThread / Wow64GetThreadContext，對 64 位元目標一定失敗。
            // 講清楚比事後給一份空報告好。
            Output("*** 目標不是 32 位元程序，EIP 取樣不會有任何結果（每秒的狀態記錄與崩潰攔截仍然有效）。");
        }

        uint modBase = 0x00400000;
        uint modSize = 0x00400000;
        if (MainModuleRange(pid, target, out uint foundBase, out uint foundSize))
        {
            modBase = foundBase;
            modSize = foundSize;
            Output($"主模組: {modBase:X8} - {(modBase + modSize):X8} ({modSize / 1024} KB)");
        }

        bool annotate = (modBase == 0x00400000);

        if (opt.WaitHotkey)
        {
            Output("等待 Ctrl+F12...");
            while (true)
            {
                if (opt.CancelRequested?.Invoke() == true || (GetAsyncKeyState(VkEscape) & 0x8000) != 0)
                {
                    CloseHandle(proc);
                    return Result<RunResult>.Fail(Strings.Get("Error_ProfilerCancelled"), ExitCodes.GeneralFailure);
                }
                if ((GetAsyncKeyState(VkControl) & 0x8000) != 0 && (GetAsyncKeyState(VkF12) & 0x8000) != 0)
                    break;
                if (FindProcess(target) == 0)
                {
                    CloseHandle(proc);
                    return Result<RunResult>.Fail(Strings.Get("Error_ProfilerGameExitedEarly"), ExitCodes.GeneralFailure);
                }
                Sleep(50);
            }
        }

        for (int s = opt.DelaySeconds; s > 0; s--)
        {
            Output($"{s}... ");
            Sleep(1000);
        }

        var startedAt = DateTime.Now;
        string logPath = string.IsNullOrWhiteSpace(opt.LogFile)
            ? BuildLogPath(opt.LogDirectory, pid, startedAt)
            : opt.LogFile;

        var log = new TraceLog(logPath);
        if (!log.IsOpen)
        {
            Output($"*** 記錄檔開不起來 ({log.OpenError})，這一次只會有畫面上的摘要。");
        }
        else
        {
            Output($"記錄檔: {logPath}");
        }

        // 程式碼映像只讀一次；之後的堆疊掃描與位元組傾印都從這份快取算，不再碰遊戲記憶體。
        byte[]? image = SnapshotImage(proc, modBase, modSize);

        var tracer = new Tracer(log, proc, pid, target, modBase, modSize, annotate, image,
                                opt.Hz, opt.CrashRingSeconds, opt.Detailed ? opt.StackDepth : 0, startedAt);
        tracer.WriteHeader(opt, wow64);

        // 偵錯器模式：唯一能在「例外發生的那一瞬間」把現場凍住的辦法。引擎自己裝了
        // unhandled-exception filter 又呼叫 SetErrorMode，所以沒有偵錯器就永遠只剩屍體。
        CrashCatcher? catcher = null;
        if (opt.CatchCrash)
        {
            catcher = new CrashCatcher(pid, logPath, opt.FullMemoryDump, modBase, modSize, annotate, image,
                                      line => tracer.WriteNote(line), Output);
            if (!catcher.Start())
            {
                Output($"崩潰攔截啟動失敗 ({catcher.StartError})，改用每秒記錄 (仍然抓得到結束代碼)。");
                tracer.WriteNote($"[!] 崩潰攔截 (偵錯器) 啟動失敗：{catcher.StartError}");
                catcher.Dispose();
                catcher = null;
            }
            else
            {
                Output("崩潰攔截已啟動 (偵錯器模式)。");
            }
        }

        // 加速器：用引擎自己的 SetSpeed，不寫遊戲記憶體。
        if (opt.SpeedMultiplier > 1)
        {
            var speed = GameSpeed.Apply(pid, opt.SpeedMultiplier, opt.SpeedMethod);
            Output(speed.Message);
            tracer.WriteNote($"[加速器] {speed.Message}");
            tracer.WriteNote();
        }

        bool untilExit = opt.Seconds <= 0;
        Output(untilExit
            ? $"\n記錄中，直到遊戲結束。每 1 秒寫一筆詳細記錄。(按 Esc 提前結束)\n"
            : $"\n記錄 {opt.Seconds} 秒。\n");

        var totalHits = new Dictionary<uint, uint>();
        var segments = new List<Segment>();
        var curSegment = new Segment { Index = 1, Start = 0 };

        ulong totalSamples = 0;
        ulong totalInModule = 0;

        var startCpu = new Dictionary<uint, double>();
        var endCpu = new Dictionary<uint, double>();
        var perThreadInModule = new Dictionary<uint, uint>();

        var tids = new List<uint>();
        var handles = new List<IntPtr>();

        void RefreshThreads()
        {
            var before = new HashSet<uint>(tids);

            for (int i = 0; i < handles.Count; i++)
            {
                if (handles[i] != IntPtr.Zero)
                {
                    endCpu[tids[i]] = CpuSeconds(handles[i]);
                    CloseHandle(handles[i]);
                }
            }

            tids = ThreadsOf(pid);
            handles.Clear();

            foreach (uint tid in tids)
            {
                IntPtr h = OpenThread(ThreadSuspendResume | ThreadGetContext | ThreadQueryInformation, false, tid);
                handles.Add(h);
                if (h != IntPtr.Zero && !startCpu.ContainsKey(tid))
                {
                    startCpu[tid] = CpuSeconds(h);
                }
                if (!before.Contains(tid)) tracer.NoteThreadStart(tid, h);
            }

            foreach (uint gone in before.Where(t => !tids.Contains(t)))
            {
                tracer.NoteThreadExit(gone);
            }
        }

        RefreshThreads();

        int intervalUs = 1_000_000 / (opt.Hz > 0 ? opt.Hz : 250);
        QueryPerformanceFrequency(out long freq);
        QueryPerformanceCounter(out long t0);

        double Elapsed()
        {
            QueryPerformanceCounter(out long now);
            return (double)(now - t0) / freq;
        }

        string BuildReport(double runTime, bool finished)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"CKPatcher 取樣報告{(finished ? " (遊戲已結束)" : " (記錄中)")}");
            sb.AppendLine($"時間長度 {runTime:F0} 秒，樣本 {totalSamples}，其中在遊戲程式碼內 {totalInModule} ({(totalSamples > 0 ? 100.0 * totalInModule / totalSamples : 0.0):F1}%)");
            sb.AppendLine($"主模組 {modBase:X8} - {(modBase + modSize):X8}");
            sb.AppendLine();

            sb.AppendLine("=== 每個執行緒的 CPU 時間 (看有沒有用到多核心) ===");
            var cpuList = new List<(uint Tid, double Cpu)>();
            foreach (var (tid, endSec) in endCpu)
            {
                startCpu.TryGetValue(tid, out double startSec);
                cpuList.Add((tid, endSec - startSec));
            }
            cpuList.Sort((a, b) => b.Cpu.CompareTo(a.Cpu));

            foreach (var (tid, cpu) in cpuList)
            {
                perThreadInModule.TryGetValue(tid, out uint inModHits);
                double pct = runTime > 0 ? 100.0 * cpu / runTime : 0.0;
                sb.AppendLine($"   tid {tid,-6}  CPU {cpu,8:F2} s  ({pct,5:F1}% of one core)  遊戲碼內樣本 {inModHits}");
            }

            if (segments.Count > 0)
            {
                sb.AppendLine("\n=== 分段 (拿最早一段和最晚一段對照，就能看出隨單位數成長的部分) ===");
                foreach (var s in segments)
                {
                    sb.AppendLine($"\n-- 第 {s.Index} 段  {s.Start:F0}-{s.End:F0} 秒  遊戲碼內樣本 {s.InModule} --");
                    AppendTop(sb, s.Hits, s.InModule, 8, annotate);
                }
            }

            sb.AppendLine("\n=== 全場彙總: 最熱的位址 (每格 16 bytes) ===");
            AppendTop(sb, totalHits, totalInModule, 40, annotate);

            var pages = new Dictionary<uint, uint>();
            foreach (var (k, v) in totalHits)
            {
                uint page = k & ~0xFFFu;
                pages[page] = pages.GetValueOrDefault(page) + v;
            }
            sb.AppendLine("\n=== 全場彙總: 以 4 KB 為單位 ===");
            AppendTop(sb, pages, totalInModule, 20, annotate);

            return sb.ToString();
        }

        int segIndex = 1;
        bool gameExited = false;
        curSegment.Index = segIndex;
        curSegment.Start = 0;
        double lastTick = 0;

        // 取樣迴圈裡不管出什麼事，收尾的部分都一定要跑完 —— 使用者要的就是
        // 「遊戲閃退之後手上有東西可以看」，這裡絕不能因為一個例外就什麼都不輸出。
        string? fatalError = null;
        try
        {
        while (true)
        {
            double t = Elapsed();
            if (!untilExit && t >= opt.Seconds) break;
            if (opt.CancelRequested?.Invoke() == true || (GetAsyncKeyState(VkEscape) & 0x8000) != 0) break;

            for (int i = 0; i < handles.Count; i++)
            {
                if (handles[i] == IntPtr.Zero) continue;
                if (!SampleEip(handles[i], out uint eip, out uint esp)) continue;

                totalSamples++;
                curSegment.Samples++;

                bool inModule = eip >= modBase && eip < modBase + modSize;
                if (inModule)
                {
                    uint bucket = eip & ~0xFu;
                    totalHits[bucket] = totalHits.GetValueOrDefault(bucket) + 1;
                    curSegment.Hits[bucket] = curSegment.Hits.GetValueOrDefault(bucket) + 1;
                    perThreadInModule[tids[i]] = perThreadInModule.GetValueOrDefault(tids[i]) + 1;
                    totalInModule++;
                    curSegment.InModule++;
                }

                tracer.Record(t, tids[i], eip, esp, inModule);
            }

            // 每秒一次：先確認程序還活著，再把這一秒的現場寫下去。
            if (t - lastTick >= 1.0)
            {
                if (ProcessExited(proc))
                {
                    gameExited = true;
                    break;
                }

                RefreshThreads();
                if (opt.Detailed) tracer.Tick(t, tids, handles);
                lastTick = t;

                if (t - curSegment.Start >= opt.SegmentSeconds)
                {
                    curSegment.End = t;
                    segments.Add(curSegment);
                    curSegment = new Segment { Index = ++segIndex, Start = t };
                    Output($"  {t:F0} 秒, {totalSamples} 樣本 (遊戲碼內 {totalInModule})");
                }
            }

            PreciseSleepUs(intervalUs);
        }
        }
        catch (Exception ex)
        {
            fatalError = ex.ToString();
            Output($"*** 取樣迴圈發生例外，記錄仍會寫出：{ex.Message}");
            tracer.WriteNote($"[!!] 取樣迴圈發生例外，以下為分析器自己的錯誤，不是遊戲的：\r\n{ex}");
            if (ProcessExited(proc)) gameExited = true;
        }

        double finalRunTime = Elapsed();
        if (curSegment.Samples > 0)
        {
            curSegment.End = finalRunTime;
            segments.Add(curSegment);
        }

        for (int i = 0; i < handles.Count; i++)
        {
            if (handles[i] != IntPtr.Zero)
            {
                endCpu[tids[i]] = CpuSeconds(handles[i]);
                CloseHandle(handles[i]);
            }
        }

        // 偵錯器如果攔到了例外，它手上的現場比取樣器精確得多，等它把報告寫完再收尾。
        if (gameExited) catcher?.WaitForCapture(TimeSpan.FromSeconds(60));

        bool exitCodeKnown = GetExitCodeProcess(proc, out uint exitCode) && exitCode != 259; // STILL_ACTIVE
        if (gameExited && !exitCodeKnown)
        {
            // 極少數情況下控制代碼已經失效，退回偵錯器記錄到的值。
            exitCodeKnown = catcher?.ExitCodeKnown == true;
            exitCode = catcher?.ExitCode ?? 0;
        }

        bool crashed = (exitCodeKnown && IsCrashExitCode(exitCode)) || catcher?.Captured == true;

        Output($"\n{(gameExited ? (crashed ? "遊戲閃退" : "遊戲結束") : "記錄結束")}，共 {totalSamples} 個樣本。");

        // 遊戲還活著、而且我們動過速度，就把它調回正常再走人。
        if (!gameExited && opt.SpeedMultiplier > 1)
        {
            var restored = GameSpeed.Restore(pid, opt.SpeedMethod);
            Output(restored.Message);
            tracer.WriteNote($"[加速器] {restored.Message}");
        }

        string finalReport = BuildReport(finalRunTime, gameExited);

        if (totalSamples == 0)
        {
            // 一個樣本都沒有也照樣輸出。什麼都收不到本身就是線索
            // （遊戲一啟動就死、或分析器沒有權限開執行緒控制代碼）。
            string why = Strings.Get("Error_NoSamplesCollected");
            finalReport = $"{why}\r\n\r\n{finalReport}";
            tracer.WriteNote($"[!] {why}");
        }

        if (fatalError is not null)
        {
            finalReport = $"分析器本身發生例外（記錄仍完整寫出）：\r\n{fatalError}\r\n\r\n{finalReport}";
        }

        if (gameExited)
        {
            tracer.WriteExitReport(finalRunTime, exitCodeKnown, exitCode, finalRunTime, catcher?.CapturedSummary);
        }
        tracer.WriteSummary(finalReport);

        string? dumpPath = catcher?.DumpPath;
        string? statePath = catcher?.StatePath;
        catcher?.Dispose();
        tracer.Dispose();
        CloseHandle(proc);

        if (log.IsOpen) Output($"記錄已寫入 {logPath}");
        if (dumpPath is not null) Output($"崩潰傾印: {dumpPath}");
        if (statePath is not null) Output($"狀態快照: {statePath}");

        return Result<RunResult>.Ok(new RunResult
        {
            Report = finalReport,
            LogPath = log.IsOpen ? logPath : null,
            DumpPath = dumpPath,
            StatePath = statePath,
            Crashed = crashed,
            ExitCode = exitCode,
            ExitCodeKnown = exitCodeKnown
        });
    }

    #region Helper Methods

    /// <summary>用 SYNCHRONIZE 控制代碼判定程序是否已結束，不受 pid 重用影響。</summary>
    private static bool ProcessExited(IntPtr process) => WaitForSingleObject(process, 0) == 0;

    private static unsafe uint FindProcess(string want)
    {
        IntPtr snap = CreateToolhelp32Snapshot(Th32CsSnapProcess, 0);
        if (snap == IntPtr.Zero || snap == (IntPtr)(-1)) return 0;

        var pe = new PROCESSENTRY32W { dwSize = (uint)sizeof(PROCESSENTRY32W) };
        uint found = 0;
        uint self = GetCurrentProcessId();

        if (Process32First(snap, ref pe))
        {
            do
            {
                if (pe.th32ProcessID == self) continue;
                string exeName = new string(pe.szExeFile);

                if (string.Equals(exeName, want, StringComparison.OrdinalIgnoreCase))
                {
                    found = pe.th32ProcessID;
                    break;
                }
            } while (Process32Next(snap, ref pe));
        }

        CloseHandle(snap);
        return found;
    }

    private static unsafe bool MainModuleRange(uint pid, string name, out uint baseAddr, out uint size)
    {
        baseAddr = 0;
        size = 0;

        for (int attempt = 0; attempt < 30; attempt++)
        {
            IntPtr snap = CreateToolhelp32Snapshot(Th32CsSnapModule | Th32CsSnapModule32, pid);
            if (snap != IntPtr.Zero && snap != (IntPtr)(-1))
            {
                var me = new MODULEENTRY32W { dwSize = (uint)sizeof(MODULEENTRY32W) };
                bool found = false;
                if (Module32First(snap, ref me))
                {
                    do
                    {
                        string modName = new string(me.szModule);

                        if (string.Equals(modName, name, StringComparison.OrdinalIgnoreCase))
                        {
                            baseAddr = (uint)me.modBaseAddr.ToInt64();
                            size = me.modBaseSize;
                            found = true;
                            break;
                        }
                    } while (Module32Next(snap, ref me));
                }
                CloseHandle(snap);
                if (found) return true;
            }
            Sleep(100);
        }

        return false;
    }

    private static List<uint> ThreadsOf(uint pid)
    {
        var list = new List<uint>();
        IntPtr snap = CreateToolhelp32Snapshot(Th32CsSnapThread, 0);
        if (snap == IntPtr.Zero || snap == (IntPtr)(-1)) return list;

        var te = new THREADENTRY32 { dwSize = (uint)Marshal.SizeOf<THREADENTRY32>() };
        if (Thread32First(snap, ref te))
        {
            do
            {
                if (te.th32OwnerProcessID == pid)
                    list.Add(te.th32ThreadID);
            } while (Thread32Next(snap, ref te));
        }

        CloseHandle(snap);
        return list;
    }

    private static bool SampleEip(IntPtr thread, out uint eip, out uint esp)
    {
        eip = 0;
        esp = 0;
        if (Wow64SuspendThread(thread) == unchecked((uint)-1)) return false;

        var ctx = new Wow64Context { ContextFlags = Wow64ContextControl };
        bool ok = Wow64GetThreadContext(thread, ref ctx);
        ResumeThread(thread);

        if (!ok) return false;
        eip = ctx.Eip;
        esp = ctx.Esp;
        return true;
    }

    private static double CpuSeconds(IntPtr thread)
    {
        if (!GetThreadTimes(thread, out _, out _, out long kt, out long ut))
            return 0.0;
        return (double)(kt + ut) / 10_000_000.0;
    }

    public static void PreciseSleepUs(int micros)
    {
        if (micros <= 0) return;
        QueryPerformanceFrequency(out long freq);
        QueryPerformanceCounter(out long start);
        long target = start + (freq * micros) / 1_000_000;

        if (micros > 2000)
        {
            Sleep((uint)(micros / 1000 - 1));
        }

        do
        {
            Thread.Yield();
            QueryPerformanceCounter(out long now);
            if (now >= target) break;
        } while (true);
    }

    private static void AppendTop(StringBuilder sb, Dictionary<uint, uint> hits, ulong denom, int count, bool annotate)
    {
        if (denom == 0)
        {
            sb.AppendLine("   (沒有樣本落在遊戲程式碼內)");
            return;
        }

        var list = hits.OrderByDescending(kv => kv.Value).Take(count).ToList();
        foreach (var (addr, hitCount) in list)
        {
            string? what = Classify(addr, annotate);
            double pct = 100.0 * hitCount / denom;
            sb.AppendLine($"   {addr:X8}  {pct,6:F2}%  {hitCount,7}  {what ?? ""}");
        }
    }

    #endregion
}
