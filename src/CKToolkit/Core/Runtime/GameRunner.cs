using System.Reflection;
using CKToolkit.Core.Common;

namespace CKToolkit.Core.Runtime;

/// <summary>
/// 診斷層的開關。全部經由子程序環境變數傳給 <c>ckperf.dll</c>，
/// 不在遊戲目錄留下任何設定檔——遊戲裝在 Program Files，本來就寫不進去。
/// </summary>
public sealed class DiagnosticsOptions
{
    /// <summary>攔截例外並寫出故障報告。這是找出「單位一多就閃退」真正錯誤位址的唯一手段。</summary>
    public bool CrashReports { get; set; } = true;

    /// <summary>故障報告之外再寫一份 minidump。</summary>
    public bool MiniDumps { get; set; } = true;

    /// <summary>背景記錄記憶體用量與位址空間碎片化。</summary>
    public bool Telemetry { get; set; } = true;

    /// <summary>量測每幀時間與 GDI 全螢幕搬移成本。純觀測，不改變任何行為。</summary>
    public bool FrameTiming { get; set; } = true;

    /// <summary>單次執行最多寫出幾份故障報告，避免連續錯誤把磁碟塞爆。</summary>
    public int MaxReports { get; set; } = 20;

    /// <summary>背景取樣週期（毫秒）。</summary>
    public int TelemetryMs { get; set; } = 1000;

    internal string ToOptionString() =>
        $"crash={(CrashReports ? 1 : 0)},dump={(MiniDumps ? 1 : 0)}," +
        $"telemetry={(Telemetry ? 1 : 0)},frames={(FrameTiming ? 1 : 0)}," +
        $"maxreports={MaxReports},telemetryms={TelemetryMs}";
}

public sealed record RunOutcome(uint ProcessId, string OutputDirectory, string Detail, bool InjectedBeforeEntryPoint);

/// <summary>
/// 帶診斷層啟動遊戲。
///
/// 這是「先閃退、再效能」路線的第一塊：引擎自己呼叫 <c>SetErrorMode</c> 與
/// <c>SetUnhandledExceptionFilter</c>，所以崩潰從來不會走到 WER，使用者只看到遊戲
/// 憑空消失。注入一個向量化例外處理常式是唯一能在第一時間攔到真正錯誤位址的方法。
///
/// 本類別對遊戲檔案零寫入。<c>ckperf.dll</c> 會展開到 <c>%LOCALAPPDATA%\CKToolkit</c>，
/// 報告也寫在那裡。
/// </summary>
public static class GameRunner
{
    private const string EmbeddedDllResource = "CKToolkit.Runtime.ckperf.dll";

