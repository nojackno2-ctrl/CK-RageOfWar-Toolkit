using System.Text;

namespace CKToolkit.Core.Perf;

/// <summary>
/// 每秒一筆的時間軸記錄 —— 分析器抓閃退的主體。
///
/// 取樣本身照樣以 Hz 設定的頻率跑 (預設 250 Hz)，但每滿一秒就把那一秒的完整現場
/// 寫進記錄檔並立刻 flush：每個執行緒的 EIP / 暫存器 / 堆疊、那一秒的熱點、
/// 記憶體與位址空間、GDI / USER / 控制代碼數量、視窗是否還在回應、模組有沒有變動。
///
/// 遊戲閃退時什麼都不會留下，所以這裡的策略是「每一秒都當成最後一秒來寫」。
/// </summary>
public static partial class Profiler
{
    /// <summary>某個執行緒在「這一秒」之內的統計。</summary>
    private sealed class ThreadSecond
    {
        public ulong Samples;
        public ulong InModule;
        public Dictionary<uint, uint> Hits { get; } = new();
        public double CpuStart;
        public double CpuEnd;
        public uint LastEip;
        public uint LastEsp;
    }

    /// <summary>時間軸上的一秒，留著是為了在崩潰報告裡回頭看趨勢。</summary>
    private sealed class SecondRow
    {
        public double T;
        public DateTime Wall;
        public ulong Samples;
        public ulong InModule;
        public ulong WorkingSet;
        public ulong PrivateBytes;
        public ulong LargestFreeBlock;
        public double AddressUsedPercent;
        public bool AddressSpaceValid;
        public uint Handles;
        public uint Gdi;
        public uint User;
        public uint PageFaultsPerSecond;
        public double ProcessCpu;
        public int Threads;
        public bool WindowResponding = true;
        public uint TopAddress;
        public double TopPercent;
    }

    /// <summary>取樣環形緩衝區的一筆。崩潰時就靠它回溯「最後那幾百個 EIP」。</summary>
    private readonly record struct RingEntry(double T, uint Tid, uint Eip, uint Esp);

    /// <summary>
    /// 記錄器。Run() 只負責取樣，格式化與落地全部在這裡。
    /// </summary>
    private sealed class Tracer : IDisposable
    {
        private readonly TraceLog _log;
        private readonly IntPtr _process;
        private readonly uint _pid;
        private readonly string _processName;
        private readonly uint _modBase;
        private readonly uint _modSize;
        private readonly bool _annotate;
        private readonly byte[]? _image;
        private readonly bool _laa;
        private readonly int _stackDepth;
        private readonly int _stackBytes;
        private readonly DateTime _startedAt;

        private readonly RingEntry[] _ring;
        private int _ringPos;
        private bool _ringWrapped;

        private readonly List<SecondRow> _history = new();
        private readonly Dictionary<uint, ThreadSecond> _threadSecond = new();
        private readonly Dictionary<uint, uint> _secondHits = new();
        private readonly Dictionary<uint, DateTime> _threadBorn = new();

        private List<ModuleInfo> _modules;
        private IntPtr _window;
        private string _windowTitle = string.Empty;

        private ulong _secondSamples;
        private ulong _secondInModule;
        private uint _lastPageFaults;
        private uint _lastHandles;
        private uint _lastGdi;
        private uint _lastUser;
        private double _lastProcessCpu;
        private ulong _lastIoRead;
        private ulong _lastIoWrite;
        private double _lastTickTime;
        private int _tickCount;

        public string Path => _log.Path;
        public bool IsOpen => _log.IsOpen;
        public string? OpenError => _log.OpenError;
        public IReadOnlyList<SecondRow> History => _history;

