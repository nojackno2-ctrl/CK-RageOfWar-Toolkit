using System.Runtime.InteropServices;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using CKToolkit.Core.Common;
using CKToolkit.Core.Lang;
using CKToolkit.Core.Perf;
using CKToolkit.Core.Runtime;
using CKToolkit.Core.Trainer;
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

            case "lang":
                if (commands.Count < 2)
                {
                    string err = Strings.Get("Error_LangSubcommandRequired");
                    return OutputError("lang", err, ExitCodes.InvalidArgs, isJson, stdout, stderr);
                }
                string langSubCmd = commands[1].ToLowerInvariant();
                if (langSubCmd == "list")
                {
                    return HandleLangList(gameDirOverride, configPathOverride, isJson, stdout, stderr);
                }
                if (langSubCmd == "install")
                {
                    return HandleLangInstall(commands.Skip(2).ToList(), gameDirOverride, configPathOverride, isJson, stdout, stderr);
                }
                if (langSubCmd == "uninstall")
                {
                    return HandleLangUninstall(gameDirOverride, configPathOverride, isJson, stdout, stderr);
                }
                if (langSubCmd == "import")
                {
                    return HandleLangImport(commands.Skip(2).ToList(), configPathOverride, isJson, stdout, stderr);
                }
                if (langSubCmd == "export-template")
                {
                    return HandleLangExportTemplate(commands.Skip(2).ToList(), gameDirOverride, configPathOverride, isJson, stdout, stderr);
                }
                return OutputError("lang", Strings.Get("Error_InvalidArgs", $"未知的 lang 子指令 '{commands[1]}'"), ExitCodes.InvalidArgs, isJson, stdout, stderr);

            case "trainer":
                if (commands.Count < 2)
                    return OutputError("trainer", Strings.Get("Error_TrainerSubcommandRequired"), ExitCodes.InvalidArgs, isJson, stdout, stderr);
                string trainerSubCmd = commands[1].ToLowerInvariant();
                if (trainerSubCmd == "list-cheats")
                    return HandleTrainerListCheats(isJson, stdout);
                if (trainerSubCmd == "list-tweaks")
                    return HandleTrainerListTweaks(isJson, stdout);
                if (trainerSubCmd == "set")
                    return HandleTrainerSet(commands.Skip(2).ToList(), gameDirOverride, configPathOverride, isJson, stdout, stderr);
                if (trainerSubCmd == "apply")
                    return HandleApply(gameDirOverride, configPathOverride, isJson, stdout, stderr, "trainer apply");
                return OutputError("trainer", Strings.Get("Error_InvalidArgs", $"未知的 trainer 子指令 '{commands[1]}'"), ExitCodes.InvalidArgs, isJson, stdout, stderr);

            case "profile":
                return HandleProfile(commands.Skip(1).ToList(), isJson, stdout, stderr);

            case "run":
                return HandleRun(commands.Skip(1).ToList(), gameDirOverride, configPathOverride, isJson, stdout, stderr);

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
    /// 處理 status 狀態查詢。此路徑為嚴格唯讀，零寫入、不建目錄、不抓檔案。
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

        var filesStatus = new Dictionary<string, object>();
        var warnings = new List<string>(config.MigrationsApplied);
        if (config.LoadError is not null) warnings.Insert(0, config.LoadError);

        foreach (GameFile f in Enum.GetValues<GameFile>())
        {
            string fileName = PatchState.GetFileName(f);
            string filePath = Path.Combine(gameDir, fileName);

            if (!File.Exists(filePath))
            {
                filesStatus[fileName] = new
                {
                    state = "unrecognised",
                    appliedPatches = (string[])[],
                    isVanilla = false,
                    isPatched = false,
                    isUnrecognised = true,
                    status = Strings.Get("Status_Unrecognised")
                };
                continue;
            }

            byte[] liveBytes;
            try
            {
                liveBytes = File.ReadAllBytes(filePath);
            }
            catch
            {
                filesStatus[fileName] = new
                {
                    state = "unrecognised",
                    appliedPatches = (string[])[],
                    isVanilla = false,
                    isPatched = false,
                    isUnrecognised = true,
                    status = Strings.Get("Status_Unrecognised")
                };
                continue;
            }

            var fileState = PatchState.Inspect(f, liveBytes);
            string stateString = fileState.Kind switch
            {
                FileStateKind.Vanilla => "vanilla",
                FileStateKind.PatchedByUs => "patched",
                _ => "unrecognised"
            };

            string statusDisplay = fileState.Kind switch
            {
                FileStateKind.Vanilla => Strings.Get("Status_Vanilla"),
                FileStateKind.PatchedByUs => Strings.Get("Status_Patched"),
                _ => Strings.Get("Status_Unrecognised")
            };

            filesStatus[fileName] = new
            {
                state = stateString,
                appliedPatches = fileState.AppliedPatches,
                isVanilla = fileState.IsVanilla,
                isPatched = fileState.IsPatched,
                isUnrecognised = fileState.IsUnrecognised,
                status = statusDisplay
            };
        }

        var data = new
        {
            gameDir,
            gameRunning = GamePaths.IsGameRunning(gameDir),
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
    private static int HandleApply(string? gameOverride, string? configOverride, bool isJson, TextWriter stdout, TextWriter stderr, string commandName = "apply")
    {
        var config = ToolkitConfig.Load(configOverride);
        string? gameDir = GamePaths.FindGameDir(gameOverride, config.GameDir);

        if (gameDir is null || !GamePaths.IsGameDir(gameDir))
        {
            string err = Strings.Get("Error_GameNotFound");
            return OutputError(commandName, err, ExitCodes.GameNotFound, isJson, stdout, stderr);
        }

        if (GamePaths.IsGameRunning(gameDir))
        {
            string err = Strings.Get("Error_GameRunning");
            return OutputError(commandName, err, ExitCodes.FileLocked, isJson, stdout, stderr);
        }

        var pipeline = PatchPipeline.CreateDefault();
        var result = pipeline.ApplyAll(gameDir, config);

        var warnings = new List<string>(config.MigrationsApplied);
        if (config.LoadError is not null) warnings.Insert(0, config.LoadError);
        warnings.AddRange(result.Warnings);

        if (!result.Success)
        {
            if (isJson)
            {
                var envelope = new JsonEnvelope
                {
                    Ok = false,
                    Command = commandName,
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
                Command = commandName,
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
                string layeredStr = fileRes.Layered.Count > 0 ? string.Join(", ", fileRes.Layered) : "vanilla";
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
    /// 處理 restore --all 還原指令。將所有已套用修補正規化還原為原版。
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

        if (GamePaths.IsGameRunning(gameDir))
        {
            string err = Strings.Get("Error_GameRunning");
            return OutputError("restore", err, ExitCodes.FileLocked, isJson, stdout, stderr);
        }

        var pipeline = PatchPipeline.CreateDefault();
        var result = pipeline.RestoreAll(gameDir);

        var warnings = new List<string>(config.MigrationsApplied);
        if (config.LoadError is not null) warnings.Insert(0, config.LoadError);
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
                    Errors = [result.ErrorMessage ?? Strings.Get("Error_GeneralFailure", "還原失敗")]
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
                stdout.WriteLine($"  - {fn}: restored={fInfo.Restored} ({fInfo.State})");
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
    /// 處理 verify 驗證指令。檢查檔案修補狀態與當前設定是否相符（嚴格唯讀）。
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
        if (config.LoadError is not null) warnings.Insert(0, config.LoadError);
        warnings.AddRange(result.Warnings);

        var report = result.Value!;
        var filesData = new Dictionary<string, object>();
        foreach (var (fn, fi) in report.Files)
        {
            filesData[fn] = new
            {
                state = fi.State,
                isVanilla = fi.IsVanilla,
                isPatched = fi.IsPatched,
                isUnrecognised = fi.IsUnrecognised,
                appliedPatches = fi.AppliedPatches,
                expectedPatches = fi.ExpectedPatches,
                matchesConfig = fi.MatchesConfig
            };
        }

        var data = new
        {
            gameDir,
            allMatchesConfig = report.AllMatchesConfig,
            allRecognised = report.AllRecognised,
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
            stdout.WriteLine(report.AllMatchesConfig && report.AllRecognised
                ? Strings.Get("Verify_AllOk")
                : Strings.Get("Verify_Mismatch"));
            stdout.WriteLine(Strings.Get("Status_GameDir", gameDir));
            foreach (var (fn, fi) in report.Files)
            {
                stdout.WriteLine($"  - {fn}: state={fi.State}, matchesConfig={fi.MatchesConfig} (applied: [{string.Join(", ", fi.AppliedPatches)}], expected: [{string.Join(", ", fi.ExpectedPatches)}])");
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
        if (config.LoadError is not null) warnings.Insert(0, config.LoadError);

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
        if (config.LoadError is not null) warnings.Insert(0, config.LoadError);

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
                            config.Perf.AddRes.RemoveAll(r =>
                            {
                                var (rw, _) = PerfModule.ParseDimensions(r, 0, 0);
                                return rw > 1600;
                            });
                            var (curW, _) = PerfModule.ParseDimensions(config.Perf.Resolution, 0, 0);
                            if (curW > 1600)
                            {
                                string oldRes = config.Perf.Resolution;
                                config.Perf.Resolution = "1600x1200";
                                warnings.Add(Strings.Get("Warning_ResolutionExceedsCapacity", oldRes, 1600, "1600x1200", 3));
                            }
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
                            config.Perf.AddRes.RemoveAll(r =>
                            {
                                var (rw, _) = PerfModule.ParseDimensions(r, 0, 0);
                                return rw > w;
                            });
                            var (curW, _) = PerfModule.ParseDimensions(config.Perf.Resolution, 0, 0);
                            if (curW > w)
                            {
                                string oldRes = config.Perf.Resolution;
                                config.Perf.Resolution = "1600x1200";
                                warnings.Add(Strings.Get("Warning_ResolutionExceedsCapacity", oldRes, w, "1600x1200", 3));
                            }

                            if (w > 2560)
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
                        if (rw > 1600)
                        {
                            if (!config.Perf.AddRes.Contains(normRes, StringComparer.OrdinalIgnoreCase))
                            {
                                config.Perf.AddRes.Add(normRes);
                            }
                        }
                        else
                        {
                            int curCapacity = config.Perf.Hires >= 1600 ? config.Perf.Hires : 1600;
                            config.Perf.AddRes.RemoveAll(r =>
                            {
                                var (w, _) = PerfModule.ParseDimensions(r, 0, 0);
                                return w > curCapacity;
                            });
                        }
                        if (rw > 2560 || rh > 1440)
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

    private static int HandleLangList(string? gameOverride, string? configOverride, bool isJson, TextWriter stdout, TextWriter stderr)
    {
        var config = ToolkitConfig.Load(configOverride);
        var packs = PackLoader.DiscoverAll();

        var packList = packs.Values.Select(p => new
        {
            id = p.Meta.Id,
            name = p.Meta.Name,
            nativeName = p.Meta.NativeName,
            version = p.Meta.Version,
            authors = p.Meta.Authors,
            gameLangFolder = p.Meta.GameLangFolder,
            gameLangKey = p.Meta.GameLangKey,
            templateLang = p.Meta.TemplateLang,
            fontFace = p.Meta.Font.Face,
            isBuiltIn = p.IsBuiltIn,
            sourcePath = p.SourcePath
        }).ToList();

        var data = new
        {
            currentPack = config.Lang.Pack,
            currentFontFace = config.Lang.FontFace,
            packs = packList
        };

        var warnings = new List<string>(config.MigrationsApplied);
        if (config.LoadError is not null) warnings.Insert(0, config.LoadError);

        if (isJson)
        {
            var envelope = new JsonEnvelope
            {
                Ok = true,
                Command = "lang list",
                Data = data,
                Warnings = warnings
            };
            stdout.WriteLine(JsonSerializer.Serialize(envelope, JsonEnvelopeOptions));
        }
        else
        {
            stdout.WriteLine("可用語言包清單 (Available Language Packs):");
            stdout.WriteLine($"目前設定 (Current Config): {config.Lang.Pack} (字型: {config.Lang.FontFace})\n");
            foreach (var p in packs.Values)
            {
                string tag = p.IsBuiltIn ? "[內建 / Built-in]" : "[外部 / External]";
                stdout.WriteLine($"  * {p.Meta.Id} - {p.Meta.NativeName} ({p.Meta.Name}) v{p.Meta.Version} {tag}");
                stdout.WriteLine($"      作者: {string.Join(", ", p.Meta.Authors)}");
                stdout.WriteLine($"      語系代號: {p.Meta.GameLangKey} -> {p.Meta.GameLangFolder}\\ (模板: {p.Meta.TemplateLang})");
                stdout.WriteLine($"      預設字型: {p.Meta.Font.Face}");
            }
            if (warnings.Count > 0)
            {
                stdout.WriteLine("\n警告 / Warnings:");
                foreach (string w in warnings) stdout.WriteLine($"  ! {w}");
            }
        }

        return ExitCodes.Success;
    }

    private static int HandleLangInstall(List<string> options, string? gameOverride, string? configOverride, bool isJson, TextWriter stdout, TextWriter stderr)
    {
        string? targetPackId = null;
        string? fontFace = null;

        for (int i = 0; i < options.Count; i++)
        {
            string opt = options[i];
            if (opt.Equals("--pack", StringComparison.OrdinalIgnoreCase) && i + 1 < options.Count)
            {
                targetPackId = options[++i];
            }
            else if (opt.Equals("--font", StringComparison.OrdinalIgnoreCase) && i + 1 < options.Count)
            {
                fontFace = options[++i];
            }
        }

        if (string.IsNullOrWhiteSpace(targetPackId))
        {
            string err = Strings.Get("Error_LangInstallMissingPack");
            return OutputError("lang install", err, ExitCodes.InvalidArgs, isJson, stdout, stderr);
        }

        var packs = PackLoader.DiscoverAll();
        if (!packs.TryGetValue(targetPackId, out var pack))
        {
            string err = Strings.Get("Error_LangPackNotFound", targetPackId);
            return OutputError("lang install", err, ExitCodes.InvalidArgs, isJson, stdout, stderr);
        }

        var config = ToolkitConfig.Load(configOverride);
        if (!string.IsNullOrWhiteSpace(gameOverride))
        {
            config.GameDir = gameOverride;
        }

        config.Lang.Pack = pack.Meta.Id;
        if (!string.IsNullOrWhiteSpace(fontFace))
        {
            config.Lang.FontFace = fontFace;
        }
        else if (!string.IsNullOrWhiteSpace(pack.Meta.Font.Face))
        {
            config.Lang.FontFace = pack.Meta.Font.Face;
        }

        config.Save(configOverride);

        var warnings = new List<string>(config.MigrationsApplied);
        if (config.LoadError is not null) warnings.Insert(0, config.LoadError);

        var data = new
        {
            pack = config.Lang.Pack,
            fontFace = config.Lang.FontFace,
            gameLangFolder = pack.Meta.GameLangFolder,
            gameLangKey = pack.Meta.GameLangKey
        };

        if (isJson)
        {
            var envelope = new JsonEnvelope
            {
                Ok = true,
                Command = "lang install",
                Data = data,
                Warnings = warnings
            };
            stdout.WriteLine(JsonSerializer.Serialize(envelope, JsonEnvelopeOptions));
        }
        else
        {
            stdout.WriteLine(Strings.Get("Lang_Install_Success", config.Lang.Pack));
            stdout.WriteLine($"  - 語言包 ID: {config.Lang.Pack}");
            stdout.WriteLine($"  - 字型: {config.Lang.FontFace}");
            if (warnings.Count > 0)
            {
                stdout.WriteLine("\n警告 / Warnings:");
                foreach (string w in warnings) stdout.WriteLine($"  ! {w}");
            }
        }

        return ExitCodes.Success;
    }

    private static int HandleLangUninstall(string? gameOverride, string? configOverride, bool isJson, TextWriter stdout, TextWriter stderr)
    {
        var config = ToolkitConfig.Load(configOverride);
        if (!string.IsNullOrWhiteSpace(gameOverride))
        {
            config.GameDir = gameOverride;
        }

        config.Lang.Pack = string.Empty;
        config.Save(configOverride);

        var warnings = new List<string>(config.MigrationsApplied);
        if (config.LoadError is not null) warnings.Insert(0, config.LoadError);

        var data = new
        {
            pack = string.Empty
        };

        if (isJson)
        {
            var envelope = new JsonEnvelope
            {
                Ok = true,
                Command = "lang uninstall",
                Data = data,
                Warnings = warnings
            };
            stdout.WriteLine(JsonSerializer.Serialize(envelope, JsonEnvelopeOptions));
        }
        else
        {
            stdout.WriteLine(Strings.Get("Lang_Uninstall_Success"));
            if (warnings.Count > 0)
            {
                stdout.WriteLine("\n警告 / Warnings:");
                foreach (string w in warnings) stdout.WriteLine($"  ! {w}");
            }
        }

        return ExitCodes.Success;
    }

    private static int HandleLangExportTemplate(List<string> options, string? gameOverride, string? configOverride, bool isJson, TextWriter stdout, TextWriter stderr)
    {
        string? outDir = null;
        string templateLang = "ENGLISH";

        for (int i = 0; i < options.Count; i++)
        {
            string opt = options[i];
            if (opt.Equals("--out", StringComparison.OrdinalIgnoreCase) && i + 1 < options.Count)
            {
                outDir = options[++i];
            }
            else if (opt.Equals("--template", StringComparison.OrdinalIgnoreCase) && i + 1 < options.Count)
            {
                templateLang = options[++i];
            }
        }

        if (string.IsNullOrWhiteSpace(outDir))
        {
            string err = Strings.Get("Error_ExportTemplateMissingOut");
            return OutputError("lang export-template", err, ExitCodes.InvalidArgs, isJson, stdout, stderr);
        }

        var config = ToolkitConfig.Load(configOverride);
        string? gameDir = GamePaths.FindGameDir(gameOverride, config.GameDir);

        if (gameDir is null || !GamePaths.IsGameDir(gameDir))
        {
            string err = Strings.Get("Error_GameNotFound");
            return OutputError("lang export-template", err, ExitCodes.GameNotFound, isJson, stdout, stderr);
        }

        string localPakPath = GamePaths.GetLocalPakPath(gameDir);
        if (!File.Exists(localPakPath))
        {
            string err = Strings.Get("Error_GameNotFound");
            return OutputError("lang export-template", err, ExitCodes.GameNotFound, isJson, stdout, stderr);
        }

        HmmPak localPak;
        try
        {
            localPak = HmmPak.Load(localPakPath);
        }
        catch (Exception ex)
        {
            return OutputError("lang export-template", Strings.Get("Error_GeneralFailure", $"讀取 local.pak 失敗：{ex.Message}"), ExitCodes.GeneralFailure, isJson, stdout, stderr);
        }

        try
        {
            LangInstaller.ExportTemplate(localPak, templateLang, outDir, msg =>
            {
                if (!isJson) stdout.WriteLine(msg);
            });
        }
        catch (Exception ex)
        {
            return OutputError("lang export-template", Strings.Get("Error_GeneralFailure", $"匯出範本失敗：{ex.Message}"), ExitCodes.GeneralFailure, isJson, stdout, stderr);
        }

        var warnings = new List<string>(config.MigrationsApplied);
        if (config.LoadError is not null) warnings.Insert(0, config.LoadError);

        var data = new
        {
            outDir = Path.GetFullPath(outDir),
            templateLang
        };

        if (isJson)
        {
            var envelope = new JsonEnvelope
            {
                Ok = true,
                Command = "lang export-template",
                Data = data,
                Warnings = warnings
            };
            stdout.WriteLine(JsonSerializer.Serialize(envelope, JsonEnvelopeOptions));
        }
        else
        {
            stdout.WriteLine(Strings.Get("Lang_ExportTemplate_Success", Path.GetFullPath(outDir)));
            if (warnings.Count > 0)
            {
                stdout.WriteLine("\n警告 / Warnings:");
                foreach (string w in warnings) stdout.WriteLine($"  ! {w}");
            }
        }

        return ExitCodes.Success;
    }

    private static int HandleLangImport(List<string> options, string? configOverride, bool isJson, TextWriter stdout, TextWriter stderr)
    {
        string? srcDir = null;
        bool overwrite = false;

        for (int i = 0; i < options.Count; i++)
        {
            string opt = options[i];
            if ((opt.Equals("--src", StringComparison.OrdinalIgnoreCase) ||
                 opt.Equals("--from", StringComparison.OrdinalIgnoreCase)) && i + 1 < options.Count)
            {
                srcDir = options[++i];
            }
            else if (opt.Equals("--overwrite", StringComparison.OrdinalIgnoreCase))
            {
                overwrite = true;
            }
        }

        if (string.IsNullOrWhiteSpace(srcDir))
        {
            string err = Strings.Get("Error_LangImportSourceMissing");
            return OutputError("lang import", err, ExitCodes.InvalidArgs, isJson, stdout, stderr);
        }

        Func<string, string, bool>? overwritePrompt = overwrite ? ((_, _) => true) : null;
        Result<LanguagePack> result = LangPackService.ImportPack(srcDir, customTargetBaseDir: null, overwritePrompt: overwritePrompt);

        if (!result.Success || result.Value is null)
        {
            return OutputError("lang import", result.ErrorMessage ?? Strings.Get("Error_GeneralFailure", "匯入失敗"), result.ExitCode, isJson, stdout, stderr);
        }

        LanguagePack pack = result.Value;
        string targetDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "langpacks", pack.Meta.Id));

        var config = ToolkitConfig.Load(configOverride);
        var warnings = new List<string>(config.MigrationsApplied);
        if (config.LoadError is not null) warnings.Insert(0, config.LoadError);

        var data = new
        {
            packId = pack.Meta.Id,
            name = pack.Meta.Name,
            nativeName = pack.Meta.NativeName,
            version = pack.Meta.Version,
            targetDir
        };

        if (isJson)
        {
            var envelope = new JsonEnvelope
            {
                Ok = true,
                Command = "lang import",
                Data = data,
                Warnings = warnings
            };
            stdout.WriteLine(JsonSerializer.Serialize(envelope, JsonEnvelopeOptions));
        }
        else
        {
            stdout.WriteLine(Strings.Get("Lang_Import_Success", pack.Meta.Name, pack.Meta.Id, targetDir));
            if (warnings.Count > 0)
            {
                stdout.WriteLine("\n警告 / Warnings:");
                foreach (string w in warnings) stdout.WriteLine($"  ! {w}");
            }
        }

        return ExitCodes.Success;
    }

    private static int HandleTrainerListCheats(bool isJson, TextWriter stdout)
    {
        var cheatList = Cheats.All.Select(c => new
        {
            id = c.Id,
            label = c.Name,
            name = c.Name,
            description = c.Description,
            defaultKey = c.DefaultKey,
            defaultKeyDisplay = KeyMap.Display(c.DefaultKey, numpadKeys: false),
            numpadKey = c.NumpadKey,
            numpadKeyDisplay = KeyMap.Display(c.NumpadKey, numpadKeys: true),
            defaultEnabled = c.DefaultEnabled,
            numpadDefaultEnabled = c.NumpadDefaultEnabled,
            parameters = c.Parameters.Select(p => new
            {
                name = p.Name,
                label = p.Label,
                description = p.Label,
                @default = p.Default,
                minimum = p.Minimum,
                maximum = p.Maximum,
                isText = p.IsText,
                isMulti = p.IsMulti,
                hidden = p.Hidden
            }).ToList()
        }).ToList();

        var data = new
        {
            totalCheats = cheatList.Count,
            cheats = cheatList
        };

        if (isJson)
        {
            var envelope = new JsonEnvelope
            {
                Ok = true,
                Command = "trainer list-cheats",
                Data = data
            };
            stdout.WriteLine(JsonSerializer.Serialize(envelope, JsonEnvelopeOptions));
        }
        else
        {
            stdout.WriteLine("作弊項目清單 (Available Cheats - 14):");
            foreach (var c in Cheats.All)
            {
                stdout.WriteLine($"  * {c.Id} - {c.Name}");
                stdout.WriteLine($"      預設按鍵 (原版): {c.DefaultKey} ({KeyMap.Display(c.DefaultKey, false)}) [{(c.DefaultEnabled ? "預設開啟" : "預設關閉")}]");
                stdout.WriteLine($"      預設按鍵 (小鍵盤): {c.NumpadKey} ({KeyMap.Display(c.NumpadKey, true)}) [{(c.NumpadDefaultEnabled ? "預設開啟" : "預設關閉")}]");
                stdout.WriteLine($"      說明: {c.Description}");
                if (c.Parameters.Count > 0)
                {
                    stdout.WriteLine("      參數 (Parameters):");
                    foreach (var p in c.Parameters)
                    {
                        string rangeStr = p.IsText ? "(文字選項)" : $"[{p.Minimum}..{p.Maximum}]";
                        stdout.WriteLine($"        - {p.Name}: {p.Label} (預設: {p.Default}, 範圍: {rangeStr})");
                    }
                }
            }
        }

        return ExitCodes.Success;
    }

    private static int HandleTrainerListTweaks(bool isJson, TextWriter stdout)
    {
        var groups = Tweaks.Groups().Select(g => new
        {
            group = g.Group,
            tweaks = g.Items.Select(t => new
            {
                id = t.Id,
                group = t.Group,
                label = t.Label,
                description = t.Description,
                @default = t.Default,
                minimum = t.Minimum,
                maximum = t.Maximum,
                isMultiplier = t.IsMultiplier
            }).ToList()
        }).ToList();

        var flatTweaks = Tweaks.All.Select(t => new
        {
            id = t.Id,
            group = t.Group,
            label = t.Label,
            description = t.Description,
            @default = t.Default,
            minimum = t.Minimum,
            maximum = t.Maximum,
            isMultiplier = t.IsMultiplier
        }).ToList();

        var data = new
        {
            totalTweaks = flatTweaks.Count,
            groups,
            tweaks = flatTweaks
        };

        if (isJson)
        {
            var envelope = new JsonEnvelope
            {
                Ok = true,
                Command = "trainer list-tweaks",
                Data = data
            };
            stdout.WriteLine(JsonSerializer.Serialize(envelope, JsonEnvelopeOptions));
        }
        else
        {
            stdout.WriteLine("數值調整清單 (Available Tweaks):");
            foreach (var (grp, items) in Tweaks.Groups())
            {
                stdout.WriteLine($"\n[{grp}] ({items.Count} 項)");
                foreach (var t in items)
                {
                    string mulStr = t.IsMultiplier ? " [倍率 / Multiplier]" : "";
                    stdout.WriteLine($"  * {t.Id}{mulStr}: {t.Label}");
                    stdout.WriteLine($"      預設: {t.Default} (範圍: [{t.Minimum}..{t.Maximum}])");
                    stdout.WriteLine($"      說明: {t.Description}");
                }
            }
        }

        return ExitCodes.Success;
    }

    private static int HandleTrainerSet(
        List<string> options,
        string? gameOverride,
        string? configOverride,
        bool isJson,
        TextWriter stdout,
        TextWriter stderr)
    {
        if (options.Count == 0)
        {
            string err = Strings.Get("Error_InvalidArgs", "trainer set 必須提供至少一個設定選項");
            return OutputError("trainer set", err, ExitCodes.InvalidArgs, isJson, stdout, stderr);
        }

        var config = ToolkitConfig.Load(configOverride);
        if (!string.IsNullOrWhiteSpace(gameOverride))
        {
            config.GameDir = gameOverride;
        }

        var warnings = new List<string>(config.MigrationsApplied);
        if (config.LoadError is not null) warnings.Insert(0, config.LoadError);

        for (int i = 0; i < options.Count; i++)
        {
            string token = options[i];
            string flag;
            string val;

            if (token.StartsWith("--"))
            {
                int eqIdx = token.IndexOf('=');
                if (eqIdx > 0)
                {
                    flag = token[..eqIdx].ToLowerInvariant();
                    val = token[(eqIdx + 1)..];
                }
                else if (i + 1 < options.Count)
                {
                    flag = token.ToLowerInvariant();
                    val = options[++i];
                }
                else
                {
                    string err = Strings.Get("Error_InvalidArgs", $"缺少選項值 '{token}'");
                    return OutputError("trainer set", err, ExitCodes.InvalidArgs, isJson, stdout, stderr, warnings);
                }
            }
            else
            {
                string err = Strings.Get("Error_InvalidArgs", $"無效的語法 '{token}'");
                return OutputError("trainer set", err, ExitCodes.InvalidArgs, isJson, stdout, stderr, warnings);
            }

            switch (flag)
            {
                case "--cheat":
                {
                    int eq = val.IndexOf('=');
                    if (eq <= 0)
                    {
                        string err = Strings.Get("Error_InvalidArgs", $"--cheat 格式必須為 <id>=on|off，實際為 '{val}'");
                        return OutputError("trainer set", err, ExitCodes.InvalidArgs, isJson, stdout, stderr, warnings);
                    }
                    string cheatId = val[..eq].Trim();
                    string stateStr = val[(eq + 1)..].Trim();

                    if (!Cheats.ById.TryGetValue(cheatId, out var cheat))
                    {
                        string err = Strings.Get("Error_TrainerUnknownCheat", cheatId);
                        return OutputError("trainer set", err, ExitCodes.InvalidArgs, isJson, stdout, stderr, warnings);
                    }

                    if (!TryParseOnOff(stateStr, out bool cheatEnabled))
                    {
                        string err = Strings.Get("Error_InvalidArgs", $"--cheat 開關必須為 on 或 off，實際為 '{stateStr}'");
                        return OutputError("trainer set", err, ExitCodes.InvalidArgs, isJson, stdout, stderr, warnings);
                    }

                    var cheatCfg = config.Trainer.Cheats.FirstOrDefault(c => c.Id.Equals(cheat.Id, StringComparison.OrdinalIgnoreCase));
                    if (cheatCfg is null)
                    {
                        cheatCfg = new CheatConfig
                        {
                            Id = cheat.Id,
                            Enabled = cheatEnabled,
                            Key = cheat.DefaultKeyFor(config.Trainer.NumpadKeys)
                        };
                        config.Trainer.Cheats.Add(cheatCfg);
                    }
                    else
                    {
                        cheatCfg.Enabled = cheatEnabled;
                    }
                    break;
                }

                case "--key":
                {
                    int eq = val.IndexOf('=');
                    if (eq <= 0)
                    {
                        string err = Strings.Get("Error_InvalidArgs", $"--key 格式必須為 <id>=<KEY>，實際為 '{val}'");
                        return OutputError("trainer set", err, ExitCodes.InvalidArgs, isJson, stdout, stderr, warnings);
                    }
                    string cheatId = val[..eq].Trim();
                    string keyName = val[(eq + 1)..].Trim();

                    if (!Cheats.ById.TryGetValue(cheatId, out var cheat))
                    {
                        string err = Strings.Get("Error_TrainerUnknownCheat", cheatId);
                        return OutputError("trainer set", err, ExitCodes.InvalidArgs, isJson, stdout, stderr, warnings);
                    }

                    var binding = KeyMap.All.FirstOrDefault(b => b.Key.Equals(keyName, StringComparison.OrdinalIgnoreCase));
                    if (binding is null)
                    {
                        string err = Strings.Get("Error_TrainerInvalidKey", keyName);
                        return OutputError("trainer set", err, ExitCodes.InvalidArgs, isJson, stdout, stderr, warnings);
                    }

                    var cheatCfg = config.Trainer.Cheats.FirstOrDefault(c => c.Id.Equals(cheat.Id, StringComparison.OrdinalIgnoreCase));
                    if (cheatCfg is null)
                    {
                        cheatCfg = new CheatConfig
                        {
                            Id = cheat.Id,
                            Enabled = cheat.DefaultEnabledFor(config.Trainer.NumpadKeys),
                            Key = binding.Key
                        };
                        config.Trainer.Cheats.Add(cheatCfg);
                    }
                    else
                    {
                        cheatCfg.Key = binding.Key;
                    }
                    break;
                }

                case "--param":
                {
                    int dotIdx = val.IndexOf('.');
                    if (dotIdx <= 0)
                    {
                        string err = Strings.Get("Error_InvalidArgs", $"--param 格式必須為 <id>.<name>=<v>，實際為 '{val}'");
                        return OutputError("trainer set", err, ExitCodes.InvalidArgs, isJson, stdout, stderr, warnings);
                    }
                    string cheatId = val[..dotIdx].Trim();
                    string rest = val[(dotIdx + 1)..];
                    int eqIdx = rest.IndexOf('=');
                    if (eqIdx <= 0)
                    {
                        string err = Strings.Get("Error_InvalidArgs", $"--param 格式必須為 <id>.<name>=<v>，實際為 '{val}'");
                        return OutputError("trainer set", err, ExitCodes.InvalidArgs, isJson, stdout, stderr, warnings);
                    }
                    string paramName = rest[..eqIdx].Trim();
                    string paramVal = rest[(eqIdx + 1)..].Trim();

                    if (!Cheats.ById.TryGetValue(cheatId, out var cheat))
                    {
                        string err = Strings.Get("Error_TrainerUnknownCheat", cheatId);
                        return OutputError("trainer set", err, ExitCodes.InvalidArgs, isJson, stdout, stderr, warnings);
                    }

                    var paramDef = cheat.Parameters.FirstOrDefault(p => p.Name.Equals(paramName, StringComparison.OrdinalIgnoreCase));
                    if (paramDef is null)
                    {
                        string err = Strings.Get("Error_TrainerUnknownParam", cheat.Id, paramName);
                        return OutputError("trainer set", err, ExitCodes.InvalidArgs, isJson, stdout, stderr, warnings);
                    }

                    if (!paramDef.IsText)
                    {
                        if (!decimal.TryParse(paramVal, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out decimal numVal))
                        {
                            string err = Strings.Get("Error_TrainerParamOutOfRange", cheat.Id, paramDef.Name, paramVal, paramDef.Minimum, paramDef.Maximum);
                            return OutputError("trainer set", err, ExitCodes.InvalidArgs, isJson, stdout, stderr, warnings);
                        }
                        if ((paramDef.Minimum != 0 || paramDef.Maximum != 0) && (numVal < paramDef.Minimum || numVal > paramDef.Maximum))
                        {
                            string err = Strings.Get("Error_TrainerParamOutOfRange", cheat.Id, paramDef.Name, paramVal, paramDef.Minimum, paramDef.Maximum);
                            return OutputError("trainer set", err, ExitCodes.InvalidArgs, isJson, stdout, stderr, warnings);
                        }
                    }
                    else if (paramDef.HasOptions)
                    {
                        var validOptions = paramDef.Options?.Select(o => o.Value).ToHashSet(StringComparer.OrdinalIgnoreCase)
                            ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                        if (paramDef.IsMulti)
                        {
                            var parts = paramVal.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                            if (parts.Length == 0)
                            {
                                string err = Strings.Get("Error_TrainerParamInvalidOption", cheat.Id, paramDef.Name, paramVal);
                                return OutputError("trainer set", err, ExitCodes.InvalidArgs, isJson, stdout, stderr, warnings);
                            }
                            foreach (var p in parts)
                            {
                                if (!validOptions.Contains(p))
                                {
                                    string err = Strings.Get("Error_TrainerParamInvalidOption", cheat.Id, paramDef.Name, p);
                                    return OutputError("trainer set", err, ExitCodes.InvalidArgs, isJson, stdout, stderr, warnings);
                                }
                            }
                        }
                        else
                        {
                            if (!validOptions.Contains(paramVal))
                            {
                                string err = Strings.Get("Error_TrainerParamInvalidOption", cheat.Id, paramDef.Name, paramVal);
                                return OutputError("trainer set", err, ExitCodes.InvalidArgs, isJson, stdout, stderr, warnings);
                            }
                        }
                    }

                    var cheatCfg = config.Trainer.Cheats.FirstOrDefault(c => c.Id.Equals(cheat.Id, StringComparison.OrdinalIgnoreCase));
                    if (cheatCfg is null)
                    {
                        cheatCfg = new CheatConfig
                        {
                            Id = cheat.Id,
                            Enabled = cheat.DefaultEnabledFor(config.Trainer.NumpadKeys),
                            Key = cheat.DefaultKeyFor(config.Trainer.NumpadKeys)
                        };
                        config.Trainer.Cheats.Add(cheatCfg);
                    }
                    cheatCfg.Parameters[paramDef.Name] = paramVal;
                    break;
                }

                case "--tweak":
                {
                    int eq = val.IndexOf('=');
                    if (eq <= 0)
                    {
                        string err = Strings.Get("Error_InvalidArgs", $"--tweak 格式必須為 <id>=<value>，實際為 '{val}'");
                        return OutputError("trainer set", err, ExitCodes.InvalidArgs, isJson, stdout, stderr, warnings);
                    }
                    string tweakId = val[..eq].Trim();
                    string tweakValStr = val[(eq + 1)..].Trim();

                    if (!Tweaks.ById.TryGetValue(tweakId, out var tweak))
                    {
                        string err = Strings.Get("Error_TrainerUnknownTweak", tweakId);
                        return OutputError("trainer set", err, ExitCodes.InvalidArgs, isJson, stdout, stderr, warnings);
                    }

                    if (!decimal.TryParse(tweakValStr, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out decimal decVal))
                    {
                        string err = Strings.Get("Error_TrainerTweakOutOfRange", tweak.Id, tweakValStr, tweak.Minimum, tweak.Maximum);
                        return OutputError("trainer set", err, ExitCodes.InvalidArgs, isJson, stdout, stderr, warnings);
                    }

                    if (decVal < tweak.Minimum || decVal > tweak.Maximum)
                    {
                        string err = Strings.Get("Error_TrainerTweakOutOfRange", tweak.Id, tweakValStr, tweak.Minimum, tweak.Maximum);
                        return OutputError("trainer set", err, ExitCodes.InvalidArgs, isJson, stdout, stderr, warnings);
                    }

                    config.Trainer.Tweaks[tweak.Id] = decVal;
                    break;
                }

                case "--numpad":
                {
                    if (TryParseOnOff(val, out bool numpad))
                    {
                        config.Trainer.NumpadKeys = numpad;
                    }
                    else
                    {
                        string err = Strings.Get("Error_InvalidArgs", $"--numpad 必須為 on 或 off，實際為 '{val}'");
                        return OutputError("trainer set", err, ExitCodes.InvalidArgs, isJson, stdout, stderr, warnings);
                    }
                    break;
                }

                case "--trainer" or "--enabled":
                {
                    if (TryParseOnOff(val, out bool enabled))
                    {
                        config.Trainer.Enabled = enabled;
                    }
                    else
                    {
                        string err = Strings.Get("Error_InvalidArgs", $"--trainer 必須為 on 或 off，實際為 '{val}'");
                        return OutputError("trainer set", err, ExitCodes.InvalidArgs, isJson, stdout, stderr, warnings);
                    }
                    break;
                }

                case "--player-mode":
                {
                    if (val.Equals("auto", StringComparison.OrdinalIgnoreCase) || val.Equals("fixed", StringComparison.OrdinalIgnoreCase))
                    {
                        config.Trainer.PlayerMode = val.ToLowerInvariant();
                    }
                    else
                    {
                        string err = Strings.Get("Error_InvalidArgs", $"--player-mode 必須為 auto 或 fixed，實際為 '{val}'");
                        return OutputError("trainer set", err, ExitCodes.InvalidArgs, isJson, stdout, stderr, warnings);
                    }
                    break;
                }

                case "--fixed-player":
                {
                    if (int.TryParse(val, out int fp) && fp >= 1 && fp <= 16)
                    {
                        config.Trainer.FixedPlayer = fp;
                    }
                    else
                    {
                        string err = Strings.Get("Error_InvalidArgs", $"--fixed-player 必須為 1 到 16 之間的整數，實際為 '{val}'");
                        return OutputError("trainer set", err, ExitCodes.InvalidArgs, isJson, stdout, stderr, warnings);
                    }
                    break;
                }

                case "--keep-vanilla":
                {
                    if (TryParseOnOff(val, out bool kv))
                    {
                        config.Trainer.KeepVanilla = kv;
                    }
                    else
                    {
                        string err = Strings.Get("Error_InvalidArgs", $"--keep-vanilla 必須為 on 或 off，實際為 '{val}'");
                        return OutputError("trainer set", err, ExitCodes.InvalidArgs, isJson, stdout, stderr, warnings);
                    }
                    break;
                }

                default:
                    return OutputError("trainer set", Strings.Get("Error_InvalidArgs", $"未知的設定選項 '{token}'"), ExitCodes.InvalidArgs, isJson, stdout, stderr, warnings);
            }
        }

        // 驗證按鍵一對一（所有已啟用的作弊不能重複綁定同一按鍵）
        var usedKeys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in config.Trainer.Cheats.Where(c => c.Enabled))
        {
            if (!Cheats.ById.TryGetValue(c.Id, out var cheatDef)) continue;
            string effectiveKey = !string.IsNullOrWhiteSpace(c.Key)
                ? c.Key
                : cheatDef.DefaultKeyFor(config.Trainer.NumpadKeys);

            if (usedKeys.TryGetValue(effectiveKey, out string? existingCheatId))
            {
                string err = Strings.Get("Error_TrainerDuplicateKey", effectiveKey, existingCheatId, c.Id);
                return OutputError("trainer set", err, ExitCodes.InvalidArgs, isJson, stdout, stderr, warnings);
            }
            usedKeys[effectiveKey] = c.Id;
        }

        // 當 trainer.enabled 為 false 時加入警告提醒
        if (!config.Trainer.Enabled)
        {
            warnings.Add(Strings.Get("Warning_TrainerNotEnabled"));
        }

        // 寫入設定檔（嚴格僅寫入 cktoolkit.json，絕對不碰遊戲檔案）
        config.Save(configOverride);

        var trainerData = new
        {
            enabled = config.Trainer.Enabled,
            numpadKeys = config.Trainer.NumpadKeys,
            playerMode = config.Trainer.PlayerMode,
            fixedPlayer = config.Trainer.FixedPlayer,
            keepVanilla = config.Trainer.KeepVanilla,
            cheats = config.Trainer.Cheats.Select(c => new
            {
                id = c.Id,
                enabled = c.Enabled,
                key = c.Key,
                parameters = c.Parameters
            }).ToList(),
            tweaks = config.Trainer.Tweaks
        };

        if (isJson)
        {
            var envelope = new JsonEnvelope
            {
                Ok = true,
                Command = "trainer set",
                Data = trainerData,
                Warnings = warnings
            };
            stdout.WriteLine(JsonSerializer.Serialize(envelope, JsonEnvelopeOptions));
        }
        else
        {
            stdout.WriteLine(Strings.Get("Trainer_Set_Success"));
            stdout.WriteLine("更新後的修改器設定 (Updated Trainer Settings):");
            stdout.WriteLine($"  - 修改器開關 (Enabled): {(config.Trainer.Enabled ? "on" : "off")}");
            stdout.WriteLine($"  - 按鍵模式 (NumpadKeys): {(config.Trainer.NumpadKeys ? "小鍵盤 (numpad)" : "原版 (original)")}");
            stdout.WriteLine($"  - 玩家目標 (PlayerMode): {config.Trainer.PlayerMode}{(config.Trainer.PlayerMode == "fixed" ? $" (玩家 #{config.Trainer.FixedPlayer})" : "")}");
            stdout.WriteLine($"  - 保留原版按鍵 (KeepVanilla): {(config.Trainer.KeepVanilla ? "on" : "off")}");
            stdout.WriteLine($"  - 已設定作弊項目數: {config.Trainer.Cheats.Count(c => c.Enabled)} 項啟用 / {config.Trainer.Cheats.Count} 項設定");
            foreach (var c in config.Trainer.Cheats.Where(c => c.Enabled))
            {
                stdout.WriteLine($"      * {c.Id}: [{(c.Enabled ? "on" : "off")}] key={c.Key}");
            }
            stdout.WriteLine($"  - 已設定調整項目 (Tweaks): {config.Trainer.Tweaks.Count} 項");
            foreach (var (k, v) in config.Trainer.Tweaks)
            {
                stdout.WriteLine($"      * {k} = {v}");
            }
            if (warnings.Count > 0)
            {
                stdout.WriteLine("\n警告 / Warnings:");
                foreach (string w in warnings) stdout.WriteLine($"  ! {w}");
            }
        }

        return ExitCodes.Success;
    }

    private static int HandleProfile(
        List<string> options,
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
                    if (flag == "--wait")
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
                            opt.OutFile = val;
                        }
                        else
                        {
                            return OutputError("profile", Strings.Get("Error_InvalidArgs", "--out 必須指定檔案路徑"), ExitCodes.InvalidArgs, isJson, stdout, stderr);
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

                    case "--wait":
                        if (val is null)
                        {
                            opt.WaitForProcess = true;
                        }
                        else if (TryParseOnOff(val, out bool waitVal))
                        {
                            opt.WaitForProcess = waitVal;
                        }
                        else
                        {
                            return OutputError("profile", Strings.Get("Error_InvalidArgs", $"--wait 必須為 on 或 off，實際為 '{val}'"), ExitCodes.InvalidArgs, isJson, stdout, stderr);
                        }
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

        var result = Profiler.Run(opt);

        if (!result.Success)
        {
            return OutputError("profile", result.ErrorMessage ?? Strings.Get("Error_GeneralFailure", "取樣分析失敗"), result.ExitCode, isJson, stdout, stderr);
        }

        string reportText = result.Value ?? string.Empty;

        var data = new
        {
            processName = opt.ProcessName,
            seconds = opt.Seconds,
            hz = opt.Hz,
            segmentSeconds = opt.SegmentSeconds,
            outFile = opt.OutFile is not null ? Path.GetFullPath(opt.OutFile) : null,
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
            if (!string.IsNullOrWhiteSpace(opt.OutFile))
            {
                stdout.WriteLine(Strings.Get("Profile_ReportWritten", Path.GetFullPath(opt.OutFile)));
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
        string manifestPath;
        var manifestWarnings = new List<string>();
        try
        {
            Directory.CreateDirectory(GameRunner.DiagnosticsDirectory);
            manifestPath = RunManifest.Write(GameRunner.DiagnosticsDirectory, gameDir, config, diag);
        }
        catch (Exception ex)
        {
            manifestPath = string.Empty;
            manifestWarnings.Add($"寫出執行配置清單失敗：{ex.Message}");
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
                GameRunner.WatchForever(diag, cts.Token, m => stdout.WriteLine($"  {m}"));
            }
            finally
            {
                Console.CancelKeyPress -= onCancel;
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
        string logPath = Path.Combine(outcome.OutputDirectory, "ckperf.log");
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
                    logFile = logPath,
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
            stdout.WriteLine($"  執行記錄  ckperf.log");
            stdout.WriteLine($"  配置清單  {RunManifest.FileName}");
            stdout.WriteLine($"  故障報告  ckcrash-*.txt（只有在真的發生例外時才會出現）");
            foreach (string w in allWarnings) stdout.WriteLine($"  ! {w}");
        }
        return ExitCodes.Success;
    }

    private static int HandleUnknown(string command, bool isJson, TextWriter stdout, TextWriter stderr)
    {
        string errMsg = Strings.Get("Error_InvalidArgs", $"未知的指令或參數 '{command}'");
        return OutputError(command, errMsg, ExitCodes.InvalidArgs, isJson, stdout, stderr);
    }
}
