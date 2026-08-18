using System.Text;
using System.Text.Json.Serialization;
using CKToolkit.Core.Perf;
using CKToolkit.I18n;

namespace CKToolkit.Core.Common;

/// <summary>
/// 各功能模組（Perf、Lang、Trainer）插入套用管線之標準介面 (SPEC.md §4)。
/// </summary>
public interface IPatchModule
{
    string ModuleId { get; }
    int Order { get; }

    void ApplyExe(ref byte[] exeBytes, ToolkitConfig config);
    void ApplyLauncher(ref byte[] launcherBytes, ToolkitConfig config);
    void ApplyDataPak(HmmPak pak, ToolkitConfig config);
    void ApplyLocalPak(HmmPak pak, ToolkitConfig config);
    void ApplyVxSettings(IniFile ini, ToolkitConfig config, IReadOnlyList<string>? availableResolutions, List<string>? warnings = null);
}

/// <summary>
/// 個別目標檔案修補套用結果資訊 (SPEC.md §10)。
/// </summary>
public sealed class FileApplyResult
{
    [JsonPropertyName("file")]
    public string File { get; set; } = string.Empty;

    [JsonPropertyName("written")]
    public bool Written { get; set; }

    [JsonPropertyName("layered")]
    public List<string> Layered { get; set; } = [];
}

/// <summary>
/// 完整套用報告 (SPEC.md §10)。
/// </summary>
public sealed class ApplyReport
{
    [JsonPropertyName("gameDir")]
    public string GameDir { get; set; } = string.Empty;

    [JsonPropertyName("filesWritten")]
    public List<string> FilesWritten { get; set; } = [];

    [JsonPropertyName("files")]
    public Dictionary<string, FileApplyResult> Files { get; set; } = new();
}

/// <summary>
/// 個別檔案還原結果資訊 (SPEC.md §10)。
/// </summary>
public sealed class FileRestoreResult
{
    [JsonPropertyName("file")]
    public string File { get; set; } = string.Empty;

    [JsonPropertyName("restored")]
    public bool Restored { get; set; }

    [JsonPropertyName("state")]
    public string State { get; set; } = string.Empty;
}

/// <summary>
/// 還原所有檔案報告 (SPEC.md §10)。
/// </summary>
public sealed class RestoreReport
{
    [JsonPropertyName("gameDir")]
    public string GameDir { get; set; } = string.Empty;

    [JsonPropertyName("restoredFiles")]
    public List<string> RestoredFiles { get; set; } = [];

    [JsonPropertyName("files")]
    public Dictionary<string, FileRestoreResult> Files { get; set; } = new();

    [JsonIgnore]
    public int Count => RestoredFiles.Count;
}

/// <summary>
/// 個別檔案驗證資訊 (SPEC.md §10)。
/// </summary>
public sealed class FileVerificationInfo
{
    [JsonPropertyName("file")]
    public string File { get; set; } = string.Empty;

    [JsonPropertyName("state")]
    public string State { get; set; } = string.Empty;

    [JsonPropertyName("isVanilla")]
    public bool IsVanilla { get; set; }

    [JsonPropertyName("isPatched")]
    public bool IsPatched { get; set; }

    [JsonPropertyName("isUnrecognised")]
    public bool IsUnrecognised { get; set; }

    [JsonPropertyName("appliedPatches")]
    public List<string> AppliedPatches { get; set; } = [];

    [JsonPropertyName("expectedPatches")]
    public List<string> ExpectedPatches { get; set; } = [];

    [JsonPropertyName("matchesConfig")]
    public bool MatchesConfig { get; set; }
}

/// <summary>
/// 驗證報告資料結構 (SPEC.md §10)。
/// </summary>
public sealed class VerificationReport
{
    [JsonPropertyName("gameFound")]
    public bool GameFound { get; set; }

    [JsonPropertyName("gameDir")]
    public string GameDir { get; set; } = string.Empty;

    [JsonPropertyName("allMatchesConfig")]
    public bool AllMatchesConfig => Files.Values.All(f => f.MatchesConfig);

    [JsonPropertyName("allRecognised")]
    public bool AllRecognised => Files.Values.All(f => !f.IsUnrecognised);