        public Tracer(TraceLog log, IntPtr process, uint pid, string processName,
                      uint modBase, uint modSize, bool annotate, byte[]? image,
                      int hz, int ringSeconds, int stackDepth, DateTime startedAt)
        {
            _log = log;
            _process = process;
            _pid = pid;
            _processName = processName;
            _modBase = modBase;
            _modSize = modSize;
            _annotate = annotate;
            _image = image;
            _laa = ImageIsLargeAddressAware(image);
            _stackDepth = Math.Clamp(stackDepth, 0, 64);
            _stackBytes = 4096;
            _startedAt = startedAt;

            int capacity = Math.Clamp(hz * 8 * Math.Max(1, ringSeconds), 8192, 400_000);
            _ring = new RingEntry[capacity];

            _modules = SnapshotModules(pid);
            _window = FindMainWindow(pid);
            if (_window != IntPtr.Zero) _windowTitle = WindowTitle(_window);
        }

        public void Dispose() => _log.Dispose();

        #region 檔頭

        public void WriteHeader(Options opt, bool wow64)
        {
            var os = Environment.OSVersion;
            _log.Line("================================================================================");
            _log.Line("  CK-RageOfWar Toolkit — 取樣分析器 / 閃退記錄");
            _log.Line("================================================================================");
            _log.Line($"開始時間      : {_startedAt:yyyy-MM-dd HH:mm:ss}");
            _log.Line($"目標程序      : {_processName}  (pid {_pid}, {(wow64 ? "32-bit WOW64" : "64-bit")})");
            _log.Line($"主模組        : {_modBase:X8} - {_modBase + _modSize:X8}  ({_modSize / 1024} KB)");
            _log.Line($"位址對照      : {(_annotate ? "可用 (模組載入在 0x00400000，EIP 可直接對靜態反組譯)" : "不可用 (模組不在預期基底，已知熱區表不套用)")}");
            _log.Line($"LargeAddressAware : {(_laa ? "是 — 使用者位址空間 4 GB" : "否 — 使用者位址空間 2 GB")}");
            _log.Line($"程式碼映像快取: {(_image is null ? "讀取失敗 (堆疊掃描與位元組傾印停用)" : $"{_image.Length / 1024} KB 已快取")}");
            _log.Line($"取樣頻率      : {opt.Hz} Hz，每 1 秒寫一筆詳細記錄");
            _log.Line($"堆疊掃描      : 每秒每執行緒最多 {_stackDepth} 層 (掃描 ESP 起 {_stackBytes} bytes)");
            _log.Line($"崩潰回溯      : 最後 {opt.CrashRingSeconds} 秒的取樣 (環形緩衝 {_ring.Length} 筆)");
            _log.Line($"分析器        : pid {Environment.ProcessId}，主機 {Environment.MachineName}，{os.VersionString}");
            _log.Line();

            _log.Line("--- 起始模組表 ---");
            foreach (var m in _modules)
            {
                _log.Line($"   {m.Base:X8} - {m.End:X8}  {m.Size / 1024,7} KB  {m.Name}");
            }
            _log.Line();

            if (_window != IntPtr.Zero)
            {
                _log.Line($"主視窗        : hwnd 0x{_window.ToInt64():X}  標題「{_windowTitle}」");
                _log.Line();
            }

            _log.Line("以下每一秒一段。遊戲若閃退，最後一段就是崩潰前一秒的現場。");
            _log.Line("================================================================================");
            _log.Line();
        }

        #endregion

        #region 取樣累積

        public void Record(double t, uint tid, uint eip, uint esp, bool inModule)
        {
            _ring[_ringPos] = new RingEntry(t, tid, eip, esp);
            if (++_ringPos >= _ring.Length) { _ringPos = 0; _ringWrapped = true; }

            if (!_threadSecond.TryGetValue(tid, out var ts))
            {
                ts = new ThreadSecond();
                _threadSecond[tid] = ts;
            }

            ts.Samples++;
            ts.LastEip = eip;
            ts.LastEsp = esp;
            _secondSamples++;

            if (inModule)
            {
                uint bucket = eip & ~0xFu;
                ts.InModule++;
                ts.Hits[bucket] = ts.Hits.GetValueOrDefault(bucket) + 1;
                _secondHits[bucket] = _secondHits.GetValueOrDefault(bucket) + 1;
                _secondInModule++;
            }
        }

