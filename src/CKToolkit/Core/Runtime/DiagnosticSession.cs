using CKToolkit.I18n;
using CKToolkit.Core.Common;
using CKToolkit.Core.Perf;

namespace CKToolkit.Core.Runtime;

/// <summary>
/// 一次診斷執行 = 注入層 + 取樣／偵錯層，同一個入口、同一個 pid、同一個輸出資料夾。
///
/// 為什麼需要這一層：整合之前，工具裡有五顆按鈕分別走三套機制，而且兩套機制的輸出
/// 落在不同資料夾。實際代價已經付過一次——2026-08-22 的大軍團閃退，使用者按了修改器頁的
/// 「啟動遊戲」，以為分析器在記錄，但那條路只做了 <c>ckperf.dll</c> 注入，
/// <see cref="Profiler"/> 那套偵錯器與取樣器整場都沒有啟動，事後只剩半份證據。
///
/// 兩層的職責是互補而不是重複，所以現在一律一起開：
///   * 注入層（<c>ckperf.dll</c> 的 VEH）：遊戲內每幀計時、記憶體與位址空間碎片化遙測，
///     以及不需要偵錯器就能寫出的故障報告。這些資料只有在行程內部才拿得到。
///   * 取樣／偵錯層（<see cref="Profiler"/>）：外部偵錯器看得到第一手例外，能在例外
///     發生的那一瞬間寫出 minidump 與 JSON 狀態快照；再加上 EIP 熱區取樣。
///
/// 同一個例外會先送到偵錯器，偵錯器以 <c>DBG_EXCEPTION_NOT_HANDLED</c> 原封不動放行後，
/// 行程內的 VEH 才會收到。所以一次閃退會留下兩份可以互相佐證的 artefact——這正是
/// 「絕不能發生閃退然後什麼都沒輸出」這條要求想要的冗餘。
/// </summary>
public static class DiagnosticSession
{
    /// <summary>要怎麼讓遊戲跟診斷層碰頭。</summary>
    public enum AttachMode
    {
        /// <summary>由工具啟動遊戲。唯一能在進入點之前就把診斷層放進去的路。</summary>
        LaunchGame,

        /// <summary>掛到已經在跑的遊戲。啟動到掛上之間的事件會漏掉。</summary>
        AttachRunning,

        /// <summary>先按開始，再照常去 Steam 開遊戲；遊戲一出現就自動接上。</summary>
        WaitForGame,
    }

    public sealed class Options
    {
        public AttachMode Mode { get; set; } = AttachMode.LaunchGame;

        /// <summary>遊戲目錄。只有 <see cref="AttachMode.LaunchGame"/> 一定要有。</summary>
        public string GameDirectory { get; set; } = string.Empty;

        /// <summary>
        /// 要找的行程名稱。空的話用 <see cref="GameRunner.FindGameProcessId"/> 的預設比對（Celtic*）。
        /// 兩層都用這裡找到的同一個 pid，所以 CLI 的 <c>--process</c> 對注入層一樣有效。
        /// </summary>
        public string? ProcessName { get; set; }

        /// <summary>
        /// 要不要一併掛上遊戲內的注入層。預設 true——兩層互補，一起開才不會只留半份證據。
        /// 關掉只剩外部取樣器與偵錯器；<see cref="AttachMode.LaunchGame"/> 不支援關閉，
        /// 因為那條路上「啟動」與「注入」本來就是同一個動作。
        /// </summary>
        public bool InjectRuntimeLayer { get; set; } = true;

        /// <summary>注入層的開關，原封不動傳給 <c>ckperf.dll</c>。</summary>
        public DiagnosticsOptions Runtime { get; set; } = new();

        /// <summary>取樣／偵錯層的設定。<c>ProcessId</c> 與 <c>LogDirectory</c> 由本類別覆寫。</summary>
        public Profiler.Options Sampler { get; set; } = new();

        /// <summary>
        /// 分析紀錄的儲存位置。開跑時會自動在下面建立
        /// <c>CKToolkit 分析紀錄\日期\單次執行</c>；空的話以
        /// <see cref="GameRunner.DiagnosticsDirectory"/> 為儲存位置。
        /// </summary>
        public string? OutputDirectory { get; set; }

        /// <summary>等遊戲出現的上限。逾時回傳失敗，不會無限期佔著。</summary>
        public TimeSpan WaitTimeout { get; set; } = TimeSpan.FromMinutes(30);