    [JsonPropertyName("appliedPatches")]
    public List<string> AppliedPatches { get; set; } = [];

    [JsonPropertyName("files")]
    public Dictionary<string, FileVerificationInfo> Files { get; set; } = new();
}

/// <summary>
/// 統一套用管線 (SPEC.md §4, AGENTS.md §2.1-2.3)。
///
/// 關鍵紀律：
///   1. 嚴格依序：Exe -> Launcher -> data.pak -> local.pak -> vxSettings.ini。
///   2. 每個檔案「讀取現行檔案 -> 正規化反轉回原版 -> 依序疊加啟用的修改 -> 僅在內容有變動時寫入一次」。
///   3. 寫檔一律「先寫 .cktmp 再取代」，中途失敗不留半殘檔案。
///   4. 內容未變更的檔案嚴格略過寫入（如未安裝語言包時不重寫 4.8MB 的 local.pak）。
///   5. 若任何檔案無法辨識 (Unrecognised)，嚴格拒絕寫入並要求 Steam 驗證檔案完整性。
/// </summary>
public sealed class PatchPipeline
{
    private static readonly Encoding IniEncoding;

    static PatchPipeline()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        IniEncoding = Encoding.GetEncoding(1252);
    }

    private readonly List<IPatchModule> _modules = new();

    public IReadOnlyList<IPatchModule> Modules => _modules;

    public void RegisterModule(IPatchModule module)
    {
        if (_modules.Any(m => string.Equals(m.ModuleId, module.ModuleId, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"模組 {module.ModuleId} 已經註冊過");
        }
        _modules.Add(module);
        _modules.Sort((a, b) => a.Order.CompareTo(b.Order));
    }

    public static PatchPipeline CreateDefault()
    {
        var pipeline = new PatchPipeline();
        pipeline.RegisterModule(new PerfModule());
        return pipeline;
    }

    /// <summary>
    /// 執行完整修補套用流程。
    /// 每個目標檔案：檢查狀態 -> 若無法辨識則拒絕並終止 -> 正規化 -> 疊加修改 -> 若內容改變則原子寫入。
    /// </summary>
    public Result<ApplyReport> ApplyAll(string gameDir, ToolkitConfig config)
    {
        if (GamePaths.IsGameRunning())
        {
            return Result<ApplyReport>.Fail(Strings.Get("Error_GameRunning"), ExitCodes.FileLocked);
        }

        if (!GamePaths.IsGameDir(gameDir))
        {
            return Result<ApplyReport>.Fail(Strings.Get("Error_GameNotFound"), ExitCodes.GameNotFound);
        }

        // 1. 事前檢查：先讀取並檢查所有目標檔案是否均存在且可辨識（若有任何檔案無法辨識，嚴格零寫入）
        var rawFiles = new Dictionary<GameFile, byte[]>();
        foreach (GameFile f in Enum.GetValues<GameFile>())
        {
            string fileName = PatchState.GetFileName(f);
            string filePath = Path.Combine(gameDir, fileName);

            if (!File.Exists(filePath))
            {
                return Result<ApplyReport>.Fail(
                    Strings.Get("Error_GameNotFound") + $" ({filePath})",
                    ExitCodes.GameNotFound);
            }

            byte[] liveBytes;
            try
            {
                liveBytes = File.ReadAllBytes(filePath);
            }
            catch (Exception ex)
            {
                return Result<ApplyReport>.Fail(
                    Strings.Get("Error_FileLocked", fileName) + $" ({ex.Message})",
                    ExitCodes.FileLocked);
            }

            var state = PatchState.Inspect(f, liveBytes);
            if (state.IsUnrecognised)
            {
                return Result<ApplyReport>.Fail(
                    Strings.Get("Error_UnrecognisedFileNeedsSteamVerify", fileName),
                    ExitCodes.BackupMissingNeedsSteamVerify);
            }

            rawFiles[f] = liveBytes;
        }

        var report = new ApplyReport { GameDir = gameDir };
        var writtenFiles = new List<string>();
        var warnings = new List<string>();

        // 2. Exe 重建與條件寫入
        {
            byte[] liveBytes = rawFiles[GameFile.Exe];
            var normRes = PatchState.Normalise(GameFile.Exe, liveBytes);
            if (!normRes.Success) return Result<ApplyReport>.Fail(normRes.ErrorMessage!, normRes.ExitCode);

            byte[] exeBytes = normRes.Value!;
            var exeLayered = new List<string>();
            if (config.Perf.Laa) exeLayered.Add("laa");
            if (config.Perf.VideoFix) exeLayered.Add("video_fix");
            if (config.Perf.Hires >= 1600) exeLayered.Add($"hires_zoom ({config.Perf.Hires})");
            if (config.Perf.KeepRes) exeLayered.Add("res_writeback");

            foreach (var mod in _modules)
            {
                mod.ApplyExe(ref exeBytes, config);
            }

            if (!exeBytes.AsSpan().SequenceEqual(liveBytes))
            {
                var writeRes = WriteAtomic(GamePaths.GetExePath(gameDir), exeBytes, GamePaths.ExeFileName);
                if (!writeRes.Success)
                {
                    report.Files[GamePaths.ExeFileName] = new FileApplyResult { File = GamePaths.ExeFileName, Written = false, Layered = exeLayered };
                    report.FilesWritten = writtenFiles;
                    string err = writtenFiles.Count > 0
                        ? Strings.Get("Error_ApplyPartialFailure", GamePaths.ExeFileName, string.Join(", ", writtenFiles))
                        : writeRes.ErrorMessage!;
                    return Result<ApplyReport>.Fail(err, writeRes.ExitCode);
                }
                writtenFiles.Add(GamePaths.ExeFileName);
                report.Files[GamePaths.ExeFileName] = new FileApplyResult { File = GamePaths.ExeFileName, Written = true, Layered = exeLayered };
            }
            else
            {
                report.Files[GamePaths.ExeFileName] = new FileApplyResult { File = GamePaths.ExeFileName, Written = false, Layered = exeLayered };
            }
        }

        // 3. Launcher 重建與條件寫入
        {
            byte[] liveBytes = rawFiles[GameFile.Launcher];
            var normRes = PatchState.Normalise(GameFile.Launcher, liveBytes);
            if (!normRes.Success) return Result<ApplyReport>.Fail(normRes.ErrorMessage!, normRes.ExitCode);

            byte[] launcherBytes = normRes.Value!;
            var launcherLayered = new List<string>();
            if (string.Equals(config.Perf.DesktopMode, "suppress", StringComparison.OrdinalIgnoreCase))
            {
                launcherLayered.Add("launcher_display");
            }
            else if (string.Equals(config.Perf.DesktopMode, "autoSwitch", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(config.Perf.Resolution))
            {
                launcherLayered.Add($"launcher_mode_table ({config.Perf.Resolution})");
            }

            foreach (var mod in _modules)
            {
                mod.ApplyLauncher(ref launcherBytes, config);
            }

            if (!launcherBytes.AsSpan().SequenceEqual(liveBytes))
            {
                var writeRes = WriteAtomic(GamePaths.GetLauncherPath(gameDir), launcherBytes, GamePaths.LauncherFileName);
                if (!writeRes.Success)
                {
                    report.Files[GamePaths.LauncherFileName] = new FileApplyResult { File = GamePaths.LauncherFileName, Written = false, Layered = launcherLayered };
                    report.FilesWritten = writtenFiles;
                    string err = Strings.Get("Error_ApplyPartialFailure", GamePaths.LauncherFileName, string.Join(", ", writtenFiles));
                    return Result<ApplyReport>.Fail(err, writeRes.ExitCode);
                }
                writtenFiles.Add(GamePaths.LauncherFileName);
                report.Files[GamePaths.LauncherFileName] = new FileApplyResult { File = GamePaths.LauncherFileName, Written = true, Layered = launcherLayered };
            }
            else
            {
                report.Files[GamePaths.LauncherFileName] = new FileApplyResult { File = GamePaths.LauncherFileName, Written = false, Layered = launcherLayered };
            }
        }

        // 4. data.pak 重建與條件寫入
        IReadOnlyList<string>? availableResolutions = null;
        {
            byte[] liveBytes = rawFiles[GameFile.DataPak];
            var normRes = PatchState.Normalise(GameFile.DataPak, liveBytes);
            if (!normRes.Success) return Result<ApplyReport>.Fail(normRes.ErrorMessage!, normRes.ExitCode);

            HmmPak dataPak;
            try
            {
                dataPak = HmmPak.FromBytes(normRes.Value!);
            }
            catch (PakException ex)
            {
                return Result<ApplyReport>.Fail($"解析 data.pak 失敗：{ex.Message}", ExitCodes.GeneralFailure);
            }

            var dataPakLayered = new List<string>();
            if (config.Perf.AddRes is { Count: > 0 })
            {
                dataPakLayered.Add($"resolutions_append ({string.Join(", ", config.Perf.AddRes)})");
            }

            foreach (var mod in _modules)
            {
                mod.ApplyDataPak(dataPak, config);
            }

            availableResolutions = Resolutions.GetAvailableResolutionsList(dataPak);
            byte[] dataPakBytes = dataPak.ToBytes();

            if (!dataPakBytes.AsSpan().SequenceEqual(liveBytes))
            {
                var writeRes = WriteAtomic(GamePaths.GetDataPakPath(gameDir), dataPakBytes, GamePaths.DataPakFileName);
                if (!writeRes.Success)
                {
                    report.Files[GamePaths.DataPakFileName] = new FileApplyResult { File = GamePaths.DataPakFileName, Written = false, Layered = dataPakLayered };
                    report.FilesWritten = writtenFiles;
                    string err = Strings.Get("Error_ApplyPartialFailure", GamePaths.DataPakFileName, string.Join(", ", writtenFiles));
                    return Result<ApplyReport>.Fail(err, writeRes.ExitCode);
                }
                writtenFiles.Add(GamePaths.DataPakFileName);
                report.Files[GamePaths.DataPakFileName] = new FileApplyResult { File = GamePaths.DataPakFileName, Written = true, Layered = dataPakLayered };
            }
            else
            {
                report.Files[GamePaths.DataPakFileName] = new FileApplyResult { File = GamePaths.DataPakFileName, Written = false, Layered = dataPakLayered };
            }
        }

        // 5. local.pak 重建與條件寫入（若無修改則絕對不重寫 4.8MB 檔案）
        {
            byte[] liveBytes = rawFiles[GameFile.LocalPak];
            var normRes = PatchState.Normalise(GameFile.LocalPak, liveBytes);
            if (!normRes.Success) return Result<ApplyReport>.Fail(normRes.ErrorMessage!, normRes.ExitCode);

            HmmPak localPak;
            try
            {
                localPak = HmmPak.FromBytes(normRes.Value!);
            }
            catch (PakException ex)
            {
                return Result<ApplyReport>.Fail($"解析 local.pak 失敗：{ex.Message}", ExitCodes.GeneralFailure);
            }

            var localPakLayered = new List<string>();
            foreach (var mod in _modules)
            {
                mod.ApplyLocalPak(localPak, config);
            }

            byte[] localPakBytes = localPak.ToBytes();

            if (!localPakBytes.AsSpan().SequenceEqual(liveBytes))
            {
                var writeRes = WriteAtomic(GamePaths.GetLocalPakPath(gameDir), localPakBytes, GamePaths.LocalPakFileName);
                if (!writeRes.Success)
                {
                    report.Files[GamePaths.LocalPakFileName] = new FileApplyResult { File = GamePaths.LocalPakFileName, Written = false, Layered = localPakLayered };
                    report.FilesWritten = writtenFiles;
                    string err = Strings.Get("Error_ApplyPartialFailure", GamePaths.LocalPakFileName, string.Join(", ", writtenFiles));
                    return Result<ApplyReport>.Fail(err, writeRes.ExitCode);
                }
                writtenFiles.Add(GamePaths.LocalPakFileName);
                report.Files[GamePaths.LocalPakFileName] = new FileApplyResult { File = GamePaths.LocalPakFileName, Written = true, Layered = localPakLayered };
            }
            else
            {
                // 略過未變更檔案之寫入
                report.Files[GamePaths.LocalPakFileName] = new FileApplyResult { File = GamePaths.LocalPakFileName, Written = false, Layered = localPakLayered };
            }
        }

        // 6. vxSettings.ini 重建與條件寫入
        {
            byte[] liveBytes = rawFiles[GameFile.VxSettings];
            var normRes = PatchState.Normalise(GameFile.VxSettings, liveBytes);
            if (!normRes.Success) return Result<ApplyReport>.Fail(normRes.ErrorMessage!, normRes.ExitCode);

            string vxText = IniEncoding.GetString(normRes.Value!);
            var ini = IniFile.FromText(vxText);

            var vxLayered = new List<string>();
            if (config.Perf.NoObjectAnimations) vxLayered.Add("no_object_animations");
            if (config.Perf.NoWaterAnimation) vxLayered.Add("no_water_animation");
            if (!string.IsNullOrWhiteSpace(config.Perf.Resolution)) vxLayered.Add($"resolution ({config.Perf.Resolution})");

            foreach (var mod in _modules)
            {
                mod.ApplyVxSettings(ini, config, availableResolutions, warnings);
            }

            byte[] iniBytes = IniEncoding.GetBytes(ini.ToText());

            if (!iniBytes.AsSpan().SequenceEqual(liveBytes))
            {
                var writeRes = WriteAtomic(GamePaths.GetVxSettingsPath(gameDir), iniBytes, GamePaths.VxSettingsFileName);
                if (!writeRes.Success)
                {
                    report.Files[GamePaths.VxSettingsFileName] = new FileApplyResult { File = GamePaths.VxSettingsFileName, Written = false, Layered = vxLayered };
                    report.FilesWritten = writtenFiles;
                    string err = Strings.Get("Error_ApplyPartialFailure", GamePaths.VxSettingsFileName, string.Join(", ", writtenFiles));
                    return Result<ApplyReport>.Fail(err, writeRes.ExitCode);
                }
                writtenFiles.Add(GamePaths.VxSettingsFileName);
                report.Files[GamePaths.VxSettingsFileName] = new FileApplyResult { File = GamePaths.VxSettingsFileName, Written = true, Layered = vxLayered };
            }
            else
            {
                report.Files[GamePaths.VxSettingsFileName] = new FileApplyResult { File = GamePaths.VxSettingsFileName, Written = false, Layered = vxLayered };
            }
        }

        report.FilesWritten = writtenFiles;

        if (config.Perf.Hires >= 2048)
        {
            warnings.Add(Strings.Get("Perf_HdCeilingWarning"));
        }

        return Result<ApplyReport>.Ok(report, warnings);
    }

    /// <summary>
    /// 將所有目標檔案正規化反轉回原版 (Vanilla)。
    /// 報告各檔案是否被還原；若任何檔案無法辨識則回傳失敗並要求 Steam 驗證。
    /// </summary>
    public Result<RestoreReport> RestoreAll(string gameDir)
    {
        if (GamePaths.IsGameRunning())
        {
            return Result<RestoreReport>.Fail(Strings.Get("Error_GameRunning"), ExitCodes.FileLocked);
        }

        if (!GamePaths.IsGameDir(gameDir))
        {
            return Result<RestoreReport>.Fail(Strings.Get("Error_GameNotFound"), ExitCodes.GameNotFound);
        }

        var report = new RestoreReport { GameDir = gameDir };
        var unrecognisedFiles = new List<string>();

        foreach (GameFile f in Enum.GetValues<GameFile>())
        {
            string fileName = PatchState.GetFileName(f);
            string filePath = Path.Combine(gameDir, fileName);

            if (!File.Exists(filePath))
            {
                unrecognisedFiles.Add(fileName);
                report.Files[fileName] = new FileRestoreResult
                {
                    File = fileName,
                    Restored = false,
                    State = "missing"
                };
                continue;
            }

            byte[] liveBytes;
            try
            {
                liveBytes = File.ReadAllBytes(filePath);
            }
            catch (Exception ex)
            {
                return Result<RestoreReport>.Fail(
                    Strings.Get("Error_FileLocked", fileName) + $" ({ex.Message})",
                    ExitCodes.FileLocked);
            }

            var state = PatchState.Inspect(f, liveBytes);
            if (state.IsUnrecognised)
            {
                unrecognisedFiles.Add(fileName);
                report.Files[fileName] = new FileRestoreResult
                {
                    File = fileName,
                    Restored = false,
                    State = "unrecognised"
                };
                continue;
            }

            if (state.IsVanilla)
            {
                report.Files[fileName] = new FileRestoreResult
                {
                    File = fileName,
                    Restored = false,
                    State = "vanilla"
                };
                continue;
            }

            // state.IsPatched: 正規化還原
            var normRes = PatchState.Normalise(f, liveBytes);
            if (!normRes.Success)
            {
                return Result<RestoreReport>.Fail(normRes.ErrorMessage!, normRes.ExitCode);
            }

            byte[] vanillaBytes = normRes.Value!;
            if (!vanillaBytes.AsSpan().SequenceEqual(liveBytes))
            {
                var writeRes = WriteAtomic(filePath, vanillaBytes, fileName);
                if (!writeRes.Success)
                {
                    return Result<RestoreReport>.Fail(writeRes.ErrorMessage!, writeRes.ExitCode);
                }

                report.RestoredFiles.Add(fileName);
                report.Files[fileName] = new FileRestoreResult
                {
                    File = fileName,
                    Restored = true,
                    State = "vanilla"
                };
            }
            else
            {
                report.Files[fileName] = new FileRestoreResult
                {
                    File = fileName,
                    Restored = false,
                    State = "vanilla"
                };
            }
        }

        if (unrecognisedFiles.Count > 0)
        {
            return Result<RestoreReport>.Fail(
                Strings.Get("Error_RestoreUnrecognisedFile", string.Join(", ", unrecognisedFiles)),
                ExitCodes.BackupMissingNeedsSteamVerify);
        }

        return Result<RestoreReport>.Ok(report);
    }

    /// <summary>
    /// 檢查遊戲檔案修補狀態與設定相符性（嚴格唯讀，零磁碟寫入）。
    /// </summary>
    public Result<VerificationReport> Verify(string gameDir, ToolkitConfig? config = null)
    {
        bool gameFound = GamePaths.IsGameDir(gameDir);
        var report = new VerificationReport
        {
            GameFound = gameFound,
            GameDir = gameDir
        };

        if (!gameFound)
        {
            return Result<VerificationReport>.Fail(
                Strings.Get("Error_GameNotFound"),
                ExitCodes.GameNotFound);
        }

        var effectiveConfig = config ?? ToolkitConfig.CreateDefault();
        var warnings = new List<string>();

        foreach (GameFile f in Enum.GetValues<GameFile>())
        {
            string fileName = PatchState.GetFileName(f);
            string targetPath = Path.Combine(gameDir, fileName);

            if (!File.Exists(targetPath))
            {
                report.Files[fileName] = new FileVerificationInfo
                {
                    File = fileName,
                    State = "unrecognised",
                    IsVanilla = false,
                    IsPatched = false,
                    IsUnrecognised = true,
                    AppliedPatches = [],
                    ExpectedPatches = GetExpectedPatchesForFile(f, effectiveConfig),
                    MatchesConfig = false
                };
                warnings.Add(Strings.Get("Warning_VerifyUnrecognisedFile", fileName));
                continue;
            }

            byte[] liveBytes;
            try
            {
                liveBytes = File.ReadAllBytes(targetPath);
            }
            catch
            {
                report.Files[fileName] = new FileVerificationInfo
                {
                    File = fileName,
                    State = "unrecognised",
                    IsVanilla = false,
                    IsPatched = false,
                    IsUnrecognised = true,
                    AppliedPatches = [],
                    ExpectedPatches = GetExpectedPatchesForFile(f, effectiveConfig),
                    MatchesConfig = false
                };
                continue;
            }

            var fileState = PatchState.Inspect(f, liveBytes);
            var appliedPatches = fileState.AppliedPatches.ToList();
            var expectedPatches = GetExpectedPatchesForFile(f, effectiveConfig);

            foreach (var p in appliedPatches)
            {
                if (!report.AppliedPatches.Contains(p))
                {
                    report.AppliedPatches.Add(p);
                }
            }

            bool matchesConfig = fileState.IsPatched || fileState.IsVanilla
                ? appliedPatches.OrderBy(x => x).SequenceEqual(expectedPatches.OrderBy(x => x))
                : false;

            string stateStr = fileState.Kind switch
            {
                FileStateKind.Vanilla => "vanilla",
                FileStateKind.PatchedByUs => "patched",
                _ => "unrecognised"
            };

            report.Files[fileName] = new FileVerificationInfo
            {
                File = fileName,
                State = stateStr,
                IsVanilla = fileState.IsVanilla,
                IsPatched = fileState.IsPatched,
                IsUnrecognised = fileState.IsUnrecognised,
                AppliedPatches = appliedPatches,
                ExpectedPatches = expectedPatches,
                MatchesConfig = matchesConfig
            };

            if (fileState.IsUnrecognised)
            {
                warnings.Add(Strings.Get("Warning_VerifyUnrecognisedFile", fileName));
            }
            else if (!matchesConfig)
            {
                warnings.Add(Strings.Get("Warning_VerifyConfigMismatch", fileName));
            }
        }

        return Result<VerificationReport>.Ok(report, warnings);
    }

    private static List<string> GetExpectedPatchesForFile(GameFile file, ToolkitConfig config)
    {
        var list = new List<string>();
        switch (file)
        {
            case GameFile.Exe:
                if (config.Perf.Laa) list.Add("laa");
                if (config.Perf.VideoFix) list.Add("video_fix");
                if (config.Perf.Hires >= 1600) list.Add("hires_zoom");
                if (config.Perf.KeepRes) list.Add("res_writeback");
                break;

            case GameFile.Launcher:
                if (string.Equals(config.Perf.DesktopMode, "suppress", StringComparison.OrdinalIgnoreCase))
                {
                    list.Add("launcher_display");
                }
                else if (string.Equals(config.Perf.DesktopMode, "autoSwitch", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(config.Perf.Resolution))
                {
                    list.Add("launcher_mode_table");
                }
                break;

            case GameFile.DataPak:
                if (config.Perf.AddRes is { Count: > 0 })
                {
                    list.Add("resolutions_append");
                }
                break;

            case GameFile.LocalPak:
                // Phase 3 語言包
                break;

            case GameFile.VxSettings:
                bool isCustomRes = false;
                if (!string.IsNullOrWhiteSpace(config.Perf.Resolution))
                {
                    string r = config.Perf.Resolution.Trim();
                    if (int.TryParse(r, out int idx))
                        isCustomRes = idx >= 4;
                    else
                        isCustomRes = !r.Equals("1024x768", StringComparison.OrdinalIgnoreCase) &&
                                      !r.Equals("1152x864", StringComparison.OrdinalIgnoreCase) &&
                                      !r.Equals("1280x1024", StringComparison.OrdinalIgnoreCase) &&
                                      !r.Equals("1600x1200", StringComparison.OrdinalIgnoreCase);
                }
                if (config.Perf.NoObjectAnimations || config.Perf.NoWaterAnimation || isCustomRes)
                {
                    list.Add("vxsettings_custom");
                }
                break;
        }
        return list;
    }

    // ---- 先寫 .cktmp 再取代之安全寫檔輔助 ----------------------------------

    private static Result WriteAtomic(string targetPath, byte[] data, string fileName)
    {
        string tempPath = targetPath + ".cktmp";
        try
        {
            File.WriteAllBytes(tempPath, data);
            File.Move(tempPath, targetPath, overwrite: true);
            return Result.Ok();
        }
        catch (IOException ex)
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
            return Result.Fail(
                Strings.Get("Error_FileLocked", fileName) + $" ({ex.Message})",
                ExitCodes.FileLocked);
        }
        catch (UnauthorizedAccessException ex)
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
            return Result.Fail(
                Strings.Get("Error_FileLocked", fileName) + $" ({ex.Message})",
                ExitCodes.FileLocked);
        }
    }
}