        public void NoteThreadStart(uint tid, IntPtr handle)
        {
            if (_threadBorn.ContainsKey(tid)) return;
            _threadBorn[tid] = DateTime.Now;
            if (_tickCount > 0)
            {
                _log.Line($"   [!] 新的執行緒 tid {tid} 出現{(handle == IntPtr.Zero ? " (無法開啟控制代碼，權限不足？)" : "")}");
            }
        }

        public void NoteThreadExit(uint tid)
        {
            if (_threadBorn.Remove(tid) && _tickCount > 0)
            {
                _log.Line($"   [!] 執行緒 tid {tid} 已結束");
            }
        }

        #endregion

        #region 每秒一段

        /// <summary>
        /// 把過去這一秒收攏成一段記錄。tids / handles 由 Run 維護，順序一一對應。
        /// </summary>
        public void Tick(double t, List<uint> tids, List<IntPtr> handles)
        {
            _tickCount++;
            double window = t - _lastTickTime;
            if (window <= 0) window = 1.0;

            DateTime wall = DateTime.Now;
            var row = new SecondRow { T = t, Wall = wall, Samples = _secondSamples, InModule = _secondInModule };

            // ---- 程序層級的計數器 ----
            var pmc = new ProcessMemoryCountersEx { cb = (uint)System.Runtime.InteropServices.Marshal.SizeOf<ProcessMemoryCountersEx>() };
            bool haveMem = GetProcessMemoryInfo(_process, ref pmc, pmc.cb);
            var space = QueryAddressSpace(_process, _laa);
            GetProcessHandleCount(_process, out uint handleCount);
            uint gdi = GetGuiResources(_process, GuiResourcesGdiObjects);
            uint user = GetGuiResources(_process, GuiResourcesUserObjects);
            GetProcessIoCounters(_process, out IoCounters io);
            double procCpu = 0;
            if (GetProcessTimes(_process, out _, out _, out long pk, out long pu))
                procCpu = (pk + pu) / 10_000_000.0;

            row.WorkingSet = haveMem ? pmc.WorkingSetSize : 0;
            row.PrivateBytes = haveMem ? pmc.PrivateUsage : 0;
            row.PageFaultsPerSecond = haveMem && pmc.PageFaultCount >= _lastPageFaults
                ? (uint)((pmc.PageFaultCount - _lastPageFaults) / window)
                : 0;
            row.Handles = handleCount;
            row.Gdi = gdi;
            row.User = user;
            row.LargestFreeBlock = space.LargestFreeBlock;
            row.AddressUsedPercent = space.UsedPercent;
            row.AddressSpaceValid = space.Complete;
            row.ProcessCpu = procCpu - _lastProcessCpu;
            row.Threads = tids.Count;

            if (_window == IntPtr.Zero || !IsWindowVisible(_window)) _window = FindMainWindow(_pid);
            if (_window != IntPtr.Zero)
            {
                row.WindowResponding = !IsHungAppWindow(_window);
                string title = WindowTitle(_window);
                if (!string.IsNullOrEmpty(title)) _windowTitle = title;
            }

            // ---- 段落標頭 ----
            _log.Line("--------------------------------------------------------------------------------");
            _log.Line($"[{t,8:F1} 秒]  {wall:yyyy-MM-dd HH:mm:ss.fff}   第 {_tickCount} 筆");

            double inModulePct = _secondSamples > 0 ? 100.0 * _secondInModule / _secondSamples : 0.0;
            _log.Line($"   取樣    本秒 {_secondSamples} 個 (遊戲碼內 {_secondInModule}, {inModulePct:F1}%)   執行緒 {tids.Count} 條");
            _log.Line($"   CPU     行程 {row.ProcessCpu / window * 100.0:F1}% of one core  (累計 {procCpu:F1} 秒)");

            if (haveMem)
            {
                _log.Line($"   記憶體  工作集 {Mb(pmc.WorkingSetSize)} (峰值 {Mb(pmc.PeakWorkingSetSize)})   私有 {Mb(pmc.PrivateUsage)}   分頁檔 {Mb(pmc.PagefileUsage)}   分頁錯誤 {row.PageFaultsPerSecond}/s");
            }
            if (space.Complete)
            {
                _log.Line($"   位址空間 已用 {Gb(space.Used)} / {Gb(space.Limit)} ({space.UsedPercent:F1}%)   提交 私有 {Mb(space.CommittedPrivate)} 映像 {Mb(space.CommittedImage)} 對應 {Mb(space.CommittedMapped)}");
                _log.Line($"            保留 {Mb(space.Reserved)}   最大連續空閒 {Mb(space.LargestFreeBlock)}   最高已提交位址 {space.HighestCommitted:X8}   區塊 {space.RegionCount}");
            }
            else
            {
                _log.Line("   位址空間 (取樣失敗；程序可能已退出，本秒數值不納入警告與趨勢判讀)");
            }
            _log.Line($"   資源    控制代碼 {handleCount} ({Delta(handleCount, _lastHandles)})   GDI {gdi} ({Delta(gdi, _lastGdi)})   USER {user} ({Delta(user, _lastUser)})");
            _log.Line($"   I/O     讀 {(io.ReadTransferCount - _lastIoRead) / 1048576.0 / window:F2} MB/s   寫 {(io.WriteTransferCount - _lastIoWrite) / 1048576.0 / window:F2} MB/s");
            if (_window != IntPtr.Zero)
            {
                _log.Line($"   視窗    「{_windowTitle}」 {(row.WindowResponding ? "回應正常" : "*** 沒有回應 (訊息迴圈卡住) ***")}");
            }
            else
            {
                _log.Line("   視窗    找不到可見的主視窗");
            }

            // ---- 每個執行緒 ----
            _log.Line("   執行緒");
            for (int i = 0; i < tids.Count; i++)
            {
                uint tid = tids[i];
                IntPtr h = i < handles.Count ? handles[i] : IntPtr.Zero;
                _threadSecond.TryGetValue(tid, out var ts);

                double cpu = 0;
                if (h != IntPtr.Zero)
                {
                    double now = CpuSeconds(h);
                    if (ts is not null)
                    {
                        if (ts.CpuStart <= 0) ts.CpuStart = now;
                        ts.CpuEnd = now;
                    }
                    cpu = now;
                }

                ulong samples = ts?.Samples ?? 0;
                ulong inMod = ts?.InModule ?? 0;
                string hot = "-";
                if (ts is not null && ts.Hits.Count > 0)
                {
                    var top = ts.Hits.OrderByDescending(kv => kv.Value).First();
                    string? what = Classify(top.Key, _annotate);
                    hot = $"{top.Key:X8} ({100.0 * top.Value / Math.Max(1UL, inMod):F0}%)" + (what is null ? "" : $"  {what}");
                }

                _log.Line($"     tid {tid,-6} 樣本 {samples,5} (遊戲碼內 {inMod,5})  CPU 累計 {cpu,7:F2}s  最熱 {hot}");

                if (h == IntPtr.Zero) continue;
                DumpThreadDetail(tid, h);
            }

            // ---- 本秒熱點 ----
            if (_secondHits.Count > 0)
            {
                _log.Line("   本秒熱點 (遊戲碼內，每格 16 bytes)");
                foreach (var (addr, hits) in _secondHits.OrderByDescending(kv => kv.Value).Take(8))
                {
                    string? what = Classify(addr, _annotate);
                    double pct = 100.0 * hits / Math.Max(1UL, _secondInModule);
                    if (row.TopAddress == 0) { row.TopAddress = addr; row.TopPercent = pct; }
                    _log.Line($"      {addr:X8}  {pct,6:F2}%  {hits,6}  {what ?? ""}");
                }
            }

            // ---- 模組變動 ----
            var modulesNow = SnapshotModules(_pid);
            if (modulesNow.Count > 0)
            {
                foreach (var m in modulesNow)
                {
                    if (!_modules.Any(o => o.Base == m.Base && o.Name == m.Name))
                        _log.Line($"   [!] 載入模組 {m.Name}  {m.Base:X8} - {m.End:X8}   {m.Path}");
                }
                foreach (var m in _modules)
                {
                    if (!modulesNow.Any(o => o.Base == m.Base && o.Name == m.Name))
                        _log.Line($"   [!] 卸載模組 {m.Name}  {m.Base:X8} - {m.End:X8}");
                }
                _modules = modulesNow;
            }

            // ---- 值得警告的狀況 ----
            WriteWarnings(row, space, gdi, user, handleCount);

            _log.Line();

            _history.Add(row);
            _lastPageFaults = haveMem ? pmc.PageFaultCount : _lastPageFaults;
            _lastHandles = handleCount;
            _lastGdi = gdi;
            _lastUser = user;
            _lastProcessCpu = procCpu;
            _lastIoRead = io.ReadTransferCount;
            _lastIoWrite = io.WriteTransferCount;
            _lastTickTime = t;

            _secondSamples = 0;
            _secondInModule = 0;
            _secondHits.Clear();
            _threadSecond.Clear();
        }

