using System.Text.Json;
using CKToolkit.Core.Common;
using CKToolkit.Core.Perf;
using CKToolkit.Core.Runtime;
using CKToolkit.I18n;

namespace CKToolkit.Cli;

public static partial class CliHost
{
    private static int HandleProfile(
        List<string> options,
        string? gameDirOverride,
        string? configPathOverride,
        bool isJson,
        TextWriter stdout,
        TextWriter stderr)
    {
        var opt = new Profiler.Options
        {
            Seconds = 0,
            Hz = 250,
            SegmentSeconds = 60,
            WaitForProcess = false,
            ProcessName = "Celtic kings.exe"
        };
        var mode = DiagnosticSession.AttachMode.AttachRunning;
        bool inject = true;

        for (int i = 0; i < options.Count; i++)
        {
            string token = options[i];
            string flag;
            string? val = null;

            if (token.StartsWith("--"))
            {
                int eqIdx = token.IndexOf('=');
                if (eqIdx > 0)
                {
                    flag = token[..eqIdx].ToLowerInvariant();
                    val = token[(eqIdx + 1)..];
                }
                else
                {
                    flag = token.ToLowerInvariant();
                    // 開關型旗標：後面可以接 on/off，也可以什麼都不接（視為 on）。
                    if (flag is "--wait" or "--detail" or "--catch-crash" or "--full-dump" or "--no-inject")
                    {
                        if (i + 1 < options.Count && (options[i + 1].Equals("on", StringComparison.OrdinalIgnoreCase) ||
                                                      options[i + 1].Equals("off", StringComparison.OrdinalIgnoreCase) ||
                                                      options[i + 1].Equals("true", StringComparison.OrdinalIgnoreCase) ||
                                                      options[i + 1].Equals("false", StringComparison.OrdinalIgnoreCase)))
                        {
                            val = options[++i];
                        }
                    }
                    else if (i + 1 < options.Count)
                    {
                        val = options[++i];
                    }
                    else
                    {
                        string err = Strings.Get("Error_InvalidArgs", $"缺少選項值 '{token}'");
                        return OutputError("profile", err, ExitCodes.InvalidArgs, isJson, stdout, stderr);
                    }
                }

                switch (flag)
                {
                    case "--seconds":
                        if (int.TryParse(val, out int sec) && sec >= 0)
                        {
                            opt.Seconds = sec;
                        }
                        else
                        {
                            return OutputError("profile", Strings.Get("Error_InvalidArgs", $"--seconds 必須為大於或等於 0 的整數，實際為 '{val}'"), ExitCodes.InvalidArgs, isJson, stdout, stderr);
                        }
                        break;

                    case "--hz":
                        if (int.TryParse(val, out int hz) && hz > 0)
                        {
                            opt.Hz = hz;
                        }
                        else
                        {
                            return OutputError("profile", Strings.Get("Error_InvalidArgs", $"--hz 必須為大於 0 的整數，實際為 '{val}'"), ExitCodes.InvalidArgs, isJson, stdout, stderr);
                        }
                        break;

                    case "--segment":
                        if (int.TryParse(val, out int seg) && seg > 0)
                        {
                            opt.SegmentSeconds = seg;
                        }
                        else
                        {
                            return OutputError("profile", Strings.Get("Error_InvalidArgs", $"--segment 必須為大於 0 的整數，實際為 '{val}'"), ExitCodes.InvalidArgs, isJson, stdout, stderr);
                        }
                        break;

                    case "--out":
                        if (!string.IsNullOrWhiteSpace(val))
                        {
                            opt.LogFile = val;
                        }
                        else
                        {
                            return OutputError("profile", Strings.Get("Error_InvalidArgs", "--out 必須指定檔案路徑"), ExitCodes.InvalidArgs, isJson, stdout, stderr);
                        }
                        break;

                    case "--log-dir":
                        if (!string.IsNullOrWhiteSpace(val))
                        {
                            opt.LogDirectory = val;
                        }
                        else
                        {
                            return OutputError("profile", Strings.Get("Error_InvalidArgs", "--log-dir 必須指定資料夾路徑"), ExitCodes.InvalidArgs, isJson, stdout, stderr);
                        }
                        break;

                    case "--detail":
                        if (val is null) { opt.Detailed = true; }
                        else if (TryParseOnOff(val, out bool detailVal)) { opt.Detailed = detailVal; }
                        else
                        {
                            return OutputError("profile", Strings.Get("Error_InvalidArgs", $"--detail 必須為 on 或 off，實際為 '{val}'"), ExitCodes.InvalidArgs, isJson, stdout, stderr);
                        }
                        break;

                    case "--catch-crash":
                        if (val is null) { opt.CatchCrash = true; }
                        else if (TryParseOnOff(val, out bool catchVal)) { opt.CatchCrash = catchVal; }
                        else
                        {
                            return OutputError("profile", Strings.Get("Error_InvalidArgs", $"--catch-crash 必須為 on 或 off，實際為 '{val}'"), ExitCodes.InvalidArgs, isJson, stdout, stderr);
                        }
                        break;

                    case "--full-dump":
                        if (val is null) { opt.FullMemoryDump = true; }
                        else if (TryParseOnOff(val, out bool dumpVal)) { opt.FullMemoryDump = dumpVal; }
                        else
                        {
                            return OutputError("profile", Strings.Get("Error_InvalidArgs", $"--full-dump 必須為 on 或 off，實際為 '{val}'"), ExitCodes.InvalidArgs, isJson, stdout, stderr);
                        }
                        break;

                    case "--stack-depth":
                        if (int.TryParse(val, out int depth) && depth >= 0 && depth <= 64)
                        {
                            opt.StackDepth = depth;
                        }
                        else
                        {
                            return OutputError("profile", Strings.Get("Error_InvalidArgs", $"--stack-depth 必須為 0..64 的整數，實際為 '{val}'"), ExitCodes.InvalidArgs, isJson, stdout, stderr);
                        }
                        break;

                    case "--speed":
                        if (int.TryParse(val, out int speed) && speed >= 0 && speed <= 100)
                        {
                            opt.SpeedMultiplier = speed;
                        }
                        else
                        {
                            return OutputError("profile", Strings.Get("Error_InvalidArgs", $"--speed 必須為 0..100 的倍率，實際為 '{val}'"), ExitCodes.InvalidArgs, isJson, stdout, stderr);
                        }
                        break;

                    case "--speed-method":
                        if (string.Equals(val, "hotkey", StringComparison.OrdinalIgnoreCase))
                        {
                            opt.SpeedMethod = GameSpeed.Method.Hotkey;
                        }
                        else if (string.Equals(val, "console", StringComparison.OrdinalIgnoreCase))
                        {
                            opt.SpeedMethod = GameSpeed.Method.Console;
                        }
                        else
                        {
                            return OutputError("profile", Strings.Get("Error_InvalidArgs", $"--speed-method 必須為 hotkey 或 console，實際為 '{val}'"), ExitCodes.InvalidArgs, isJson, stdout, stderr);
                        }
                        break;

                    case "--process":
                        if (!string.IsNullOrWhiteSpace(val))
                        {
                            opt.ProcessName = val;
                        }
                        else
                        {
                            return OutputError("profile", Strings.Get("Error_InvalidArgs", "--process 必須指定程序名稱"), ExitCodes.InvalidArgs, isJson, stdout, stderr);
                        }
                        break;

                    // --wait 是 --mode wait 的舊寫法，保留下來讓既有腳本不必改。
                    case "--wait":
                        if (val is null)
                        {
                            mode = DiagnosticSession.AttachMode.WaitForGame;
                        }
                        else if (TryParseOnOff(val, out bool waitVal))
                        {
                            if (waitVal) mode = DiagnosticSession.AttachMode.WaitForGame;
                        }
                        else
                        {
                            return OutputError("profile", Strings.Get("Error_InvalidArgs", $"--wait 必須為 on 或 off，實際為 '{val}'"), ExitCodes.InvalidArgs, isJson, stdout, stderr);
                        }
                        break;

                    case "--mode":
                        switch (val?.ToLowerInvariant())
                        {
                            case "launch": mode = DiagnosticSession.AttachMode.LaunchGame; break;
                            case "attach": mode = DiagnosticSession.AttachMode.AttachRunning; break;
                            case "wait":   mode = DiagnosticSession.AttachMode.WaitForGame; break;
                            default:
                                return OutputError("profile", Strings.Get("Error_InvalidArgs", $"--mode 必須為 launch、attach 或 wait，實際為 '{val}'"), ExitCodes.InvalidArgs, isJson, stdout, stderr);
                        }
                        break;

                    case "--no-inject":
                        inject = false;
                        break;

                    default:
                        return OutputError("profile", Strings.Get("Error_InvalidArgs", $"未知的分析器選項 '{token}'"), ExitCodes.InvalidArgs, isJson, stdout, stderr);
                }
            }
            else
            {
                return OutputError("profile", Strings.Get("Error_InvalidArgs", $"未知的參數語法 '{token}'"), ExitCodes.InvalidArgs, isJson, stdout, stderr);
            }
        }

        if (!isJson)
        {
            opt.Log = msg => stdout.WriteLine(msg);
        }

        var toolkitConfig = ToolkitConfig.Load(configPathOverride);
        string? gameDir = GamePaths.FindGameDir(gameDirOverride, toolkitConfig.GameDir);
        bool gameDirUsable = gameDir is not null && GamePaths.IsGameDir(gameDir);

        // 只有「由工具啟動遊戲」非要有效的遊戲目錄不可；另外兩種模式是掛到別人開的
        // 行程上，沒有目錄照樣記錄得到（只是執行清單會少一份）。
        if (mode == DiagnosticSession.AttachMode.LaunchGame && !gameDirUsable)
            return OutputError("profile", Strings.Get("Error_GameNotFound"), ExitCodes.GameNotFound, isJson, stdout, stderr);

        // 舊版 --out 是完整檔名；現在仍保留自訂檔名，但它所在的
        // 資料夾改為「儲存位置」，所有兩層證據仍進入同一個自動分類的場次資料夾。
        string? selectedOutputLocation = opt.LogDirectory;
        if (string.IsNullOrWhiteSpace(selectedOutputLocation) && !string.IsNullOrWhiteSpace(opt.LogFile))
        {
            string fullLogPath = Path.GetFullPath(opt.LogFile);
            selectedOutputLocation = Path.GetDirectoryName(fullLogPath);
        }

        var session = new DiagnosticSession.Options
        {
            Mode = mode,
            GameDirectory = gameDirUsable ? gameDir! : string.Empty,
            ProcessName = opt.ProcessName,
            InjectRuntimeLayer = inject,
            OutputDirectory = selectedOutputLocation,
            Config = gameDirUsable ? toolkitConfig : null,
            Sampler = opt,
            Log = opt.Log,
        };

        var result = DiagnosticSession.Run(session);

        if (!result.Success)
        {
            return OutputError("profile", result.ErrorMessage ?? Strings.Get("Error_GeneralFailure", "取樣分析失敗"), result.ExitCode, isJson, stdout, stderr);
        }

        var value = result.Value!;
        var run = value.Sampler;
        string reportText = run.Report;

        var data = new
        {
            processName = opt.ProcessName,
            processId = value.ProcessId,
            mode = mode.ToString(),
            runtimeLayerActive = value.RuntimeLayerActive,
            runtimeLayerEarly = value.InjectedBeforeEntryPoint,
            runtimeLayerNote = value.RuntimeLayerNote,
            outputDirectory = value.OutputDirectory,
            seconds = opt.Seconds,
            hz = opt.Hz,
            segmentSeconds = opt.SegmentSeconds,
            detailed = opt.Detailed,
            catchCrash = opt.CatchCrash,
            speedMultiplier = opt.SpeedMultiplier,
            logFile = run.LogPath is not null ? Path.GetFullPath(run.LogPath) : null,
            dumpFile = run.DumpPath is not null ? Path.GetFullPath(run.DumpPath) : null,
            stateFile = run.StatePath is not null ? Path.GetFullPath(run.StatePath) : null,
            crashed = run.Crashed,
            exitCode = run.ExitCodeKnown ? $"0x{run.ExitCode:X8}" : null,
            report = reportText
        };

        if (isJson)
        {
            var envelope = new JsonEnvelope
            {
                Ok = true,
                Command = "profile",
                Data = data
            };
            stdout.WriteLine(JsonSerializer.Serialize(envelope, JsonEnvelopeOptions));
        }
        else
        {
            stdout.WriteLine("\n" + Strings.Get("Profile_Success"));
            stdout.WriteLine($"診斷輸出資料夾：{value.OutputDirectory}");
            stdout.WriteLine(value.RuntimeLayerActive
                ? $"遊戲內診斷層：已就位（{(value.InjectedBeforeEntryPoint ? "進入點之前" : "掛載方式")}），ckperf-*.log 同在上述資料夾。"
                : $"遊戲內診斷層：未就位（{value.RuntimeLayerNote ?? "-"}）。");
            if (run.LogPath is not null)
            {
                stdout.WriteLine(Strings.Get("Profile_ReportWritten", Path.GetFullPath(run.LogPath)));
            }
            if (run.DumpPath is not null)
            {
                stdout.WriteLine(Strings.Get("Profile_DumpWritten", Path.GetFullPath(run.DumpPath)));
            }
            if (run.StatePath is not null)
            {
                stdout.WriteLine(Strings.Get("Profile_StateWritten", Path.GetFullPath(run.StatePath)));
            }
            if (run.Crashed)
            {
                stdout.WriteLine(Strings.Get("Profile_Crashed", run.ExitCodeKnown ? $"0x{run.ExitCode:X8}" : "?"));
            }
            stdout.WriteLine("\n--- 分析報告摘要 (Profile Report) ---");
            stdout.WriteLine(reportText);
        }

        return ExitCodes.Success;
    }