    /// <summary>診斷輸出目錄：<c>%LOCALAPPDATA%\CKToolkit\diag</c>。</summary>
    public static string DiagnosticsDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                     "CKToolkit", "diag");

    private static string RuntimeDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                     "CKToolkit", "runtime");

    /// <summary>
    /// 啟動遊戲並注入診斷層。
    /// </summary>
    /// <param name="gameDir">遊戲目錄（必須通過 <see cref="GamePaths.IsGameDir"/>）。</param>
    public static Result<RunOutcome> LaunchWithDiagnostics(
        string gameDir, DiagnosticsOptions options, Action<string>? log = null)
    {
        if (!GamePaths.IsGameDir(gameDir))
            return Result<RunOutcome>.Fail($"不是有效的遊戲目錄：{gameDir}", ExitCodes.GameNotFound);

        string exe = Path.Combine(gameDir, GamePaths.ExeFileName);

        Result<string> dll = ExtractRuntimeDll();
        if (dll.IsError) return Result<RunOutcome>.Fail(dll.ErrorMessage!, dll.ExitCode);

        string outDir = DiagnosticsDirectory;
        Directory.CreateDirectory(outDir);

        WriteSettingsFile(options, outDir);

        var env = new Dictionary<string, string>
        {
            ["CKPERF_OUT"]  = outDir,
            ["CKPERF_OPTS"] = options.ToOptionString(),
        };

        var r = ProcessInjector.LaunchAndInject(exe, gameDir, dll.Value!, env, log);
        if (r.ProcessId == 0)
            return Result<RunOutcome>.Fail(r.Detail);

        var warnings = new List<string>();
        if (!r.InjectedBeforeEntryPoint)
            warnings.Add("診斷層未能在進入點之前就位；遊戲啟動最初數毫秒內的事件不會被記錄。");

        return Result<RunOutcome>.Ok(new RunOutcome(r.ProcessId, outDir, r.Detail, r.InjectedBeforeEntryPoint), warnings);
    }

    /// <summary>
    /// 掛載到已經在執行的遊戲。
    ///
    /// 使用者會從 Steam 開遊戲，這是常態而不是例外。這條路上診斷層會晚幾秒才就位，
    /// 但對「打很久才閃退」的目標而言那幾秒毫無影響——真正的損失是整場都沒插樁。
    /// </summary>
    public static Result<RunOutcome> AttachToRunningGame(DiagnosticsOptions options, Action<string>? log = null)
    {
        int pid = FindGameProcessId();
        if (pid == 0)
            return Result<RunOutcome>.Fail("找不到執行中的《Celtic Kings》行程。請先開遊戲，或改用「帶診斷啟動遊戲」。");

        return AttachToProcess((uint)pid, options, log);
    }

    /// <summary>
    /// 等遊戲出現，一出現就掛上去。
    ///
    /// 這是給「我想照常從 Steam 開遊戲」準備的：先按這個，再去 Steam 按開始。
    /// </summary>
    /// <param name="timeout">等待上限。逾時回傳失敗，不會無限期佔著。</param>
    public static Result<RunOutcome> WaitForGameAndAttach(
        DiagnosticsOptions options, TimeSpan timeout, CancellationToken cancel = default, Action<string>? log = null)
    {
        log?.Invoke($"等待《Celtic Kings》啟動（最多 {timeout.TotalSeconds:F0} 秒）……現在可以去 Steam 開遊戲了。");

        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (cancel.IsCancellationRequested)
                return Result<RunOutcome>.Fail("已取消等待。");

            int pid = FindGameProcessId();
            if (pid != 0)
            {
                log?.Invoke($"偵測到遊戲行程 pid {pid}，正在掛載。");
                return AttachToProcess((uint)pid, options, log);
            }
            Thread.Sleep(250);
        }
        return Result<RunOutcome>.Fail("等待逾時，期間沒有偵測到《Celtic Kings》啟動。");
    }

    /// <summary>
    /// 常駐監看：只要遊戲出現就掛上去，掛完繼續等下一次，直到被取消。
    ///
    /// 為什麼需要這個而不是只有單次等待：兩次「玩到閃退」的回報最後都發現整場沒有被
    /// 插樁，因為使用者是從 Steam 開遊戲的，而那條路上沒有任何注入點。要求人每次改用
    /// 別的方式啟動，是把工具的缺陷轉嫁給使用者；讓工具自己等，才是正確的分工。
    ///
    /// 已經掛載過的行程會被記住，所以重複輪詢不會重複注入。
    /// </summary>
    public static void WatchForever(
        DiagnosticsOptions options, CancellationToken cancel, Action<string> log)
    {
        // The watcher keeps its OWN file, separate from the injected DLL's log.
        //
        // Three sessions in a row were reported as crashes with no diagnostics at all,
        // and each time the only thing on disk was the toolkit's own smoke test -- which
        // proves nothing was captured but says nothing about WHY. A watcher that leaves
        // no trace when it fails to attach is a collector that cannot be debugged. This
        // file records every decision it makes, so an empty diag folder is never again
        // the whole story.
        string watchLog = Path.Combine(DiagnosticsDirectory,
            $"ckwatch-{DateTime.Now:yyyyMMdd-HHmmss}.log");
        void Trace(string m)
        {
            log(m);
            try
            {
                File.AppendAllText(watchLog, $"[{DateTime.Now:HH:mm:ss.fff}] {m}{Environment.NewLine}");
            }
            catch
            {
                // Never let logging failure stop the watching.
            }
        }

        try { Directory.CreateDirectory(DiagnosticsDirectory); } catch { /* reported below */ }

        Trace($"常駐監看已啟動（本工具 pid {Environment.ProcessId}，"
            + $"{(Environment.IsPrivilegedProcess ? "以系統管理員身分執行" : "一般權限")}）。");
        Trace($"監看紀錄：{watchLog}");
        Trace("現在可以照常從 Steam 開遊戲——每一次啟動都會自動掛上診斷層。");

        var handled = new HashSet<int>();
        int idleTicks = 0;
        while (!cancel.IsCancellationRequested)
        {
            int pid = FindGameProcessId();
            if (pid != 0 && !handled.Contains(pid))
            {
                handled.Add(pid);
                if (ProcessInjector.IsAlreadyInjected((uint)pid))
                {
                    Trace($"pid {pid} 已經載入 ckperf.dll，略過。");
                }
                else
                {
                    Trace($"偵測到遊戲行程 pid {pid}，正在掛載……");
                    Result<RunOutcome> r = AttachToProcess((uint)pid, options, Trace);
                    Trace(r.IsOk
                        ? $"pid {pid} 已就位。診斷輸出：{DiagnosticsDirectory}"
                        : $"pid {pid} 掛載失敗：{r.ErrorMessage}");
                }
            }

            // 行程結束後把它從已處理清單移除，這樣同一個 pid 被系統重用時仍會處理。
            if (pid == 0 && handled.Count > 0) handled.Clear();

            // A heartbeat every two minutes. Without it, a watcher that ran all evening
            // and simply never saw the game is indistinguishable from one that was never
            // started at all.
            if (++idleTicks >= 480)
            {
                idleTicks = 0;
                Trace(pid == 0 ? "監看中，目前沒有偵測到遊戲行程。" : $"監看中，遊戲 pid {pid} 執行中。");
            }

            try { Task.Delay(250, cancel).Wait(cancel); }
            catch (OperationCanceledException) { break; }
            catch (AggregateException) { break; }
        }
        Trace("常駐監看已停止。");
    }

    private static Result<RunOutcome> AttachToProcess(uint pid, DiagnosticsOptions options, Action<string>? log)
    {
        if (ProcessInjector.IsAlreadyInjected(pid))
        {
            return Result<RunOutcome>.Fail(
                $"pid {pid} 已經載入 ckperf.dll，這一場已經在記錄了。" +
                $"若要換設定重新量測，請關掉遊戲再重開。");
        }

        Result<string> dll = ExtractRuntimeDll();
        if (dll.IsError) return Result<RunOutcome>.Fail(dll.ErrorMessage!, dll.ExitCode);

        string outDir = DiagnosticsDirectory;
        Directory.CreateDirectory(outDir);

        // 掛載模式沒有機會設定子程序環境（行程是別人開的），設定只能經由
        // DLL 旁邊的 ckperf.ini 傳遞。所以這一步一定要在注入之前完成。
        WriteSettingsFile(options, outDir);

        var r = ProcessInjector.AttachAndInject(pid, dll.Value!, log);
        if (r.ProcessId == 0)
            return Result<RunOutcome>.Fail(r.Detail);

        return Result<RunOutcome>.Ok(
            new RunOutcome(r.ProcessId, outDir, r.Detail, InjectedBeforeEntryPoint: false),
            ["以掛載方式就位，遊戲啟動到掛載之間發生的事件不會被記錄。"]);
    }

    /// <summary>回傳執行中的遊戲行程 ID；找不到回傳 0。</summary>
    public static int FindGameProcessId()
    {
        try
        {
            foreach (var p in System.Diagnostics.Process.GetProcesses())
            {
                try
                {
                    if (p.ProcessName.StartsWith("Celtic", StringComparison.OrdinalIgnoreCase))
                        return p.Id;
                }
                catch
                {
                    // 無權限查詢的行程直接跳過。
                }
            }
        }
        catch
        {
            // 列舉整批失敗時當作找不到。
        }
        return 0;
    }

    /// <summary>
    /// 把診斷設定寫到 DLL 旁邊的 <c>ckperf.ini</c>。
    ///
    /// UTF-16LE 加 BOM：<c>GetPrivateProfileStringW</c> 只在看到 BOM 時才會用 UTF-16
    /// 解讀，而使用者名稱可能含非 ASCII 字元，輸出路徑就會跟著含非 ASCII 字元。
    /// </summary>
    private static void WriteSettingsFile(DiagnosticsOptions options, string outDir)
    {
        try
        {
            Directory.CreateDirectory(RuntimeDirectory);
            string ini =
                "[ckperf]\r\n" +
                $"out={outDir}\r\n" +
                $"opts={options.ToOptionString()}\r\n";
            File.WriteAllText(Path.Combine(RuntimeDirectory, "ckperf.ini"), ini,
                              new System.Text.UnicodeEncoding(bigEndian: false, byteOrderMark: true));
        }
        catch
        {
            // 寫不出來就讓 DLL 走內建預設值（全開、輸出到 LocalAppData），
            // 那已經是我們想要的行為，不值得為此讓啟動失敗。
        }
    }

    /// <summary>
    /// 把內嵌的 <c>ckperf.dll</c> 展開到 <c>%LOCALAPPDATA%\CKToolkit\runtime</c>。
    ///
    /// 為什麼不直接從工具所在目錄注入：遠端 <c>LoadLibraryA</c> 收的是 ANSI 路徑，
    /// 而本工具很可能被放在含非 ANSI 字元的目錄（這個專案自己就在「離線儲存」底下）。
    /// LocalAppData 之下的路徑是純 ASCII，注入才穩。
    /// </summary>
    private static Result<string> ExtractRuntimeDll()
    {
        try
        {
            Directory.CreateDirectory(RuntimeDirectory);
            string target = Path.Combine(RuntimeDirectory, "ckperf.dll");

            using Stream? src = Assembly.GetExecutingAssembly().GetManifestResourceStream(EmbeddedDllResource);
            if (src is null)
                return Result<string>.Fail("執行檔內找不到內嵌的 ckperf.dll；請以 tools/perf/build-ckperf.ps1 重建後再建置。");

            byte[] wanted = new byte[src.Length];
            src.ReadExactly(wanted);

            // 只在內容不同時覆寫。遊戲執行中時 DLL 是被鎖住的，重寫會失敗；
            // 位元組相同就沒有重寫的必要，也就不會因為上一局還開著而卡住這一局。
            if (File.Exists(target) && File.ReadAllBytes(target).AsSpan().SequenceEqual(wanted))
                return Result<string>.Ok(target);

            File.WriteAllBytes(target, wanted);
            return Result<string>.Ok(target);
        }
        catch (IOException ex)
        {
            return Result<string>.Fail($"展開 ckperf.dll 失敗（可能有另一個遊戲行程仍載入著它）：{ex.Message}", ExitCodes.FileLocked);
        }
        catch (Exception ex)
        {
            return Result<string>.Fail($"展開 ckperf.dll 失敗：{ex.Message}");
        }
    }
}