        /// <summary>
        /// 停一下這條執行緒，把完整的暫存器與堆疊抓下來。一秒一次，暫停時間約幾十微秒，
        /// 對遊戲沒有可感知的影響，但崩潰時這就是唯一的現場。
        /// </summary>
        private unsafe void DumpThreadDetail(uint tid, IntPtr handle)
        {
            if (Wow64SuspendThread(handle) == unchecked((uint)-1)) return;

            var ctx = new Wow64Context { ContextFlags = Wow64ContextFull };
            bool ok = Wow64GetThreadContext(handle, ref ctx);

            var stack = new byte[_stackBytes];
            int stackLen = 0;
            if (ok && ctx.Esp != 0 && _stackDepth > 0)
            {
                fixed (byte* p = stack)
                {
                    if (ReadProcessMemory(_process, (IntPtr)ctx.Esp, p, (IntPtr)_stackBytes, out IntPtr got))
                        stackLen = (int)got;
                }
            }

            ResumeThread(handle);
            if (!ok) return;

            string where = DescribeAddress(ctx.Eip, _modBase, _modSize, _annotate, _modules);
            string bytes = BytesAt(_image, _modBase, ctx.Eip, 8);
            _log.Line($"        EIP {ctx.Eip:X8}  ESP {ctx.Esp:X8}  EBP {ctx.Ebp:X8}  [{where}]{(bytes.Length > 0 ? $"  位元組 {bytes}" : "")}");
            _log.Line($"        EAX {ctx.Eax:X8} EBX {ctx.Ebx:X8} ECX {ctx.Ecx:X8} EDX {ctx.Edx:X8} ESI {ctx.Esi:X8} EDI {ctx.Edi:X8} EFLAGS {ctx.EFlags:X8}");

            if (stackLen >= 4 && _stackDepth > 0)
            {
                var frames = ScanStack(stack, stackLen, ctx.Esp, _modBase, _modSize, _image, _stackDepth);
                if (frames.Count == 0)
                {
                    _log.Line("        堆疊 (掃不到落在遊戲碼的回傳位址)");
                }
                else
                {
                    _log.Line("        堆疊 (由內而外；括號是發出 call 的位址)");
                    foreach (var f in frames)
                    {
                        string? what = Classify(f.CallSite, _annotate);
                        _log.Line($"          {f.StackAddress:X8} -> {f.ReturnAddress:X8}  (call @ {f.CallSite:X8}){(what is null ? "" : $"  {what}")}");
                    }
                }
            }
            else if (_stackDepth > 0)
            {
                _log.Line("        堆疊 (ESP 讀不到，執行緒可能正在核心態)");
            }
        }