    /// <summary>
    /// 帶診斷層啟動遊戲。
    ///
    /// 引擎自己呼叫 SetErrorMode 與 SetUnhandledExceptionFilter，崩潰永遠走不到 WER，
    /// 使用者只看到遊戲憑空消失。本指令注入 ckperf.dll，在引擎的任何處理常式之前
    /// 掛上向量化例外處理常式，把真正的錯誤位址攔下來寫成報告。
    ///
    /// 對遊戲檔案零寫入：只改被啟動行程的記憶體，磁碟上的執行檔一個位元組都不動。
    /// </summary>
    private static int HandleRun(
        List<string> options,
        string? gameOverride,
        string? configOverride,
        bool isJson,
        TextWriter stdout,
        TextWriter stderr)
    {
        var diag = new DiagnosticsOptions();
        bool plain = false;
        bool attach = false;
        bool watch = false;
        bool watchSecondsGiven = false;
        int watchSeconds = 300;

        for (int i = 0; i < options.Count; i++)
        {
            string token = options[i];
            int eq = token.IndexOf('=');
            string flag = (eq > 0 ? token[..eq] : token).ToLowerInvariant();
            string? val = eq > 0 ? token[(eq + 1)..] : null;

            string? TakeValue()
            {
                if (val is not null) return val;
                if (i + 1 < options.Count) return options[++i];
                return null;
            }

            switch (flag)
            {
                case "--plain":        plain = true; break;
                case "--attach":       attach = true; break;
                case "--watch":        watch = true; break;

                case "--log-dir":
                {
                    string? v = TakeValue();
                    if (string.IsNullOrWhiteSpace(v))
                        return OutputError("run", Strings.Get("Error_InvalidArgs", "--log-dir 必須指定儲存位置"),
                                           ExitCodes.InvalidArgs, isJson, stdout, stderr);
                    diag.OutputDirectory = v;
                    break;
                }

                case "--watch-seconds":
                {
                    string? v = TakeValue();
                    if (!int.TryParse(v, out int n) || n < 5 || n > 3600)
                        return OutputError("run", Strings.Get("Error_InvalidArgs", $"--watch-seconds 需為 5..3600 的整數，收到 '{v}'"),
                                           ExitCodes.InvalidArgs, isJson, stdout, stderr);
                    watchSeconds = n;
                    watchSecondsGiven = true;
                    watch = true;
                    break;
                }

                case "--no-crash":     diag.CrashReports = false; break;
                case "--no-dump":      diag.MiniDumps = false; break;
                case "--no-telemetry": diag.Telemetry = false; break;
                case "--no-frames":    diag.FrameTiming = false; break;

                case "--maxreports":
                {
                    string? v = TakeValue();
                    if (!int.TryParse(v, out int n) || n < 1 || n > 1000)
                        return OutputError("run", Strings.Get("Error_InvalidArgs", $"--maxreports 需為 1..1000 的整數，收到 '{v}'"),
                                           ExitCodes.InvalidArgs, isJson, stdout, stderr);
                    diag.MaxReports = n;
                    break;
                }

                case "--telemetry-ms":
                {
                    string? v = TakeValue();
                    if (!int.TryParse(v, out int n) || n < 100 || n > 60000)
                        return OutputError("run", Strings.Get("Error_InvalidArgs", $"--telemetry-ms 需為 100..60000 的整數，收到 '{v}'"),
                                           ExitCodes.InvalidArgs, isJson, stdout, stderr);
                    diag.TelemetryMs = n;
                    break;
                }

                default:
                    return OutputError("run", Strings.Get("Error_InvalidArgs", $"未知的選項 '{token}'"),
                                       ExitCodes.InvalidArgs, isJson, stdout, stderr);
            }
        }

        int modes = (plain ? 1 : 0) + (attach ? 1 : 0) + (watch ? 1 : 0);
        if (modes > 1)
            return OutputError("run", Strings.Get("Error_InvalidArgs", "--plain / --attach / --watch 三者互斥，一次只能指定一個"),
                               ExitCodes.InvalidArgs, isJson, stdout, stderr);

        var config = ToolkitConfig.Load(configOverride);
        string? gameDir = GamePaths.FindGameDir(gameOverride, config.GameDir);
        if (gameDir is null || !GamePaths.IsGameDir(gameDir))
            return OutputError("run", Strings.Get("Error_GameNotFound"), ExitCodes.GameNotFound, isJson, stdout, stderr);

        if (plain)
        {
            // 無插樁對照組。要判斷診斷層本身有沒有影響效能，就需要這個。
            try
            {
                // UseShellExecute = true 是刻意的：false 會讓遊戲繼承本行程的
                // 標準輸出控制代碼，呼叫端的管線就要等到遊戲結束才收得到 EOF，
                // 表現出來就是「指令卡住不返回」。
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = Path.Combine(gameDir, GamePaths.ExeFileName),
                    WorkingDirectory = gameDir,
                    UseShellExecute = true
                };
                using var proc = System.Diagnostics.Process.Start(psi);
                int plainPid = proc?.Id ?? 0;
                if (isJson)
                {
                    stdout.WriteLine(JsonSerializer.Serialize(new JsonEnvelope
                    {
                        Ok = true,
                        Command = "run",
                        Data = new { pid = plainPid, instrumented = false, gameDir }
                    }, JsonEnvelopeOptions));
                }
                else
                {
                    stdout.WriteLine($"已啟動遊戲（未插樁），pid {plainPid}。");
                }
                return ExitCodes.Success;
            }
            catch (Exception ex)
            {
                return OutputError("run", $"啟動遊戲失敗：{ex.Message}", ExitCodes.GeneralFailure, isJson, stdout, stderr);
            }
        }

