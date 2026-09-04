using System.Text;
using System.Text.Json.Serialization;
using CKToolkit.Core.Lang;
using CKToolkit.Core.Perf;
using CKToolkit.Core.Trainer;
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
    void ApplyDataPak(HmmPak pak, ToolkitConfig config, List<string>? warnings = null);
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
    /// <summary>偵測到的遊戲組建指紋；null 表示尚未偵測（例如提前失敗）。</summary>
    [JsonPropertyName("gameBuild")]
    public GameBuildInfo? GameBuild { get; set; }

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
    /// <summary>偵測到的遊戲組建指紋；null 表示尚未偵測。</summary>
    [JsonPropertyName("gameBuild")]
    public GameBuildInfo? GameBuild { get; set; }

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
            throw new InvalidOperationException($"Internal: module '{module.ModuleId}' is already registered.");
        }
        _modules.Add(module);
        _modules.Sort((a, b) => a.Order.CompareTo(b.Order));
    }

    public static PatchPipeline CreateDefault()
    {
        var pipeline = new PatchPipeline();
        pipeline.RegisterModule(new TrainerModule());
        pipeline.RegisterModule(new PerfModule());
        pipeline.RegisterModule(new LangModule());
        return pipeline;
    }

    /// <summary>
    /// 執行完整修補套用流程。
    /// 每個目標檔案：檢查狀態 -> 若無法辨識則拒絕並終止 -> 正規化 -> 疊加修改 -> 若內容改變則原子寫入。
    /// </summary>
    public Result<ApplyReport> ApplyAll(string gameDir, ToolkitConfig config) =>
        ApplyAllStaged(gameDir, config);

    private Result<ApplyReport> ApplyAllStaged(string gameDir, ToolkitConfig config)
    {
        if (GamePaths.IsGameRunning(gameDir))
            return Result<ApplyReport>.Fail(Strings.Get("Error_GameRunning"), ExitCodes.FileLocked);

        if (!GamePaths.IsGameDir(gameDir))
            return Result<ApplyReport>.Fail(Strings.Get("Error_GameNotFound"), ExitCodes.GameNotFound);

        var readResult = ReadAndInspectTargets(gameDir);
        if (!readResult.Success)
            return Result<ApplyReport>.Fail(readResult.ErrorMessage!, readResult.ExitCode);

        Dictionary<GameFile, byte[]> rawFiles = readResult.Value!;
        Result<byte[]> normalisedExe;
        try
        {
            normalisedExe = PatchState.Normalise(GameFile.Exe, rawFiles[GameFile.Exe]);
        }
        catch (Exception ex)
        {
            return Result<ApplyReport>.Fail(
                Strings.Get("Error_PipelineTransformFailed", GamePaths.ExeFileName, ex.Message),
                ExitCodes.GeneralFailure);
        }
        if (!normalisedExe.Success)
            return Result<ApplyReport>.Fail(normalisedExe.ErrorMessage!, normalisedExe.ExitCode);

        GameBuildInfo build = GameVersion.Detect(normalisedExe.Value!);
        if (build.Id == GameBuild.Unknown)
        {
            return Result<ApplyReport>.Fail(
                Strings.Get(
                    "Error_UnknownGameBuild",
                    build.Build,
                    $"{DateTimeOffset.FromUnixTimeSeconds(GameVersion.KnownTimeDateStamp).UtcDateTime:yyyy-MM-dd HH:mm:ss}Z (0x{GameVersion.KnownTimeDateStamp:X8})"),
                ExitCodes.BackupMissingNeedsSteamVerify);
        }

        Result configValidation = ValidateApplyConfig(config);
        if (!configValidation.Success)
            return Result<ApplyReport>.Fail(configValidation.ErrorMessage!, configValidation.ExitCode);

        var normalisedFiles = new Dictionary<GameFile, byte[]>();
        foreach (GameFile file in Enum.GetValues<GameFile>())
        {
            Result<byte[]> normalised;
            try
            {
                normalised = PatchState.Normalise(file, rawFiles[file]);
            }
            catch (Exception ex)
            {
                return Result<ApplyReport>.Fail(
                    Strings.Get("Error_PipelineTransformFailed", PatchState.GetFileName(file), ex.Message),
                    ExitCodes.GeneralFailure);
            }

            if (!normalised.Success)
                return Result<ApplyReport>.Fail(normalised.ErrorMessage!, normalised.ExitCode);

            normalisedFiles[file] = normalised.Value!;
        }

        var warnings = new List<string>();
        var stagedFiles = new Dictionary<GameFile, byte[]>(normalisedFiles);
        var layered = new Dictionary<GameFile, List<string>>
        {
            [GameFile.Exe] = [],
            [GameFile.Launcher] = [],
            [GameFile.DataPak] = [],
            [GameFile.LocalPak] = [],
            [GameFile.VxSettings] = []
        };

        byte[] exeBytes = stagedFiles[GameFile.Exe];
        if (config.Perf.Laa) layered[GameFile.Exe].Add("laa");
        if (config.Perf.VideoFix) layered[GameFile.Exe].Add("video_fix");
        if (config.Perf.Hires >= 1600) layered[GameFile.Exe].Add($"hires_zoom ({config.Perf.Hires})");
        if (config.Perf.KeepRes) layered[GameFile.Exe].Add("res_writeback");
        if (ScopedTweakPatch.HasSupportedLegacyPayload(config.Trainer))
            layered[GameFile.Exe].Add("scoped_tweaks");
        if (config.Trainer.Enabled && config.Trainer.SupportsFilePatch && config.Trainer.NumpadKeys) layered[GameFile.Exe].Add("key_map");
        foreach (IPatchModule module in _modules)
        {
            try
            {
                module.ApplyExe(ref exeBytes, config);
            }
            catch (Exception ex)
            {
                return ModuleFailure<ApplyReport>(module, GamePaths.ExeFileName, ex);
            }
        }
        stagedFiles[GameFile.Exe] = exeBytes;

        byte[] launcherBytes = stagedFiles[GameFile.Launcher];
        if (config.Perf.DesktopMode.Equals("suppress", StringComparison.OrdinalIgnoreCase))
            layered[GameFile.Launcher].Add("launcher_display");
        else if (config.Perf.DesktopMode.Equals("autoSwitch", StringComparison.OrdinalIgnoreCase))
            layered[GameFile.Launcher].Add($"launcher_mode_table ({config.Perf.Resolution})");
        foreach (IPatchModule module in _modules)
        {
            try
            {
                module.ApplyLauncher(ref launcherBytes, config);
            }
            catch (Exception ex)
            {
                return ModuleFailure<ApplyReport>(module, GamePaths.LauncherFileName, ex);
            }
        }
        stagedFiles[GameFile.Launcher] = launcherBytes;

        HmmPak dataPak;
        try
        {
            dataPak = HmmPak.FromBytes(stagedFiles[GameFile.DataPak]);
        }
        catch (Exception ex)
        {
            return Result<ApplyReport>.Fail(
                Strings.Get("Error_PipelineTransformFailed", GamePaths.DataPakFileName, ex.Message),
                ExitCodes.GeneralFailure);
        }

        bool hasCustomRes =
            (config.Perf.Hires >= 1600 &&
             !Resolutions.StockResolutions.Any(s => $"{s.Width}x{s.Height}".Equals(config.Perf.Resolution, StringComparison.OrdinalIgnoreCase))) ||
            config.Perf.AddRes.Count > 0;
        if (hasCustomRes) layered[GameFile.DataPak].Add("resolutions_append");
        if (TrainerHasDataPakPayload(config)) layered[GameFile.DataPak].Add("trainer_marker");
        foreach (IPatchModule module in _modules)
        {
            try
            {
                module.ApplyDataPak(dataPak, config, warnings);
            }
            catch (Exception ex)
            {
                return ModuleFailure<ApplyReport>(module, GamePaths.DataPakFileName, ex);
            }
        }
        IReadOnlyList<string> availableResolutions = Resolutions.GetAvailableResolutionsList(dataPak);
        try
        {
            stagedFiles[GameFile.DataPak] = dataPak.ToBytes();
        }
        catch (Exception ex)
        {
            return Result<ApplyReport>.Fail(
                Strings.Get("Error_PipelineTransformFailed", GamePaths.DataPakFileName, ex.Message),
                ExitCodes.GeneralFailure);
        }

        // 沒有任何模組要動 local.pak 時，連解析都不做。
        // HmmPak 的 FromBytes -> ToBytes 往返不保證位元組恆等（重建時的版面配置
        // 與原檔的死空間不同），所以白繞一圈會讓 4.8MB 的檔案被無謂重寫。
        // 正規化結果仍留在 stagedFiles，因此移除先前語言包的路徑照樣會寫入。
        if (!string.IsNullOrWhiteSpace(config.Lang.Pack))
        {
            layered[GameFile.LocalPak].Add($"langpack ({config.Lang.Pack})");
            HmmPak localPak;
            try
            {
                localPak = HmmPak.FromBytes(stagedFiles[GameFile.LocalPak]);
            }
            catch (Exception ex)
            {
                return Result<ApplyReport>.Fail(
                    Strings.Get("Error_PipelineTransformFailed", GamePaths.LocalPakFileName, ex.Message),
                    ExitCodes.GeneralFailure);
            }

            foreach (IPatchModule module in _modules)
            {
                try
                {
                    module.ApplyLocalPak(localPak, config);
                }
                catch (Exception ex)
                {
                    return ModuleFailure<ApplyReport>(module, GamePaths.LocalPakFileName, ex);
                }
            }

            try
            {
                stagedFiles[GameFile.LocalPak] = localPak.ToBytes();
            }
            catch (Exception ex)
            {
                return Result<ApplyReport>.Fail(
                    Strings.Get("Error_PipelineTransformFailed", GamePaths.LocalPakFileName, ex.Message),
                    ExitCodes.GeneralFailure);
            }
        }

        IniFile ini;
        try
        {
            ini = IniFile.FromText(IniEncoding.GetString(stagedFiles[GameFile.VxSettings]));
        }
        catch (Exception ex)
        {
            return Result<ApplyReport>.Fail(
                Strings.Get("Error_PipelineTransformFailed", GamePaths.VxSettingsFileName, ex.Message),
                ExitCodes.GeneralFailure);
        }

        if (config.Perf.NoObjectAnimations) layered[GameFile.VxSettings].Add("no_object_animations");
        if (config.Perf.NoWaterAnimation) layered[GameFile.VxSettings].Add("no_water_animation");
        layered[GameFile.VxSettings].Add($"resolution ({config.Perf.Resolution})");
        if (!string.IsNullOrWhiteSpace(config.Lang.Pack))
            layered[GameFile.VxSettings].Add($"lang_default ({PackLoader.ResolveGameLangIdentity(config.Lang.Pack).Key})");
        foreach (IPatchModule module in _modules)
        {
            try
            {
                module.ApplyVxSettings(ini, config, availableResolutions, warnings);
            }
            catch (Exception ex)
            {
                return ModuleFailure<ApplyReport>(module, GamePaths.VxSettingsFileName, ex);
            }
        }
        stagedFiles[GameFile.VxSettings] = IniEncoding.GetBytes(ini.ToText());

        var report = new ApplyReport { GameDir = gameDir, GameBuild = build };
        foreach (GameFile file in Enum.GetValues<GameFile>())
        {
            string fileName = PatchState.GetFileName(file);
            bool changed = !stagedFiles[file].AsSpan().SequenceEqual(rawFiles[file]);
            report.Files[fileName] = new FileApplyResult
            {
                File = fileName,
                Written = false,
                Layered = layered[file]
            };

            if (!changed) continue;

            Result writeResult = WriteAtomic(Path.Combine(gameDir, fileName), stagedFiles[file], fileName);
            if (!writeResult.Success)
            {
                string error = report.FilesWritten.Count == 0
                    ? writeResult.ErrorMessage!
                    : Strings.Get("Error_ApplyPartialFailure", fileName, string.Join(", ", report.FilesWritten));
                return Result<ApplyReport>.Fail(error, writeResult.ExitCode);
            }

            report.Files[fileName].Written = true;
            report.FilesWritten.Add(fileName);
        }

        return Result<ApplyReport>.Ok(report, warnings);
    }

    private static Result<Dictionary<GameFile, byte[]>> ReadAndInspectTargets(string gameDir)
    {
        var files = new Dictionary<GameFile, byte[]>();
        foreach (GameFile file in Enum.GetValues<GameFile>())
        {
            string fileName = PatchState.GetFileName(file);
            string path = Path.Combine(gameDir, fileName);
            if (!File.Exists(path))
            {
                return Result<Dictionary<GameFile, byte[]>>.Fail(
                    Strings.Get("Error_GameNotFound") + $" ({path})",
                    ExitCodes.GameNotFound);
            }

            byte[] bytes;
            try
            {
                bytes = File.ReadAllBytes(path);
            }
            catch (Exception ex)
            {
                return Result<Dictionary<GameFile, byte[]>>.Fail(
                    Strings.Get("Error_FileLocked", fileName) + $" ({ex.Message})",
                    ExitCodes.FileLocked);
            }

            FileState state;
            try
            {
                state = PatchState.Inspect(file, bytes);
            }
            catch (Exception ex)
            {
                return Result<Dictionary<GameFile, byte[]>>.Fail(
                    Strings.Get("Error_PipelineTransformFailed", fileName, ex.Message),
                    ExitCodes.GeneralFailure);
            }

            if (state.IsUnrecognised)
            {
                return Result<Dictionary<GameFile, byte[]>>.Fail(
                    Strings.Get("Error_UnrecognisedFileNeedsSteamVerify", fileName),
                    ExitCodes.BackupMissingNeedsSteamVerify);
            }

            files[file] = bytes;
        }

        return Result<Dictionary<GameFile, byte[]>>.Ok(files);
    }

    private static Result ValidateApplyConfig(ToolkitConfig config)
    {
        if (config.LoadError is not null)
            return Result.Fail(config.LoadError, ExitCodes.InvalidArgs);
        if (config.Perf is null || config.Lang is null || config.Trainer is null)
            return Result.Fail(Strings.Get("Error_InvalidConfig"), ExitCodes.InvalidArgs);

        Result resolutionResult = ValidateSurface(config.Perf.Resolution);
        if (!resolutionResult.Success) return resolutionResult;

        var (_, surfaceHeight) = PerfModule.ParseDimensions(config.Perf.Resolution, 0, 0);
        if (config.Perf.Hires != 0)
        {
            if (config.Perf.Hires < 1600 ||
                !CellGridPatch.IsSurfaceSupported(config.Perf.Hires, surfaceHeight))
            {
                return Result.Fail(
                    Strings.Get(
                        "Error_ResolutionExceedsGridCeiling",
                        config.Perf.Hires,
                        CellGridPatch.MaxSurfaceWidth,
                        CellGridPatch.MaxSurfaceHeight),
                    ExitCodes.InvalidArgs);
            }
        }

        if (config.Perf.AddRes is null)
            return Result.Fail(Strings.Get("Error_InvalidConfig"), ExitCodes.InvalidArgs);
        foreach (string resolution in config.Perf.AddRes)
        {
            Result extraResult = ValidateSurface(resolution);
            if (!extraResult.Success) return extraResult;
        }

        if (string.IsNullOrWhiteSpace(config.Perf.DesktopMode) ||
            (!config.Perf.DesktopMode.Equals("suppress", StringComparison.OrdinalIgnoreCase) &&
             !config.Perf.DesktopMode.Equals("autoSwitch", StringComparison.OrdinalIgnoreCase) &&
             !config.Perf.DesktopMode.Equals("stock", StringComparison.OrdinalIgnoreCase)))
        {
            return Result.Fail(
                Strings.Get("Error_InvalidDesktopMode", config.Perf.DesktopMode ?? string.Empty),
                ExitCodes.InvalidArgs);
        }

        if (config.Trainer.Cheats is null || config.Trainer.Tweaks is null ||
            config.Trainer.ScopedTweaks is null)
            return Result.Fail(Strings.Get("Error_InvalidConfig"), ExitCodes.InvalidArgs);

        foreach (CheatConfig cheat in config.Trainer.Cheats)
        {
            if (cheat is null || !Cheats.ById.ContainsKey(cheat.Id))
            {
                return Result.Fail(
                    Strings.Get("Error_TrainerUnknownCheat", cheat?.Id ?? string.Empty),
                    ExitCodes.InvalidArgs);
            }
        }

        foreach (var (id, value) in config.Trainer.Tweaks.ToList())
        {
            if (Tweaks.Retired.Contains(id))
                continue;
            if (!Tweaks.ById.TryGetValue(id, out Tweak? tweak))
                return Result.Fail(Strings.Get("Error_TrainerUnknownTweak", id), ExitCodes.InvalidArgs);
            if (value < tweak.Minimum || value > tweak.Maximum)
            {
                config.Trainer.Tweaks[id] = Math.Clamp(value, tweak.Minimum, tweak.Maximum);
            }
        }

        foreach (var (id, values) in config.Trainer.ScopedTweaks.ToList())
        {
            if (Tweaks.Retired.Contains(id))
                continue;
            if (!Tweaks.ById.TryGetValue(id, out Tweak? tweak))
                return Result.Fail(Strings.Get("Error_TrainerUnknownTweak", id), ExitCodes.InvalidArgs);
            if (!ScopedTweakPatch.IsSupportedScopedTweakId(id))
            {
                return Result.Fail(
                    Strings.Get("Error_TrainerScopedTweakUnsupported", id),
                    ExitCodes.InvalidArgs);
            }
            if (values is null)
                return Result.Fail(Strings.Get("Error_InvalidConfig"), ExitCodes.InvalidArgs);

            IReadOnlyList<string> allowedScopes = ScopedTweakPatch.GetSupportedScopes(id);
            foreach (var (scope, value) in values.ToList())
            {
                if (!allowedScopes.Contains(scope, StringComparer.Ordinal))
                {
                    return Result.Fail(
                        Strings.Get("Error_TrainerScopedTweakUnknownScope", id, scope),
                        ExitCodes.InvalidArgs);
                }
                if (value < tweak.Minimum || value > tweak.Maximum)
                {
                    values[scope] = Math.Clamp(value, tweak.Minimum, tweak.Maximum);
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(config.Lang.Pack))
        {
            string packId = config.Lang.Pack.Trim();
            if (packId.Equals("chinese", StringComparison.OrdinalIgnoreCase)) packId = "zh-TW";

            try
            {
                Dictionary<string, LanguagePack> packs = PackLoader.DiscoverAll();
                if (!packs.ContainsKey(packId))
                {
                    Result<LanguagePack> embedded = PackLoader.LoadEmbeddedPack(packId);
                    if (!embedded.Success || embedded.Value is null)
                    {
                        return Result.Fail(
                            Strings.Get("Error_LanguagePackUnavailable", packId),
                            ExitCodes.InvalidArgs);
                    }
                }
            }
            catch
            {
                return Result.Fail(
                    Strings.Get("Error_LanguagePackUnavailable", packId),
                    ExitCodes.InvalidArgs);
            }
        }

        return Result.Ok();
    }

    private static Result ValidateSurface(string? value)
    {
        var (width, height) = PerfModule.ParseDimensions(value ?? string.Empty, 0, 0);
        if (CellGridPatch.IsSurfaceSupported(width, height)) return Result.Ok();

        return Result.Fail(
            Strings.Get(
                "Error_ResolutionExceedsGridCeiling",
                value ?? string.Empty,
                CellGridPatch.MaxSurfaceWidth,
                CellGridPatch.MaxSurfaceHeight),
            ExitCodes.InvalidArgs);
    }

    private static Result<T> ModuleFailure<T>(IPatchModule module, string fileName, Exception exception) =>
        Result<T>.Fail(
            Strings.Get("Error_ModuleApplyFailed", module.ModuleId, fileName, exception.Message),
            ExitCodes.GeneralFailure);

    /// <summary>
    /// 將所有目標檔案正規化反轉回原版 (Vanilla)。
    /// 報告各檔案是否被還原；若任何檔案無法辨識則回傳失敗並要求 Steam 驗證。
    /// </summary>
    public Result<RestoreReport> RestoreAll(string gameDir) =>
        RestoreAllStaged(gameDir);

    private static Result<RestoreReport> RestoreAllStaged(string gameDir)
    {
        if (GamePaths.IsGameRunning(gameDir))
            return Result<RestoreReport>.Fail(Strings.Get("Error_GameRunning"), ExitCodes.FileLocked);

        if (!GamePaths.IsGameDir(gameDir))
            return Result<RestoreReport>.Fail(Strings.Get("Error_GameNotFound"), ExitCodes.GameNotFound);

        var readResult = ReadAndInspectTargets(gameDir);
        if (!readResult.Success)
            return Result<RestoreReport>.Fail(readResult.ErrorMessage!, readResult.ExitCode);

        Dictionary<GameFile, byte[]> rawFiles = readResult.Value!;
        var stagedFiles = new Dictionary<GameFile, byte[]>();
        foreach (GameFile file in Enum.GetValues<GameFile>())
        {
            Result<byte[]> normalised;
            try
            {
                normalised = PatchState.Normalise(file, rawFiles[file]);
            }
            catch (Exception ex)
            {
                return Result<RestoreReport>.Fail(
                    Strings.Get("Error_PipelineTransformFailed", PatchState.GetFileName(file), ex.Message),
                    ExitCodes.GeneralFailure);
            }

            if (!normalised.Success)
                return Result<RestoreReport>.Fail(normalised.ErrorMessage!, normalised.ExitCode);

            stagedFiles[file] = normalised.Value!;
        }

        var report = new RestoreReport { GameDir = gameDir };
        foreach (GameFile file in Enum.GetValues<GameFile>())
        {
            string fileName = PatchState.GetFileName(file);
            bool changed = !stagedFiles[file].AsSpan().SequenceEqual(rawFiles[file]);
            report.Files[fileName] = new FileRestoreResult
            {
                File = fileName,
                Restored = false,
                State = "vanilla"
            };

            if (!changed) continue;

            Result writeResult = WriteAtomic(Path.Combine(gameDir, fileName), stagedFiles[file], fileName);
            if (!writeResult.Success)
                return Result<RestoreReport>.Fail(writeResult.ErrorMessage!, writeResult.ExitCode);

            report.Files[fileName].Restored = true;
            report.RestoredFiles.Add(fileName);
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

            if (f == GameFile.Exe)
            {
                // 用正規化後的位元組偵測，HiRes 附加的 .ckhr 節區才不會影響 SizeOfImage。
                // 正規化失敗就退回用現行位元組，至少時間戳仍然讀得到。
                var normalised = PatchState.Normalise(f, liveBytes);
                var build = GameVersion.Detect(normalised.Success ? normalised.Value! : liveBytes);
                report.GameBuild = build;
                GameVersion.WarnIfUnknown(build, warnings);
            }

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

            if (matchesConfig && f == GameFile.Exe)
            {
                matchesConfig = ScopedTweakPatch.MatchesLegacySettings(liveBytes, effectiveConfig.Trainer);
            }
            else if (matchesConfig && f == GameFile.DataPak)
            {
                matchesConfig = TrainerMarkerMatchesConfig(liveBytes, effectiveConfig);
            }

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
                if (config.Perf.Hires >= 1600)
                {
                    // PerfModule.ApplyExe 在同一個條件下會連 CellGridPatch 一起套用，
                    // 期望清單必須跟著列出來，否則 verify 會對每一份高解析度設定
                    // 都回報「exe 與設定不符」。
                    list.Add("hires_zoom");
                    list.Add("cell_grid");
                }
                if (config.Perf.KeepRes) list.Add("res_writeback");
                if (ScopedTweakPatch.HasSupportedLegacyPayload(config.Trainer)) list.Add("scoped_tweaks");
                if (config.Trainer.Enabled && config.Trainer.SupportsFilePatch && config.Trainer.NumpadKeys) list.Add("key_map");
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
                bool hasCustomDataRes = (config.Perf.Hires >= 1600 && !Resolutions.StockResolutions.Any(s => $"{s.Width}x{s.Height}".Equals(config.Perf.Resolution, StringComparison.OrdinalIgnoreCase))) || (config.Perf.AddRes is { Count: > 0 });
                if (hasCustomDataRes)
                {
                    list.Add("resolutions_append");
                }
                if (TrainerHasDataPakPayload(config)) list.Add("trainer_marker");
                break;

            case GameFile.LocalPak:
                if (!string.IsNullOrWhiteSpace(config.Lang.Pack))
                {
                    // PatchState 記錄的是 local.pak 裡真正存在的語系資料夾名，
                    // 所以期望值也必須用同一個來源，不能拿語言包 ID 湊。
                    list.Add($"langpack_{PackLoader.ResolveGameLangIdentity(config.Lang.Pack).Folder}");
                }
                break;

            case GameFile.VxSettings:
                bool isCustomRes = false;
                if (!string.IsNullOrWhiteSpace(config.Perf.Resolution))
                {
                    string r = config.Perf.Resolution.Trim();
                    if (int.TryParse(r, out int idx))
                        isCustomRes = idx != 3;
                    else
                        isCustomRes = !r.Equals("1600x1200", StringComparison.OrdinalIgnoreCase);
                }
                if (config.Perf.NoObjectAnimations || config.Perf.NoWaterAnimation || isCustomRes)
                {
                    list.Add("vxsettings_custom");
                }
                if (!string.IsNullOrWhiteSpace(config.Lang.Pack))
                {
                    list.Add($"lang_default ({PackLoader.ResolveGameLangIdentity(config.Lang.Pack).Key})");
                }
                break;
        }
        return list;
    }

    internal static bool TrainerHasDataPakPayload(ToolkitConfig config) =>
        (config.Trainer.Enabled &&
         ((config.Trainer.SupportsFilePatch && config.Trainer.Cheats.Any(c => c.Enabled)) ||
          config.Trainer.Tweaks.Any(kv =>
              !ScopedTweakPatch.ShouldRouteToScopedPatch(config.Trainer, kv.Key) &&
              Tweaks.ById.TryGetValue(kv.Key, out var tweak) && kv.Value != tweak.Default))) ||
        config.GameSettings.HasAnyModifications;

    internal static bool TrainerMarkerMatchesConfig(byte[] dataPakBytes, ToolkitConfig config)
    {
        bool expected = TrainerHasDataPakPayload(config);
        HmmPak pak;
        try
        {
            pak = HmmPak.FromBytes(dataPakBytes);
        }
        catch
        {
            return false;
        }

        TrainerMarker? marker = TrainerInstaller.ReadMarker(pak);
        if (!expected)
            return marker is null;
        if (marker is null)
            return false;

        var expectedCheats = (config.Trainer.Enabled && config.Trainer.SupportsFilePatch)
            ? config.Trainer.Cheats
                .Where(c => c.Enabled)
                .Select(c => c.Id)
                .ToList()
            : [];
        var expectedTweaks = config.Trainer.Enabled
            ? config.Trainer.Tweaks
                .Where(kv =>
                    !ScopedTweakPatch.ShouldRouteToScopedPatch(config.Trainer, kv.Key) &&
                    Tweaks.ById.TryGetValue(kv.Key, out var tweak) && kv.Value != tweak.Default)
                .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal)
            : new Dictionary<string, decimal>(StringComparer.Ordinal);

        var expectedGameSettings = new List<string>();
        if (config.GameSettings.AllowVikingLordHeroArmy) expectedGameSettings.Add("allow_viking_lord_army");
        if (config.GameSettings.AllowLiberatiHeroArmy) expectedGameSettings.Add("allow_liberati_army");

        return marker.Cheats.SequenceEqual(expectedCheats, StringComparer.Ordinal) &&
               marker.Tweaks.Count == expectedTweaks.Count &&
               marker.Tweaks.All(kv => expectedTweaks.TryGetValue(kv.Key, out decimal value) &&
                                       value == kv.Value) &&
               marker.GameSettings.SequenceEqual(expectedGameSettings, StringComparer.Ordinal);
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
