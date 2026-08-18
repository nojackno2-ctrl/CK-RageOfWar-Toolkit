using System.Runtime.InteropServices;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using CKToolkit.Core.Common;
using CKToolkit.Core.Perf;
using CKToolkit.I18n;

namespace CKToolkit.Cli;

/// <summary>
/// CLI 回傳之 JSON 封套結構 (SPEC.md §10)。
/// </summary>
public sealed class JsonEnvelope
{
    [JsonPropertyName("ok")]
    public bool Ok { get; set; }

    [JsonPropertyName("command")]
    public string Command { get; set; } = string.Empty;

    [JsonPropertyName("data")]
    public object? Data { get; set; }

    [JsonPropertyName("warnings")]
    public List<string> Warnings { get; set; } = [];

    [JsonPropertyName("errors")]
    public List<string> Errors { get; set; } = [];
}

/// <summary>
/// CLI 命令列處理主機 (SPEC.md §10)。
/// 供 AI 代理程式非互動式驅動，支援結構化 JSON 輸出封套與標準結束代碼。
/// 確保輸出永遠為無 BOM 之 UTF-8 編碼。
/// </summary>
public static partial class CliHost
{
    private const int AttachParentProcess = -1;

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AttachConsole(int dwProcessId);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AllocConsole();

    private static readonly JsonSerializerOptions JsonEnvelopeOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// CLI 主進入點。設定無 BOM UTF-8 編碼並執行指令。
    /// </summary>
    public static int Run(string[] args)
    {
        var utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        Console.OutputEncoding = utf8NoBom;
        Console.InputEncoding = utf8NoBom;

        EnsureConsole();

        Console.OutputEncoding = utf8NoBom;
        Console.InputEncoding = utf8NoBom;

        using var stdout = new StreamWriter(Console.OpenStandardOutput(), utf8NoBom) { AutoFlush = true };
        using var stderr = new StreamWriter(Console.OpenStandardError(), utf8NoBom) { AutoFlush = true };
        Console.SetOut(stdout);
        Console.SetError(stderr);

        return Execute(args, stdout, stderr);
    }

    private static void EnsureConsole()
    {
        if (!AttachConsole(AttachParentProcess))
        {
            AllocConsole();
        }
    }