        /// <summary>當下的工具設定，寫進執行清單。沒有的話就不寫清單。</summary>
        public ToolkitConfig? Config { get; set; }

        public Action<string>? Log { get; set; }

        public CancellationToken Cancel { get; set; }
    }

    /// <summary>一次執行留下的全部東西：兩層的狀態，加上取樣器的報告與檔案路徑。</summary>
    public sealed class SessionResult
    {
        public uint ProcessId { get; init; }
        public string OutputDirectory { get; init; } = string.Empty;

        /// <summary>注入層是否真的就位。false 時 <see cref="RuntimeLayerNote"/> 說明原因。</summary>
        public bool RuntimeLayerActive { get; init; }

        /// <summary>注入層是否趕在進入點之前就位。只有自行啟動遊戲時才可能為 true。</summary>
        public bool InjectedBeforeEntryPoint { get; init; }

        public string? RuntimeLayerNote { get; init; }

        public Profiler.RunResult Sampler { get; init; } = new();
    }

    /// <summary>
    /// 跑完一整場診斷。這個呼叫會一直阻塞到遊戲結束、時間到、或被取消。
    /// </summary>
    public static Result<SessionResult> Run(Options opt)
    {
        void Output(string message) => opt.Log?.Invoke(message);

        string selectedLocation = string.IsNullOrWhiteSpace(opt.OutputDirectory)
            ? GameRunner.DiagnosticsDirectory
            : opt.OutputDirectory!;
        string outDir;
        try
        {
            outDir = DiagnosticOutputLayout.CreateSessionDirectory(
                selectedLocation, ModeFolderName(opt.Mode), DateTime.Now);
        }
        catch (Exception ex)
        {
            return Result<SessionResult>.Fail(Strings.Get("Error_DiagOutputDirFailed", selectedLocation, ex.Message));
        }

        // 兩層都指到同一個資料夾。這是整合最實際的那一半：事後撿檔案時不必再記得
        // 哪一種證據被寫到哪裡去。
        opt.Runtime.OutputDirectory = outDir;
        opt.Sampler.LogDirectory = outDir;
        if (!string.IsNullOrWhiteSpace(opt.Sampler.LogFile))
            opt.Sampler.LogFile = Path.Combine(outDir, Path.GetFileName(opt.Sampler.LogFile));
        Output($"診斷輸出資料夾：{outDir}");

        WriteManifest(outDir, opt);

        var warnings = new List<string>();
        uint pid;
        bool runtimeActive;
        bool beforeEntryPoint = false;
        string? runtimeNote = null;

        if (opt.Mode == AttachMode.LaunchGame)
        {
            // 啟動與注入在這條路上是同一個動作（行程是暫停建立、注入完才放行的），
            // 所以拆不開：啟動失敗就是整場失敗，沒有可以退而求其次的東西。
            Result<RunOutcome> launched = GameRunner.LaunchWithDiagnostics(opt.GameDirectory, opt.Runtime, Output);
            if (!launched.Success || launched.Value is null)
                return Result<SessionResult>.Fail(launched.ErrorMessage ?? Strings.Get("Error_DiagLaunchFailed"), launched.ExitCode);

            pid = launched.Value.ProcessId;
            beforeEntryPoint = launched.Value.InjectedBeforeEntryPoint;
            runtimeActive = true;
            warnings.AddRange(launched.Warnings);
        }
        else
        {
            pid = opt.Mode == AttachMode.AttachRunning
                ? FindProcess(opt)
                : WaitForGame(opt, Output);

            if (pid == 0)
            {
                return Result<SessionResult>.Fail(opt.Mode == AttachMode.AttachRunning
                    ? "找不到執行中的《Celtic Kings》行程。請先開遊戲，或改用「由工具啟動遊戲」。"
                    : opt.Cancel.IsCancellationRequested
                        ? "已取消等待。"
                        : "等待逾時，期間沒有偵測到《Celtic Kings》啟動。");
            }

            if (!opt.InjectRuntimeLayer)
            {
                runtimeActive = false;
                runtimeNote = "呼叫端要求不掛注入層";
                Output("注入層已停用；這一場只有取樣器與偵錯器的記錄。");
            }
            else
            {
                // 注入失敗不是整場失敗。遊戲確實在跑，取樣器與偵錯器照樣接得上，
                // 而偵錯器正好是三種證據裡最關鍵的那一種——為了少一層遙測而整場不記錄，
                // 就是把「閃退了卻什麼都沒輸出」再演一次。
                Result<RunOutcome> attached = GameRunner.AttachToProcess(pid, opt.Runtime, Output);
                runtimeActive = attached.Success;
                if (runtimeActive)
                {
                    warnings.AddRange(attached.Warnings);
                }
                else
                {
                    runtimeNote = attached.ErrorMessage;
                    Output($"注入層未就位（{runtimeNote}）；取樣器與偵錯器仍會照常記錄這一場。");
                    warnings.Add($"注入層未就位：{runtimeNote}");
                }
            }
        }

        // 取樣器接的是上面已經確定的那一個 pid，不再自己找一次。
        opt.Sampler.ProcessId = pid;
        opt.Sampler.WaitForProcess = false;
        opt.Sampler.Log ??= opt.Log;
        opt.Sampler.CancelRequested ??= () => opt.Cancel.IsCancellationRequested;

        Result<Profiler.RunResult> sampled = Profiler.Run(opt.Sampler);

        var result = new SessionResult
        {
            ProcessId = pid,
            OutputDirectory = outDir,
            RuntimeLayerActive = runtimeActive,
            InjectedBeforeEntryPoint = beforeEntryPoint,
            RuntimeLayerNote = runtimeNote,
            Sampler = sampled.Value ?? new Profiler.RunResult(),
        };

        if (!sampled.Success)
        {
            string detail = sampled.ErrorMessage ?? "取樣分析失敗。";
            if (runtimeActive)
                detail += $" 注入層仍在記錄，遊戲結束後請到 {outDir} 查看 ckperf-*.log。";
            return Result<SessionResult>.Fail(detail, sampled.ExitCode, warnings);
        }

        warnings.AddRange(sampled.Warnings);
        return Result<SessionResult>.Ok(result, warnings);
    }