        private void WriteWarnings(SecondRow row, AddressSpaceInfo space, uint gdi, uint user, uint handles)
        {
            if (space.Complete && space.UsedPercent >= 80.0)
                _log.Line($"   [警告] 位址空間已用 {space.UsedPercent:F1}%，接近 {Gb(space.Limit)} 上限 —— 下一次大塊配置失敗就會閃退");
            if (space.Complete && space.LargestFreeBlock < 64UL * 1048576)
                _log.Line($"   [警告] 最大連續空閒區塊只剩 {Mb(space.LargestFreeBlock)}，位址空間碎片化嚴重");
            if (gdi >= 6000)
                _log.Line($"   [警告] GDI 物件 {gdi} 個 (每程序上限 10000)，疑似 GDI 洩漏");
            if (user >= 6000)
                _log.Line($"   [警告] USER 物件 {user} 個 (每程序上限 10000)，疑似 USER 物件洩漏");
            if (handles >= 10000)
                _log.Line($"   [警告] 控制代碼 {handles} 個，疑似控制代碼洩漏");
            if (!row.WindowResponding)
                _log.Line("   [警告] 主視窗沒有回應，訊息迴圈卡住 (接下來很可能就是閃退或被系統結束)");
        }

        private static string Delta(uint now, uint before)
        {
            if (before == 0) return "起始";
            long d = (long)now - before;
            return d == 0 ? "±0" : (d > 0 ? $"+{d}" : d.ToString());
        }