        // 配置清單先寫，啟動後寫。萬一啟動失敗，至少留下當時的狀態可查。
        // 配置清單必須跟它要解釋的 ckcrash-*.txt 放在同一個診斷輸出資料夾。
        string manifestPath;
        var manifestWarnings = new List<string>();
        string selectedDiagLocation = GameRunner.ResolveOutputDirectory(diag);
        string sessionMode = attach ? "attach" : watch ? (watchSecondsGiven ? "wait" : "watch") : "launch";
        string diagOutDir;
        try
        {
            diagOutDir = DiagnosticOutputLayout.CreateSessionDirectory(
                selectedDiagLocation, sessionMode, DateTime.Now);
            diag.OutputDirectory = diagOutDir;
            manifestPath = RunManifest.Write(diagOutDir, gameDir, config, diag);
        }
        catch (Exception ex)
        {
            return OutputError("run", $"診斷輸出資料夾建立失敗（{selectedDiagLocation}）：{ex.Message}",
                               ExitCodes.GeneralFailure, isJson, stdout, stderr);
        }

        var progress = new List<string>();
        void Report(string m)
        {
            progress.Add(m);
            // 等待模式可能一等好幾分鐘，逐句印出來才知道它還活著。
            if (!isJson && watch) stdout.WriteLine($"  {m}");
        }