    private static string ModeFolderName(AttachMode mode) => mode switch
    {
        AttachMode.LaunchGame => "launch",
        AttachMode.AttachRunning => "attach",
        AttachMode.WaitForGame => "wait",
        _ => "session"
    };

    /// <summary>找出要記錄的行程；找不到回傳 0。</summary>
    private static uint FindProcess(Options opt)
    {
        if (string.IsNullOrWhiteSpace(opt.ProcessName)) return (uint)GameRunner.FindGameProcessId();

        string name = Path.GetFileNameWithoutExtension(opt.ProcessName)!;
        try
        {
            var found = System.Diagnostics.Process.GetProcessesByName(name);
            return found.Length == 0 ? 0u : (uint)found[0].Id;
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    /// 等遊戲出現，回傳 pid；被取消或逾時回傳 0。
    ///
    /// 刻意不用 <see cref="GameRunner.WaitForGameAndAttach"/>：那個方法把「等待」跟
    /// 「注入」綁在一起，注入失敗時 pid 就跟著丟掉了，而這裡即使注入不成也還要用 pid
    /// 去接取樣器。
    /// </summary>
    private static uint WaitForGame(Options opt, Action<string> output)
    {
        output($"等待《Celtic Kings》啟動（最多 {opt.WaitTimeout.TotalMinutes:F0} 分鐘）……現在可以照常去 Steam 開遊戲了。");

        DateTime deadline = DateTime.UtcNow + opt.WaitTimeout;
        while (DateTime.UtcNow < deadline)
        {
            if (opt.Cancel.IsCancellationRequested) return 0;

            uint pid = FindProcess(opt);
            if (pid != 0)
            {
                output($"偵測到遊戲行程 pid {pid}，立刻就位。");
                return pid;
            }
            Thread.Sleep(250);
        }
        return 0;
    }

    /// <summary>
    /// 把當下的工具設定寫成執行清單。
    ///
    /// 寫不出來不擋住診斷本身：故障報告仍然有價值，只是解讀時要自己回想當時的設定。
    /// </summary>
    private static void WriteManifest(string outDir, Options opt)
    {
        if (opt.Config is null || string.IsNullOrWhiteSpace(opt.GameDirectory)) return;
        try
        {
            RunManifest.Write(outDir, opt.GameDirectory, opt.Config, opt.Runtime);
        }
        catch
        {
            // 見上方註解：清單是輔助資料，不值得為它讓整場診斷失敗。
        }
    }
}