        #endregion

        #region 收尾 / 崩潰報告

        /// <summary>遊戲程序不見了：把結束代碼、最後現場、趨勢、可能原因全部寫下來。</summary>
        public void WriteExitReport(double t, bool exitCodeKnown, uint exitCode, double runTime,
                                    string? capturedException)
        {
            // 結束代碼會騙人：引擎自己裝了 unhandled-exception filter，可以把存取違規吞掉
            // 再自行退出。但 first-chance 例外也可能被修復後繼續跑，所以這裡只把退出前
            // 最後一筆稱為「候選」，不把單一摘要冒充成已證明的根因。
            bool crashed = capturedException is not null || (exitCodeKnown && IsCrashExitCode(exitCode));

            _log.Line("================================================================================");
            _log.Line(crashed ? "  ***  遊戲閃退  ***" : "  遊戲程序已結束");
            _log.Line("================================================================================");
            _log.Line($"結束時間      : {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}   (開始後 {t:F1} 秒)");
            _log.Line($"結束代碼      : {(exitCodeKnown ? DescribeExitCode(exitCode) : "讀不到 (程序控制代碼已失效)")}");
            if (capturedException is not null)
            {
                _log.Line($"退出前最後例外: {capturedException}");
            }
            _log.Line($"判定          : {(capturedException is not null ? "疑似閃退 —— 退出前攔到 crash-looking 例外；根因請以最後現場與完整例外序列判讀" : crashed ? "不正常結束 —— 未處理的例外，引擎把 WER 關掉了所以沒有任何對話框" : exitCodeKnown && exitCode == 0 ? "正常結束" : "非例外結束 (可能是引擎自己 exit、或被外部結束)")}");
            if (capturedException is not null && exitCodeKnown && !IsCrashExitCode(exitCode))
            {
                _log.Line("                （結束代碼與 first-chance 例外都可能誤導；請以時間上最後的現場與完整序列為準。）");
            }
            _log.Line($"存活時間      : {runTime:F1} 秒");
            _log.Line();

            WriteLastKnownPositions();
            WriteTrend();
            WriteDiagnosis(crashed, exitCodeKnown, exitCode);
        }

