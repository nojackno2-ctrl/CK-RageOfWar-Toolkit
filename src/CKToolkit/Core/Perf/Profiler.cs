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

    [StructLayout(LayoutKind.Explicit, Size = 716)]
    public struct Wow64Context
    {
        [FieldOffset(0)]
        public uint ContextFlags;

        [FieldOffset(184)]
        public uint Eip;

        [FieldOffset(196)]
        public uint Esp;
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
        public string? OutFile { get; set; }
        public string ProcessName { get; set; } = DefaultProcessName;
        public Action<string>? Log { get; set; }
        public Func<bool>? CancelRequested { get; set; }
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

    /// <summary>
    /// 執行取樣分析。
    /// </summary>
    public static Result<string> Run(Options opt)
    {
        void Output(string msg) => opt.Log?.Invoke(msg);

        string target = string.IsNullOrWhiteSpace(opt.ProcessName) ? DefaultProcessName : opt.ProcessName;

        uint pid = FindProcess(target);
        if (pid == 0 && opt.WaitForProcess)
        {
            Output($"等待 {target} 啟動中... (按 Esc 取消)");
            while (pid == 0)
            {
                if (opt.CancelRequested?.Invoke() == true || (GetAsyncKeyState(VkEscape) & 0x8000) != 0)
                {
                    return Result<string>.Fail(Strings.Get("Error_ProfilerCancelled"), ExitCodes.GeneralFailure);
                }
                Sleep(200);
                pid = FindProcess(target);
            }
            Output("偵測到了，立刻開始記錄。");
        }

        if (pid == 0)
        {
            return Result<string>.Fail(Strings.Get("Error_TargetProcessNotRunning"), ExitCodes.GeneralFailure);
        }

        IntPtr proc = OpenProcess(ProcessQueryInformation, false, pid);
        if (proc == IntPtr.Zero)
        {
            return Result<string>.Fail(Strings.Get("Error_CannotOpenGameProcess"), ExitCodes.GeneralFailure);
        }

        IsWow64Process(proc, out bool wow64);
        CloseHandle(proc);

        Output($"附加到 {target} (pid {pid}, {(wow64 ? "32-bit" : "64-bit")})");

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
                    return Result<string>.Fail(Strings.Get("Error_ProfilerCancelled"), ExitCodes.GeneralFailure);
                }
                if ((GetAsyncKeyState(VkControl) & 0x8000) != 0 && (GetAsyncKeyState(VkF12) & 0x8000) != 0)
                    break;
                if (FindProcess(target) == 0)
                {
                    return Result<string>.Fail("遊戲在開始取樣前已結束", ExitCodes.GeneralFailure);
                }
                Sleep(50);
            }
        }

        for (int s = opt.DelaySeconds; s > 0; s--)
        {
            Output($"{s}... ");
            Sleep(1000);
        }

        bool untilExit = opt.Seconds <= 0;
        Output(untilExit
            ? $"\n記錄中，直到遊戲結束。每 {opt.SegmentSeconds} 秒寫一次報告。(按 Esc 提前結束)\n"
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

        void FlushReport(double runTime, bool finished)
        {
            if (string.IsNullOrWhiteSpace(opt.OutFile)) return;
            try
            {
                string rep = BuildReport(runTime, finished);
                File.WriteAllText(opt.OutFile, rep, Encoding.UTF8);
            }
            catch { }
        }

        int segIndex = 1;
        bool gameExited = false;
        curSegment.Index = segIndex;
        curSegment.Start = 0;

        while (true)
        {
            double t = Elapsed();
            if (!untilExit && t >= opt.Seconds) break;
            if (opt.CancelRequested?.Invoke() == true || (GetAsyncKeyState(VkEscape) & 0x8000) != 0) break;

            bool anyAlive = false;
            for (int i = 0; i < handles.Count; i++)
            {
                if (handles[i] == IntPtr.Zero) continue;
                if (!SampleEip(handles[i], out uint eip)) continue;

                anyAlive = true;
                totalSamples++;
                curSegment.Samples++;

                if (eip >= modBase && eip < modBase + modSize)
                {
                    uint bucket = eip & ~0xFu;
                    totalHits[bucket] = totalHits.GetValueOrDefault(bucket) + 1;
                    curSegment.Hits[bucket] = curSegment.Hits.GetValueOrDefault(bucket) + 1;
                    perThreadInModule[tids[i]] = perThreadInModule.GetValueOrDefault(tids[i]) + 1;
                    totalInModule++;
                    curSegment.InModule++;
                }
            }

            if (t - curSegment.Start >= opt.SegmentSeconds)
            {
                curSegment.End = t;
                segments.Add(curSegment);
                curSegment = new Segment { Index = ++segIndex, Start = t };

                RefreshThreads();
                if (FindProcess(target) == 0)
                {
                    gameExited = true;
                    break;
                }
                FlushReport(t, false);
                Output($"  {t:F0} 秒, {totalSamples} 樣本 (遊戲碼內 {totalInModule}) -- 已寫檔");
            }
            else if (!anyAlive)
            {
                if (FindProcess(target) == 0)
                {
                    gameExited = true;
                    break;
                }
                RefreshThreads();
            }

            PreciseSleepUs(intervalUs);
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

        Output($"\n{(gameExited ? "遊戲結束" : "記錄結束")}，共 {totalSamples} 個樣本。");

        if (totalSamples == 0)
        {
            return Result<string>.Fail(Strings.Get("Error_NoSamplesCollected"), ExitCodes.GeneralFailure);
        }

        string finalReport = BuildReport(finalRunTime, true);
        FlushReport(finalRunTime, true);
        if (!string.IsNullOrWhiteSpace(opt.OutFile))
        {
            Output($"報告已寫入 {opt.OutFile}");
        }

        return Result<string>.Ok(finalReport);
    }

    #region Helper Methods

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

    private static bool SampleEip(IntPtr thread, out uint eip)
    {
        eip = 0;
        if (Wow64SuspendThread(thread) == unchecked((uint)-1)) return false;

        var ctx = new Wow64Context { ContextFlags = Wow64ContextControl };
        bool ok = Wow64GetThreadContext(thread, ref ctx);
        ResumeThread(thread);

        if (!ok) return false;
        eip = ctx.Eip;
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