        // 沒有指定等待秒數的 --watch 是常駐監看，跑到使用者按 Ctrl+C 為止。
        // 這是預設行為而不是額外選項，因為「玩到閃退卻沒有資料」已經發生兩次，
        // 兩次都是遊戲從 Steam 啟動、繞過了唯一的注入點。
        if (watch && !watchSecondsGiven)
        {
            using var cts = new CancellationTokenSource();
            ConsoleCancelEventHandler onCancel = (_, e) => { e.Cancel = true; cts.Cancel(); };
            Console.CancelKeyPress += onCancel;
            try
            {
                GameRunner.WatchForever(diag, cts.Token, m =>
                {
                    if (isJson)
                    {
                        stdout.WriteLine(JsonSerializer.Serialize(new JsonEnvelope
                        {
                            Ok = true,
                            Command = "run",
                            Data = new
                            {
                                eventType = "progress",
                                message = m,
                                gameDir,
                                outputDirectory = diagOutDir,
                                configManifest = manifestPath
                            }
                        }, JsonLineOptions));
                    }
                    else
                    {
                        stdout.WriteLine($"  {m}");
                    }
                });
            }
            finally
            {
                Console.CancelKeyPress -= onCancel;
            }

            if (isJson)
            {
                stdout.WriteLine(JsonSerializer.Serialize(new JsonEnvelope
                {
                    Ok = true,
                    Command = "run",
                    Data = new
                    {
                        eventType = "completed",
                        cancelled = cts.IsCancellationRequested,
                        gameDir,
                        outputDirectory = diagOutDir,
                        configManifest = manifestPath
                    }
                }, JsonLineOptions));
            }
            return ExitCodes.Success;
        }