        private void WriteLastKnownPositions()
        {
            _log.Line("--- 每個執行緒最後被取樣到的位置 ---");

            var byThread = new Dictionary<uint, List<RingEntry>>();
            int count = _ringWrapped ? _ring.Length : _ringPos;
            for (int i = 0; i < count; i++)
            {
                int idx = _ringWrapped ? (_ringPos + i) % _ring.Length : i;
                var e = _ring[idx];
                if (e.Tid == 0) continue;
                if (!byThread.TryGetValue(e.Tid, out var list))
                {
                    list = new List<RingEntry>();
                    byThread[e.Tid] = list;
                }
                list.Add(e);
            }

            if (byThread.Count == 0)
            {
                _log.Line("   (環形緩衝區是空的 —— 沒有取樣到任何東西)");
                _log.Line();
                return;
            }

            foreach (var (tid, list) in byThread.OrderByDescending(kv => kv.Value.Count))
            {
                var last = list[^1];
                string where = DescribeAddress(last.Eip, _modBase, _modSize, _annotate, _modules);
                _log.Line($"   tid {tid}  最後樣本 t={last.T:F3}s  EIP {last.Eip:X8}  ESP {last.Esp:X8}  [{where}]");

                string bytes = BytesAt(_image, _modBase, last.Eip, 16);
                if (bytes.Length > 0) _log.Line($"      該位址的位元組 {bytes}");

                int take = Math.Min(24, list.Count);
                _log.Line($"      最後 {take} 個樣本 (由新到舊)");
                for (int i = 0; i < take; i++)
                {
                    var e = list[list.Count - 1 - i];
                    string? what = Classify(e.Eip, _annotate);
                    _log.Line($"        t={e.T,9:F3}  EIP {e.Eip:X8}  ESP {e.Esp:X8}  {what ?? DescribeAddress(e.Eip, _modBase, _modSize, false, _modules)}");
                }

                // 最後那幾秒這條執行緒待在哪 —— 熱點集中就代表卡在同一段程式碼。
                var tail = list.Where(e => e.T >= last.T - 5.0).ToList();
                if (tail.Count > 4)
                {
                    var hot = tail.GroupBy(e => e.Eip & ~0xFu)
                                  .OrderByDescending(g => g.Count())
                                  .Take(5);
                    _log.Line("      崩潰前 5 秒最常出現的位置");
                    foreach (var g in hot)
                    {
                        string? what = Classify(g.Key, _annotate);
                        _log.Line($"        {g.Key:X8}  {100.0 * g.Count() / tail.Count,6:F1}%  {what ?? ""}");
                    }
                }
                _log.Line();
            }
        }

        private void WriteTrend()
        {
            if (_history.Count == 0) return;

            int take = Math.Min(30, _history.Count);
            _log.Line($"--- 崩潰前 {take} 秒的趨勢 ---");
            _log.Line("     秒     工作集MB   私有MB  位址空間%  最大空閒MB  控制代碼   GDI  USER  樣本  遊戲碼內%  視窗");
            for (int i = _history.Count - take; i < _history.Count; i++)
            {
                var r = _history[i];
                double inPct = r.Samples > 0 ? 100.0 * r.InModule / r.Samples : 0.0;
                string addressPct = r.AddressSpaceValid ? $"{r.AddressUsedPercent:F1}" : "n/a";
                string largestFree = r.AddressSpaceValid ? $"{r.LargestFreeBlock / 1048576.0:F1}" : "n/a";
                _log.Line($"   {r.T,6:F0}  {r.WorkingSet / 1048576.0,10:F1} {r.PrivateBytes / 1048576.0,8:F1} {addressPct,10} {largestFree,11} {r.Handles,9} {r.Gdi,5} {r.User,5} {r.Samples,5} {inPct,10:F1}  {(r.WindowResponding ? "正常" : "無回應")}");
            }
            _log.Line();
        }

