using System.Runtime.InteropServices;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using CKToolkit.Core.Common;
using CKToolkit.Core.Lang;
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
                if (langSubCmd == "export-template")
                {
                    return HandleLangExportTemplate(commands.Skip(2).ToList(), gameDirOverride, configPathOverride, isJson, stdout, stderr);
                }
                return OutputError("lang", Strings.Get("Error_InvalidArgs", $"未知的 lang 子指令 '{commands[1]}'"), ExitCodes.InvalidArgs, isJson, stdout, stderr);

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
        string templateLang = "GERMAN";

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

    private static int HandleUnknown(string command, bool isJson, TextWriter stdout, TextWriter stderr)
    {
        string errMsg = Strings.Get("Error_InvalidArgs", $"未知的指令或參數 '{command}'");
        return OutputError(command, errMsg, ExitCodes.InvalidArgs, isJson, stdout, stderr);
    }
}
