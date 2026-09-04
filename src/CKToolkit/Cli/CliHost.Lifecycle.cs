using System.Text.Json;
using CKToolkit.Core.Common;
using CKToolkit.I18n;

namespace CKToolkit.Cli;

public static partial class CliHost
{
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
        string versionStr = "1.0.5";
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
        GameBuildInfo? gameBuild = null;
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

            if (f == GameFile.Exe)
            {
                // 用正規化後的位元組偵測，HiRes 的 .ckhr 節區才不會影響 SizeOfImage。
                var normalised = PatchState.Normalise(f, liveBytes);
                gameBuild = GameVersion.Detect(normalised.Success ? normalised.Value! : liveBytes);
                GameVersion.WarnIfUnknown(gameBuild, warnings);
            }

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
            gameBuild,
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
            if (gameBuild is not null)
            {
                stdout.WriteLine(
                    $"{Strings.Get("Gui_Status_GameBuild")}: {gameBuild.Build}"
                    + (gameBuild.IsKnown ? " [OK]" : " [?]"));
            }
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
        if (config.LoadError is not null)
            return RejectCorruptConfig(commandName, config, isJson, stdout, stderr);
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

        // restore 只認得 --all；其餘 token 多半是打錯的選項，靜默忽略會讓代理程式
        // 以為自己下了某個限制條件，實際卻做了完整還原（ISSUE-065）。
        string? unknownRestoreOption = commandArgs
            .FirstOrDefault(a => !a.Equals("--all", StringComparison.OrdinalIgnoreCase));
        if (unknownRestoreOption is not null)
        {
            return OutputError("restore", Strings.Get("Error_UnknownOption", unknownRestoreOption),
                ExitCodes.InvalidArgs, isJson, stdout, stderr);
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
            if (report.GameBuild is not null)
            {
                stdout.WriteLine(
                    $"{Strings.Get("Gui_Status_GameBuild")}: {report.GameBuild.Build}"
                    + (report.GameBuild.IsKnown ? " [OK]" : " [?]"));
            }
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
}