        private void WriteDiagnosis(bool crashed, bool exitCodeKnown, uint exitCode)
        {
            _log.Line("--- 可能原因 ---");
            var notes = new List<string>();

            if (_history.Count >= 2)
            {
                var first = _history[0];
                var last = _history[^1];
                int window = Math.Min(30, _history.Count);
                var windowStart = _history[_history.Count - window];

                if (last.AddressSpaceValid && last.AddressUsedPercent >= 75.0)
                    notes.Add($"位址空間已用 {last.AddressUsedPercent:F1}%，逼近 {(_laa ? "4 GB" : "2 GB")} 上限 —— 32 位元行程的典型死法：某次配置失敗、引擎沒檢查回傳值就寫下去");
                if (last.AddressSpaceValid && last.LargestFreeBlock < 32UL * 1048576)
                    notes.Add($"最大連續空閒區塊只剩 {Mb(last.LargestFreeBlock)}，即使總量還夠，大塊配置 (地圖、surface) 也會失敗");

                double privateGrowth = (last.PrivateBytes - (double)windowStart.PrivateBytes) / 1048576.0;
                if (privateGrowth > 128)
                    notes.Add($"最後 {window} 秒私有記憶體增加 {privateGrowth:F0} MB，成長速度異常 (記憶體洩漏或一次性大量載入)");

                if (last.Gdi > first.Gdi + 500)
                    notes.Add($"GDI 物件從 {first.Gdi} 漲到 {last.Gdi}，GDI 洩漏會在達到 10000 上限時讓繪圖 API 全部失敗");
                if (last.Handles > first.Handles + 2000)
                    notes.Add($"控制代碼從 {first.Handles} 漲到 {last.Handles}，控制代碼洩漏");
                if (!last.WindowResponding)
                    notes.Add("崩潰前主視窗已經沒有回應 —— 先卡住再死，通常是無窮迴圈或死結，不是單純的野指標");

                var stalled = _history.Skip(Math.Max(0, _history.Count - 5)).Where(r => r.Samples > 0 && r.InModule * 10 < r.Samples).Count();
                if (stalled >= 3)
                    notes.Add("崩潰前多數樣本落在系統 DLL 而不是遊戲碼 —— 卡在系統呼叫 (檔案 I/O、GDI、等待) 而不是遊戲自己的迴圈");
            }

            if (exitCodeKnown)
            {
                switch (exitCode)
                {
                    case 0xC0000005:
                        notes.Add("存取違規：對照上面的最後 EIP 與堆疊，那個位址就是崩潰指令 (或極接近)。把 EIP 拿去查靜態反組譯即可定位");
                        break;
                    case 0xC00000FD:
                        notes.Add("堆疊溢位：看堆疊掃描有沒有同一個回傳位址重複出現 —— 那就是無窮遞迴的位置");
                        break;
                    case 0xC0000374:
                        notes.Add("堆積損毀：崩潰點不是肇因點。真正的越界寫入發生在更早，時間軸的記憶體欄位可以幫忙縮小範圍");
                        break;
                    case 0xC0000017:
                    case 0xC000017D:
                        notes.Add("記憶體配置失敗：確認位址空間欄位，以及是否已套用 LargeAddressAware");
                        break;
                }
            }

            if (!crashed && exitCodeKnown && exitCode == 0)
                notes.Add("這一次是正常結束，不是閃退 —— 記錄檔仍保留完整時間軸供效能分析");

            if (notes.Count == 0)
                notes.Add("沒有偵測到明顯的資源異常。請看上面「每個執行緒最後被取樣到的位置」，那是最接近崩潰的現場");

            foreach (string n in notes) _log.Line($"   * {n}");
            _log.Line();
        }

        public void WriteSummary(string report)
        {
            _log.Line("================================================================================");
            _log.Line("  全場彙總報告");
            _log.Line("================================================================================");
            _log.Block(report);
            _log.Line();
        }

        public void WriteNote(string text = "") => _log.Line(text);

        #endregion
    }
}