        Result<RunOutcome> result =
            attach ? GameRunner.AttachToRunningGame(diag, Report)
          : watch  ? GameRunner.WaitForGameAndAttach(diag, TimeSpan.FromSeconds(watchSeconds), default, Report)
                   : GameRunner.LaunchWithDiagnostics(gameDir, diag, Report);

        if (result.IsError)
            return OutputError("run", result.ErrorMessage!, result.ExitCode, isJson, stdout, stderr,
                               [.. manifestWarnings, .. result.Warnings]);

        RunOutcome outcome = result.Value!;
        string logPattern = Path.Combine(outcome.OutputDirectory, "ckperf-*.log");
        var allWarnings = new List<string>(manifestWarnings);
        allWarnings.AddRange(result.Warnings);

        if (isJson)
        {
            stdout.WriteLine(JsonSerializer.Serialize(new JsonEnvelope
            {
                Ok = true,
                Command = "run",
                Warnings = allWarnings,
                Data = new
                {
                    pid = outcome.ProcessId,
                    instrumented = true,
                    injectedBeforeEntryPoint = outcome.InjectedBeforeEntryPoint,
                    gameDir,
                    outputDirectory = outcome.OutputDirectory,
                    logFilePattern = logPattern,
                    configManifest = manifestPath,
                    crashReportGlob = Path.Combine(outcome.OutputDirectory, "ckcrash-*.txt"),
                    detail = outcome.Detail,
                    steps = progress
                }
            }, JsonEnvelopeOptions));
        }
        else
        {
            if (!watch) foreach (string s in progress) stdout.WriteLine($"  {s}");
            stdout.WriteLine(attach || watch
                ? $"已掛載到遊戲行程 pid {outcome.ProcessId}。{outcome.Detail}"
                : $"遊戲已啟動，pid {outcome.ProcessId}。{outcome.Detail}");
            stdout.WriteLine($"診斷輸出：{outcome.OutputDirectory}");
            stdout.WriteLine($"  執行記錄  ckperf-*.log");
            stdout.WriteLine($"  配置清單  {RunManifest.FileName}");
            stdout.WriteLine($"  故障報告  ckcrash-*.txt（只有在真的發生例外時才會出現）");
            foreach (string w in allWarnings) stdout.WriteLine($"  ! {w}");
        }
        return ExitCodes.Success;
    }
}