    /// <summary>
    /// 執行 CLI 指令並輸出至指定 TextWriter（便於單元測試與自我測試）。
    /// </summary>
    public static int Execute(string[] args, TextWriter stdout, TextWriter stderr)
    {
        bool isJson = false;
        string? gameDirOverride = null;
        string? configPathOverride = null;
        var commands = new List<string>();

        // 解析全域旗標與指令 tokens
        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            if (arg.Equals("--json", StringComparison.OrdinalIgnoreCase))
            {
                isJson = true;
            }
            else if (arg.Equals("--game", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                gameDirOverride = args[++i];
            }
            else if (arg.Equals("--config", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                configPathOverride = args[++i];
            }
            else
            {
                commands.Add(arg);
            }
        }

        string primaryCmd = commands.Count > 0 ? commands[0].ToLowerInvariant() : "help";

        switch (primaryCmd)
        {
            case "--help" or "-h" or "help":
                return HandleHelp(isJson, stdout);

            case "--version" or "-v" or "version":
                return HandleVersion(isJson, stdout);

            case "status":
                return HandleStatus(gameDirOverride, configPathOverride, isJson, stdout, stderr);

            case "apply":
                return HandleApply(gameDirOverride, configPathOverride, isJson, stdout, stderr);

            case "restore":
                return HandleRestore(commands.Skip(1).ToList(), gameDirOverride, configPathOverride, isJson, stdout, stderr);

            case "verify":
                return HandleVerify(gameDirOverride, configPathOverride, isJson, stdout, stderr);

            case "perf":
                if (commands.Count < 2)
                {
                    string err = Strings.Get("Error_PerfSubcommandRequired");
                    return OutputError("perf", err, ExitCodes.InvalidArgs, isJson, stdout, stderr);
                }
                string subCmd = commands[1].ToLowerInvariant();
                if (subCmd == "get")
                {
                    return HandlePerfGet(gameDirOverride, configPathOverride, isJson, stdout, stderr);
                }
                if (subCmd == "set")
                {
                    return HandlePerfSet(commands.Skip(2).ToList(), gameDirOverride, configPathOverride, isJson, stdout, stderr);
                }
                return OutputError("perf", Strings.Get("Error_InvalidArgs", $"未知的 perf 子指令 '{commands[1]}'"), ExitCodes.InvalidArgs, isJson, stdout, stderr);

            default:
                return HandleUnknown(primaryCmd, isJson, stdout, stderr);
        }
    }

    private static int HandleHelp(bool isJson, TextWriter stdout)
    {
        string helpText = Strings.Get("Cli_HelpText");
        if (isJson)
        {
            var envelope = new JsonEnvelope
            {
                Ok = true,
                Command = "help",
                Data = new { help = helpText }
            };
            stdout.WriteLine(JsonSerializer.Serialize(envelope, JsonEnvelopeOptions));
        }
        else
        {
            stdout.WriteLine(helpText);
        }
        return ExitCodes.Success;
    }

    private static int HandleVersion(bool isJson, TextWriter stdout)
    {
        string versionStr = "1.0.0";
        if (isJson)
        {
            var envelope = new JsonEnvelope
            {
                Ok = true,
                Command = "version",
                Data = new { version = versionStr, toolkit = "CKToolkit" }
            };
            stdout.WriteLine(JsonSerializer.Serialize(envelope, JsonEnvelopeOptions));
        }
        else
        {
            stdout.WriteLine(Strings.Get("Cli_Version", versionStr));
        }
        return ExitCodes.Success;
    }

    /// <summary>
    /// 處理 status 狀態查詢。此路徑為嚴格唯讀，絕不建立備份目錄、絕不抓取檔案、絕不寫入設定。
    /// </summary>
    private static int HandleStatus(string? gameOverride, string? configOverride, bool isJson, TextWriter stdout, TextWriter stderr)
    {
        var config = ToolkitConfig.Load(configOverride);
        string? gameDir = GamePaths.FindGameDir(gameOverride, config.GameDir);

        if (gameDir is null || !GamePaths.IsGameDir(gameDir))
        {
            string err = Strings.Get("Error_GameNotFound");
            return OutputError("status", err, ExitCodes.GameNotFound, isJson, stdout, stderr);
        }

        // 純唯讀查詢：使用 BackupManager 唯讀 API，絕不呼叫 ReadPristine 或 EnsureBackup
        var backupMgr = new BackupManager();
        PerfModule.RegisterSignatures(backupMgr);
        var filesStatus = new Dictionary<string, object>();
        var warnings = new List<string>(config.MigrationsApplied);

        bool anyCoverageIncomplete = false;

        foreach (GameFile f in Enum.GetValues<GameFile>())
        {
            string fileName = BackupManager.GetFileName(f);
            bool hasBackup = backupMgr.HasBackup(f);
            var state = backupMgr.GetFilePristineState(gameDir, f);
            var provenance = backupMgr.GetBackupProvenance(f);

            if (!backupMgr.IsCoverageComplete(f))
            {
                anyCoverageIncomplete = true;
            }

            if (provenance is not null && !provenance.CoverageComplete)
            {
                warnings.Add(Strings.Get("Warning_BaselineCapturedIncompleteCoverage", fileName));
            }

            string stateString = state switch
            {
                PristineState.Pristine => "pristine",
                PristineState.Patched => "patched",
                _ => "unknown"
            };

            string statusDisplay = state switch
            {
                PristineState.Pristine => Strings.Get("Status_Pristine"),
                PristineState.Patched => Strings.Get("Status_Patched"),
                _ => Strings.Get("Status_Unknown")
            };

            bool? isPristine = state switch
            {
                PristineState.Pristine => true,
                PristineState.Patched => false,
                _ => null
            };

            filesStatus[fileName] = new
            {
                hasBackup,
                backupProvenance = provenance is not null ? new
                {
                    capturedAt = provenance.CapturedAtUtc,
                    coverageComplete = provenance.CoverageComplete,
                    registeredSignatures = provenance.RegisteredSignatures,
                    missingSignatures = provenance.MissingSignatures
                } : null,
                pristineState = stateString,
                isPristine,
                status = statusDisplay
            };
        }

        if (anyCoverageIncomplete)
        {
            warnings.Add(Strings.Get("Warning_DetectionIncomplete"));
        }

        var data = new
        {
            gameDir,
            gameRunning = GamePaths.IsGameRunning(),
            configVersion = config.Version,
            uiLanguage = config.UiLanguage,
            files = filesStatus
        };

        if (isJson)
        {
            var envelope = new JsonEnvelope
            {
                Ok = true,
                Command = "status",
                Data = data,
                Warnings = warnings
            };
            stdout.WriteLine(JsonSerializer.Serialize(envelope, JsonEnvelopeOptions));
        }
        else
        {
            stdout.WriteLine(Strings.Get("Status_GameDir", gameDir));
            foreach (var (fn, info) in filesStatus)
            {
                stdout.WriteLine($"  - {fn}: {JsonSerializer.Serialize(info, JsonEnvelopeOptions)}");
            }
            if (warnings.Count > 0)
            {
                stdout.WriteLine("\n警告 / Warnings:");
                foreach (string w in warnings)
                {
                    stdout.WriteLine($"  ! {w}");
                }
            }
        }

        return ExitCodes.Success;
    }

    /// <summary>
    /// 處理 apply 修補套用指令。依序疊加所有已啟用修改並寫入遊戲檔案。
    /// </summary>
    private static int HandleApply(string? gameOverride, string? configOverride, bool isJson, TextWriter stdout, TextWriter stderr)
    {
        var config = ToolkitConfig.Load(configOverride);
        string? gameDir = GamePaths.FindGameDir(gameOverride, config.GameDir);

        if (gameDir is null || !GamePaths.IsGameDir(gameDir))
        {
            string err = Strings.Get("Error_GameNotFound");
            return OutputError("apply", err, ExitCodes.GameNotFound, isJson, stdout, stderr);
        }

        if (GamePaths.IsGameRunning())
        {
            string err = Strings.Get("Error_GameRunning");
            return OutputError("apply", err, ExitCodes.FileLocked, isJson, stdout, stderr);
        }

        var pipeline = PatchPipeline.CreateDefault();
        var result = pipeline.ApplyAll(gameDir, config);

        var warnings = new List<string>(config.MigrationsApplied);
        warnings.AddRange(result.Warnings);

        if (!result.Success)
        {
            if (isJson)
            {
                var envelope = new JsonEnvelope
                {
                    Ok = false,
                    Command = "apply",
                    Data = result.Value is not null ? new
                    {
                        gameDir,
                        filesWritten = result.Value.FilesWritten,
                        files = result.Value.Files
                    } : null,
                    Warnings = warnings,
                    Errors = [result.ErrorMessage ?? Strings.Get("Error_GeneralFailure", "套用失敗")]
                };
                stdout.WriteLine(JsonSerializer.Serialize(envelope, JsonEnvelopeOptions));
            }
            else
            {
                stderr.WriteLine(result.ErrorMessage);
                if (warnings.Count > 0)
                {
                    stderr.WriteLine("\n警告 / Warnings:");
                    foreach (string w in warnings) stderr.WriteLine($"  ! {w}");
                }
            }
            return result.ExitCode;
        }

        var report = result.Value!;
        if (isJson)
        {
            var envelope = new JsonEnvelope
            {
                Ok = true,
                Command = "apply",
                Data = new
                {
                    gameDir,
                    filesWritten = report.FilesWritten,
                    files = report.Files
                },
                Warnings = warnings
            };
            stdout.WriteLine(JsonSerializer.Serialize(envelope, JsonEnvelopeOptions));
        }
        else
        {
            stdout.WriteLine(Strings.Get("Apply_Success"));
            stdout.WriteLine(Strings.Get("Status_GameDir", gameDir));
            foreach (var (fn, fileRes) in report.Files)
            {
                string layeredStr = fileRes.Layered.Count > 0 ? string.Join(", ", fileRes.Layered) : "pristine";
                stdout.WriteLine($"  - {fn}: [written: {fileRes.Written}] (layered: {layeredStr})");
            }
            if (warnings.Count > 0)
            {
                stdout.WriteLine("\n警告 / Warnings:");
                foreach (string w in warnings) stdout.WriteLine($"  ! {w}");
            }
        }

        return ExitCodes.Success;
    }

    /// <summary>
    /// 處理 restore --all 還原指令。從備份還原所有目標檔案並驗證逐位元組一致性。
    /// </summary>
    private static int HandleRestore(List<string> commandArgs, string? gameOverride, string? configOverride, bool isJson, TextWriter stdout, TextWriter stderr)
    {
        bool hasAll = commandArgs.Contains("--all", StringComparer.OrdinalIgnoreCase);
        if (!hasAll)
        {
            string err = Strings.Get("Error_RestoreAllMissingFlag");
            return OutputError("restore", err, ExitCodes.InvalidArgs, isJson, stdout, stderr);
        }

        var config = ToolkitConfig.Load(configOverride);
        string? gameDir = GamePaths.FindGameDir(gameOverride, config.GameDir);

        if (gameDir is null || !GamePaths.IsGameDir(gameDir))
        {
            string err = Strings.Get("Error_GameNotFound");
            return OutputError("restore", err, ExitCodes.GameNotFound, isJson, stdout, stderr);
        }

        if (GamePaths.IsGameRunning())
        {
            string err = Strings.Get("Error_GameRunning");
            return OutputError("restore", err, ExitCodes.FileLocked, isJson, stdout, stderr);
        }

        var pipeline = PatchPipeline.CreateDefault();
        var result = pipeline.RestoreAll(gameDir);

        var warnings = new List<string>(config.MigrationsApplied);
        warnings.AddRange(result.Warnings);

        if (!result.Success)
        {
            if (isJson)
            {
                var envelope = new JsonEnvelope
                {
                    Ok = false,
                    Command = "restore",
                    Warnings = warnings,
                    Errors = [result.ErrorMessage ?? Strings.Get("Error_NoBackupsToRestore")]
                };
                stdout.WriteLine(JsonSerializer.Serialize(envelope, JsonEnvelopeOptions));
            }
            else
            {
                stderr.WriteLine(result.ErrorMessage);
                if (warnings.Count > 0)
                {
                    stderr.WriteLine("\n警告 / Warnings:");
                    foreach (string w in warnings) stderr.WriteLine($"  ! {w}");
                }
            }
            return result.ExitCode;
        }

        var report = result.Value!;
        if (isJson)
        {
            var envelope = new JsonEnvelope
            {
                Ok = true,
                Command = "restore",
                Data = new
                {
                    gameDir,
                    restoredFiles = report.RestoredFiles,
                    files = report.Files
                },
                Warnings = warnings
            };
            stdout.WriteLine(JsonSerializer.Serialize(envelope, JsonEnvelopeOptions));
        }
        else
        {
            stdout.WriteLine(Strings.Get("Restore_Success"));
            stdout.WriteLine(Strings.Get("Status_GameDir", gameDir));
            foreach (var (fn, fInfo) in report.Files)
            {
                stdout.WriteLine($"  - {fn}: restored={fInfo.Restored}, verified={fInfo.ByteEqualityVerified} ({fInfo.Status})");
            }
            if (warnings.Count > 0)
            {
                stdout.WriteLine("\n警告 / Warnings:");
                foreach (string w in warnings) stdout.WriteLine($"  ! {w}");
            }
        }

        return ExitCodes.Success;
    }

    /// <summary>
    /// 處理 verify 驗證指令。檢查備份完整性、歷程與 live 檔案是否與設定相符（嚴格唯讀）。
    /// </summary>
    private static int HandleVerify(string? gameOverride, string? configOverride, bool isJson, TextWriter stdout, TextWriter stderr)
    {
        var config = ToolkitConfig.Load(configOverride);
        string? gameDir = GamePaths.FindGameDir(gameOverride, config.GameDir);

        if (gameDir is null || !GamePaths.IsGameDir(gameDir))
        {
            string err = Strings.Get("Error_GameNotFound");
            return OutputError("verify", err, ExitCodes.GameNotFound, isJson, stdout, stderr);
        }

        var pipeline = PatchPipeline.CreateDefault();
        var result = pipeline.Verify(gameDir, config);

        var warnings = new List<string>(config.MigrationsApplied);
        warnings.AddRange(result.Warnings);

        var report = result.Value!;
        var filesData = new Dictionary<string, object>();
        foreach (var (fn, fi) in report.Files)
        {
            filesData[fn] = new
            {
                hasBackup = fi.HasBackup,
                backupProvenance = fi.BackupProvenance is not null ? new
                {
                    capturedAt = fi.BackupProvenance.CapturedAtUtc,
                    coverageComplete = fi.BackupProvenance.CoverageComplete,
                    registeredSignatures = fi.BackupProvenance.RegisteredSignatures,
                    missingSignatures = fi.BackupProvenance.MissingSignatures
                } : null,
                pristineState = fi.PristineState switch
                {
                    PristineState.Pristine => "pristine",
                    PristineState.Patched => "patched",
                    _ => "unknown"
                },
                isPristine = fi.IsPristine,
                appliedPatches = fi.AppliedPatches,
                expectedPatches = fi.ExpectedPatches,
                matchesConfig = fi.MatchesConfig
            };
        }

        var data = new
        {
            gameDir,
            allBackupsPresent = report.AllBackupsPresent,
            allMatchesConfig = report.AllMatchesConfig,
            files = filesData
        };

        if (isJson)
        {
            var envelope = new JsonEnvelope
            {
                Ok = true,
                Command = "verify",
                Data = data,
                Warnings = warnings
            };
            stdout.WriteLine(JsonSerializer.Serialize(envelope, JsonEnvelopeOptions));
        }
        else
        {
            stdout.WriteLine(report.AllMatchesConfig && report.AllBackupsPresent
                ? Strings.Get("Verify_AllOk")
                : Strings.Get("Verify_Mismatch"));
            stdout.WriteLine(Strings.Get("Status_GameDir", gameDir));
            foreach (var (fn, fi) in report.Files)
            {
                stdout.WriteLine($"  - {fn}: hasBackup={fi.HasBackup}, state={fi.PristineState}, matchesConfig={fi.MatchesConfig} (applied: [{string.Join(", ", fi.AppliedPatches)}], expected: [{string.Join(", ", fi.ExpectedPatches)}])");
            }
            if (warnings.Count > 0)
            {
                stdout.WriteLine("\n警告 / Warnings:");
                foreach (string w in warnings) stdout.WriteLine($"  ! {w}");
            }
        }

        return ExitCodes.Success;
    }

    /// <summary>
    /// 處理 perf get 指令。讀取當前有效之效能修補設定。
    /// </summary>
    private static int HandlePerfGet(string? gameOverride, string? configOverride, bool isJson, TextWriter stdout, TextWriter stderr)
    {
        var config = ToolkitConfig.Load(configOverride);
        if (!string.IsNullOrWhiteSpace(gameOverride))
        {
            config.GameDir = gameOverride;
        }

        var perfData = new
        {
            laa = config.Perf.Laa,
            videoFix = config.Perf.VideoFix,
            keepRes = config.Perf.KeepRes,
            hires = config.Perf.Hires,
            resolution = config.Perf.Resolution,
            addRes = config.Perf.AddRes,
            desktopMode = config.Perf.DesktopMode,
            noObjectAnimations = config.Perf.NoObjectAnimations,
            noWaterAnimation = config.Perf.NoWaterAnimation
        };

        var warnings = new List<string>(config.MigrationsApplied);

        if (isJson)
        {
            var envelope = new JsonEnvelope
            {
                Ok = true,
                Command = "perf get",
                Data = perfData,
                Warnings = warnings
            };
            stdout.WriteLine(JsonSerializer.Serialize(envelope, JsonEnvelopeOptions));
        }
        else
        {
            stdout.WriteLine("效能修補設定 (Performance Settings):");
            stdout.WriteLine($"  - LargeAddressAware (LAA): {(config.Perf.Laa ? "on" : "off")}");
            stdout.WriteLine($"  - VideoMode Fix (16bpp): {(config.Perf.VideoFix ? "on" : "off")}");
            stdout.WriteLine($"  - HiRes Zoom: {(config.Perf.Hires > 0 ? $"{config.Perf.Hires}" : "off")}");
            stdout.WriteLine($"  - Keep Resolution (res_writeback): {(config.Perf.KeepRes ? "on" : "off")}");
            stdout.WriteLine($"  - Desktop Mode: {config.Perf.DesktopMode}");
            stdout.WriteLine($"  - Resolution: {config.Perf.Resolution}");
            stdout.WriteLine($"  - Object Animations: {(!config.Perf.NoObjectAnimations ? "on" : "off")}");
            stdout.WriteLine($"  - Water Animation: {(!config.Perf.NoWaterAnimation ? "on" : "off")}");
            if (warnings.Count > 0)
            {
                stdout.WriteLine("\n警告 / Warnings:");
                foreach (string w in warnings) stdout.WriteLine($"  ! {w}");
            }
        }

        return ExitCodes.Success;
    }

    /// <summary>
    /// 處理 perf set 指令。修改設定檔中之效能設定（嚴格不碰遊戲檔案，套用需執行 apply）。
    /// </summary>
    private static int HandlePerfSet(List<string> options, string? gameOverride, string? configOverride, bool isJson, TextWriter stdout, TextWriter stderr)
    {
        if (options.Count == 0)
        {
            string err = Strings.Get("Error_InvalidArgs", "perf set 必須提供至少一個設定選項");
            return OutputError("perf set", err, ExitCodes.InvalidArgs, isJson, stdout, stderr);
        }

        var config = ToolkitConfig.Load(configOverride);
        if (!string.IsNullOrWhiteSpace(gameOverride))
        {
            config.GameDir = gameOverride;
        }

        var warnings = new List<string>(config.MigrationsApplied);

        for (int i = 0; i < options.Count; i++)
        {
            string opt = options[i];
            if (opt.StartsWith("--") && i + 1 < options.Count)
            {
                string flag = opt.ToLowerInvariant();
                string val = options[++i];

                switch (flag)
                {
                    case "--laa":
                        if (TryParseOnOff(val, out bool laa))
                        {
                            config.Perf.Laa = laa;
                        }
                        else
                        {
                            return OutputError("perf set", Strings.Get("Error_InvalidArgs", $"--laa 必須為 on 或 off，實際為 '{val}'"), ExitCodes.InvalidArgs, isJson, stdout, stderr);
                        }
                        break;

                    case "--videofix":
                        if (TryParseOnOff(val, out bool vfix))
                        {
                            config.Perf.VideoFix = vfix;
                        }
                        else
                        {
                            return OutputError("perf set", Strings.Get("Error_InvalidArgs", $"--videofix 必須為 on 或 off，實際為 '{val}'"), ExitCodes.InvalidArgs, isJson, stdout, stderr);
                        }
                        break;

                    case "--hires":
                        if (val.Equals("off", StringComparison.OrdinalIgnoreCase) || val == "0" || val.Equals("none", StringComparison.OrdinalIgnoreCase))
                        {
                            config.Perf.Hires = 0;
                        }
                        else
                        {
                            int w;
                            if (val.Contains('x', StringComparison.OrdinalIgnoreCase))
                            {
                                var parts = val.Split(['x', 'X'], StringSplitOptions.RemoveEmptyEntries);
                                if (parts.Length != 2 || !int.TryParse(parts[0], out w) || !int.TryParse(parts[1], out int h) || w <= 0 || h <= 0)
                                {
                                    return OutputError("perf set", Strings.Get("Error_InvalidArgs", $"--hires 格式必須為 <W>x<H> 或 off，實際為 '{val}'"), ExitCodes.InvalidArgs, isJson, stdout, stderr);
                                }
                            }
                            else if (!int.TryParse(val, out w) || w <= 0)
                            {
                                return OutputError("perf set", Strings.Get("Error_InvalidArgs", $"--hires 格式必須為 <W>x<H> 或 off，實際為 '{val}'"), ExitCodes.InvalidArgs, isJson, stdout, stderr);
                            }

                            if (w < 1600 || w > 16384)
                            {
                                return OutputError("perf set", Strings.Get("Error_InvalidTableDimensions"), ExitCodes.InvalidArgs, isJson, stdout, stderr);
                            }

                            config.Perf.Hires = w;
                            if (w >= 2048)
                            {
                                warnings.Add(Strings.Get("Perf_HdCeilingWarning"));
                            }
                        }
                        break;

                    case "--keepres":
                        if (TryParseOnOff(val, out bool keepRes))
                        {
                            config.Perf.KeepRes = keepRes;
                        }
                        else
                        {
                            return OutputError("perf set", Strings.Get("Error_InvalidArgs", $"--keepres 必須為 on 或 off，實際為 '{val}'"), ExitCodes.InvalidArgs, isJson, stdout, stderr);
                        }
                        break;

                    case "--desktop":
                        if (val.Equals("suppress", StringComparison.OrdinalIgnoreCase))
                        {
                            config.Perf.DesktopMode = "suppress";
                        }
                        else if (val.Equals("autoswitch", StringComparison.OrdinalIgnoreCase))
                        {
                            config.Perf.DesktopMode = "autoSwitch";
                        }
                        else
                        {
                            return OutputError("perf set", Strings.Get("Error_InvalidArgs", $"--desktop 必須為 suppress 或 autoswitch，實際為 '{val}'"), ExitCodes.InvalidArgs, isJson, stdout, stderr);
                        }
                        break;

                    case "--resolution":
                        var resParts = val.Split(['x', 'X'], StringSplitOptions.RemoveEmptyEntries);
                        if (resParts.Length != 2 || !int.TryParse(resParts[0], out int rw) || !int.TryParse(resParts[1], out int rh) || rw <= 0 || rh <= 0)
                        {
                            return OutputError("perf set", Strings.Get("Error_InvalidArgs", $"--resolution 格式必須為 <寬>x<高>，實際為 '{val}'"), ExitCodes.InvalidArgs, isJson, stdout, stderr);
                        }

                        string normRes = $"{rw}x{rh}";
                        config.Perf.Resolution = normRes;
                        if (!config.Perf.AddRes.Contains(normRes, StringComparer.OrdinalIgnoreCase))
                        {
                            config.Perf.AddRes.Add(normRes);
                        }
                        if (rw >= 2048 || rh >= 1152)
                        {
                            warnings.Add(Strings.Get("Perf_HdCeilingWarning"));
                        }
                        break;

                    case "--anim-objects":
                        if (TryParseOnOff(val, out bool animObj))
                        {
                            config.Perf.NoObjectAnimations = !animObj;
                        }
                        else
                        {
                            return OutputError("perf set", Strings.Get("Error_InvalidArgs", $"--anim-objects 必須為 on 或 off，實際為 '{val}'"), ExitCodes.InvalidArgs, isJson, stdout, stderr);
                        }
                        break;

                    case "--anim-water":
                        if (TryParseOnOff(val, out bool animWater))
                        {
                            config.Perf.NoWaterAnimation = !animWater;
                        }
                        else
                        {
                            return OutputError("perf set", Strings.Get("Error_InvalidArgs", $"--anim-water 必須為 on 或 off，實際為 '{val}'"), ExitCodes.InvalidArgs, isJson, stdout, stderr);
                        }
                        break;

                    default:
                        return OutputError("perf set", Strings.Get("Error_InvalidArgs", $"未知的設定選項 '{opt}'"), ExitCodes.InvalidArgs, isJson, stdout, stderr);
                }
            }
            else
            {
                return OutputError("perf set", Strings.Get("Error_InvalidArgs", $"缺少選項值或無效的語法 '{opt}'"), ExitCodes.InvalidArgs, isJson, stdout, stderr);
            }
        }

        // 儲存設定檔（純設定檔寫入，絕對不碰遊戲檔案）
        config.Save(configOverride);

        var perfData = new
        {
            laa = config.Perf.Laa,
            videoFix = config.Perf.VideoFix,
            keepRes = config.Perf.KeepRes,
            hires = config.Perf.Hires,
            resolution = config.Perf.Resolution,
            addRes = config.Perf.AddRes,
            desktopMode = config.Perf.DesktopMode,
            noObjectAnimations = config.Perf.NoObjectAnimations,
            noWaterAnimation = config.Perf.NoWaterAnimation
        };

        if (isJson)
        {
            var envelope = new JsonEnvelope
            {
                Ok = true,
                Command = "perf set",
                Data = perfData,
                Warnings = warnings
            };
            stdout.WriteLine(JsonSerializer.Serialize(envelope, JsonEnvelopeOptions));
        }
        else
        {
            stdout.WriteLine(Strings.Get("Perf_Set_Success"));
            stdout.WriteLine("更新後的效能修補設定 (Updated Performance Settings):");
            stdout.WriteLine($"  - LargeAddressAware (LAA): {(config.Perf.Laa ? "on" : "off")}");
            stdout.WriteLine($"  - VideoMode Fix (16bpp): {(config.Perf.VideoFix ? "on" : "off")}");
            stdout.WriteLine($"  - HiRes Zoom: {(config.Perf.Hires > 0 ? $"{config.Perf.Hires}" : "off")}");
            stdout.WriteLine($"  - Keep Resolution (res_writeback): {(config.Perf.KeepRes ? "on" : "off")}");
            stdout.WriteLine($"  - Desktop Mode: {config.Perf.DesktopMode}");
            stdout.WriteLine($"  - Resolution: {config.Perf.Resolution}");
            stdout.WriteLine($"  - Object Animations: {(!config.Perf.NoObjectAnimations ? "on" : "off")}");
            stdout.WriteLine($"  - Water Animation: {(!config.Perf.NoWaterAnimation ? "on" : "off")}");
            if (warnings.Count > 0)
            {
                stdout.WriteLine("\n警告 / Warnings:");
                foreach (string w in warnings) stdout.WriteLine($"  ! {w}");
            }
        }

        return ExitCodes.Success;
    }

    private static bool TryParseOnOff(string s, out bool value)
    {
        if (s.Equals("on", StringComparison.OrdinalIgnoreCase) || s == "1" || s.Equals("true", StringComparison.OrdinalIgnoreCase))
        {
            value = true;
            return true;
        }
        if (s.Equals("off", StringComparison.OrdinalIgnoreCase) || s == "0" || s.Equals("false", StringComparison.OrdinalIgnoreCase))
        {
            value = false;
            return true;
        }
        value = false;
        return false;
    }

    private static int OutputError(string command, string message, int exitCode, bool isJson, TextWriter stdout, TextWriter stderr, List<string>? warnings = null)
    {
        if (isJson)
        {
            var envelope = new JsonEnvelope
            {
                Ok = false,
                Command = command,
                Warnings = warnings ?? [],
                Errors = [message]
            };
            stdout.WriteLine(JsonSerializer.Serialize(envelope, JsonEnvelopeOptions));
        }
        else
        {
            stderr.WriteLine(message);
            if (warnings is { Count: > 0 })
            {
                stderr.WriteLine("\n警告 / Warnings:");
                foreach (string w in warnings) stderr.WriteLine($"  ! {w}");
            }
        }
        return exitCode;
    }

    private static int HandleUnknown(string command, bool isJson, TextWriter stdout, TextWriter stderr)
    {
        string errMsg = Strings.Get("Error_InvalidArgs", $"未知的指令或參數 '{command}'");
        return OutputError(command, errMsg, ExitCodes.InvalidArgs, isJson, stdout, stderr);
    }
}
