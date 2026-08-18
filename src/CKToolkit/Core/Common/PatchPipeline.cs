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
    void ApplyVxSettings(IniFile ini, ToolkitConfig config, IReadOnlyList<string>? availableResolutions);
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
/// 個別檔案驗證資訊 (SPEC.md §10)。
/// </summary>
public sealed class FileVerificationInfo
{
    [JsonPropertyName("file")]
    public string File { get; set; } = string.Empty;

    [JsonPropertyName("hasBackup")]
    public bool HasBackup { get; set; }

    [JsonPropertyName("backupProvenance")]
    public BackupProvenance? BackupProvenance { get; set; }

    [JsonPropertyName("pristineState")]
    public PristineState PristineState { get; set; } = PristineState.Unknown;

    [JsonPropertyName("isPristine")]
    public bool IsPristine => PristineState == PristineState.Pristine;

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

    [JsonPropertyName("allBackupsPresent")]
    public bool AllBackupsPresent => Files.Values.All(f => f.HasBackup);

    [JsonPropertyName("allMatchesConfig")]
    public bool AllMatchesConfig => Files.Values.All(f => f.MatchesConfig);

    [JsonPropertyName("hasBackup")]
    public Dictionary<GameFile, bool> HasBackup { get; set; } = new();

    [JsonPropertyName("backupProvenances")]
    public Dictionary<GameFile, BackupProvenance?> BackupProvenances { get; set; } = new();

    [JsonPropertyName("pristineStates")]
    public Dictionary<GameFile, PristineState> PristineStates { get; set; } = new();

    [JsonPropertyName("isPristine")]
    public Dictionary<GameFile, bool> IsPristine { get; set; } = new();

    [JsonPropertyName("appliedPatches")]
    public List<string> AppliedPatches { get; set; } = [];

    [JsonPropertyName("files")]
    public Dictionary<string, FileVerificationInfo> Files { get; set; } = new();
}

/// <summary>
/// 統一套用管線 (SPEC.md §4)。
///
/// 關鍵紀律：
///   1. 嚴格依序：Exe -> Launcher -> data.pak -> local.pak -> vxSettings.ini。
///   2. 每個檔案一律「從 pristine 重建，依序疊加所有啟用的修改，最後只寫入一次」。
///   3. 寫檔一律「先寫 .cktmp 再取代」，中途失敗不留半殘檔案。
///   4. 寫入失敗（如遊戲正在執行）回傳 ExitCodes.FileLocked 而非拋出未處理例外。
/// </summary>
public sealed class PatchPipeline
{
    private static readonly Encoding IniEncoding;

    static PatchPipeline()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        IniEncoding = Encoding.GetEncoding(1252);
    }

    private readonly BackupManager _backupManager;
    private readonly List<IPatchModule> _modules = new();

    public PatchPipeline(BackupManager backupManager)
    {
        _backupManager = backupManager;
    }

    public BackupManager BackupManager => _backupManager;

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

    public static PatchPipeline CreateDefault(BackupManager? backupManager = null)
    {
        var bm = backupManager ?? new BackupManager();
        PerfModule.RegisterSignatures(bm);
        var pipeline = new PatchPipeline(bm);
        pipeline.RegisterModule(new PerfModule());
        return pipeline;
    }

    /// <summary>
    /// 執行完整修補套用流程。
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

        // 1. 確保五大目標檔案均有原版備份（若任何檔案基準建立失敗，嚴格不進行任何後續寫入）
        var backupRes = _backupManager.EnsureAllBackups(gameDir);
        if (!backupRes.Success)
        {
            return Result<ApplyReport>.Fail(backupRes.ErrorMessage!, backupRes.ExitCode, backupRes.Warnings);
        }

        var warnings = new List<string>(backupRes.Warnings);
        var report = new ApplyReport
        {
            GameDir = gameDir
        };
        var writtenFiles = new List<string>();

        // 2. Exe 重建與寫入
        var exeRes = _backupManager.ReadPristine(GameFile.Exe, gameDir);
        if (!exeRes.Success) return Result<ApplyReport>.Fail(exeRes.ErrorMessage!, exeRes.ExitCode, warnings);
        byte[] exeBytes = exeRes.Value!;

        var exeLayered = new List<string>();
        if (config.Perf.Laa) exeLayered.Add("laa");
        if (config.Perf.VideoFix) exeLayered.Add("video_fix");
        if (config.Perf.Hires > 0) exeLayered.Add($"hires_zoom ({config.Perf.Hires})");
        if (config.Perf.KeepRes) exeLayered.Add("res_writeback");

        foreach (var mod in _modules)
        {
            mod.ApplyExe(ref exeBytes, config);
        }

        var writeExeRes = WriteAtomic(GamePaths.GetExePath(gameDir), exeBytes, GamePaths.ExeFileName);
        if (!writeExeRes.Success)
        {
            report.Files[GamePaths.ExeFileName] = new FileApplyResult { File = GamePaths.ExeFileName, Written = false, Layered = exeLayered };
            report.FilesWritten = writtenFiles;
            string err = writtenFiles.Count > 0
                ? Strings.Get("Error_ApplyPartialFailure", GamePaths.ExeFileName, string.Join(", ", writtenFiles))
                : writeExeRes.ErrorMessage!;
            return Result<ApplyReport>.Fail(err, writeExeRes.ExitCode, warnings);
        }
        writtenFiles.Add(GamePaths.ExeFileName);
        report.Files[GamePaths.ExeFileName] = new FileApplyResult { File = GamePaths.ExeFileName, Written = true, Layered = exeLayered };

        // 3. Launcher 重建與寫入
        var launcherRes = _backupManager.ReadPristine(GameFile.Launcher, gameDir);
        if (!launcherRes.Success) return Result<ApplyReport>.Fail(launcherRes.ErrorMessage!, launcherRes.ExitCode, warnings);
        byte[] launcherBytes = launcherRes.Value!;

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

        var writeLauncherRes = WriteAtomic(GamePaths.GetLauncherPath(gameDir), launcherBytes, GamePaths.LauncherFileName);
        if (!writeLauncherRes.Success)
        {
            report.Files[GamePaths.LauncherFileName] = new FileApplyResult { File = GamePaths.LauncherFileName, Written = false, Layered = launcherLayered };
            report.FilesWritten = writtenFiles;
            string err = Strings.Get("Error_ApplyPartialFailure", GamePaths.LauncherFileName, string.Join(", ", writtenFiles));
            return Result<ApplyReport>.Fail(err, writeLauncherRes.ExitCode, warnings);
        }
        writtenFiles.Add(GamePaths.LauncherFileName);
        report.Files[GamePaths.LauncherFileName] = new FileApplyResult { File = GamePaths.LauncherFileName, Written = true, Layered = launcherLayered };

        // 4. data.pak 重建與寫入
        var dataPakRes = _backupManager.ReadPristine(GameFile.DataPak, gameDir);
        if (!dataPakRes.Success) return Result<ApplyReport>.Fail(dataPakRes.ErrorMessage!, dataPakRes.ExitCode, warnings);

        HmmPak dataPak;
        try
        {
            dataPak = HmmPak.FromBytes(dataPakRes.Value!);
        }
        catch (PakException ex)
        {
            return Result<ApplyReport>.Fail($"解析 pristine data.pak 失敗：{ex.Message}", ExitCodes.GeneralFailure, warnings);
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

        var availableResolutions = Resolutions.GetAvailableResolutionsList(dataPak);

        byte[] dataPakBytes = dataPak.ToBytes();
        var writeDataPakRes = WriteAtomic(GamePaths.GetDataPakPath(gameDir), dataPakBytes, GamePaths.DataPakFileName);
        if (!writeDataPakRes.Success)
        {
            report.Files[GamePaths.DataPakFileName] = new FileApplyResult { File = GamePaths.DataPakFileName, Written = false, Layered = dataPakLayered };
            report.FilesWritten = writtenFiles;
            string err = Strings.Get("Error_ApplyPartialFailure", GamePaths.DataPakFileName, string.Join(", ", writtenFiles));
            return Result<ApplyReport>.Fail(err, writeDataPakRes.ExitCode, warnings);
        }
        writtenFiles.Add(GamePaths.DataPakFileName);
        report.Files[GamePaths.DataPakFileName] = new FileApplyResult { File = GamePaths.DataPakFileName, Written = true, Layered = dataPakLayered };

        // 5. local.pak 重建與寫入
        var localPakRes = _backupManager.ReadPristine(GameFile.LocalPak, gameDir);
        if (!localPakRes.Success) return Result<ApplyReport>.Fail(localPakRes.ErrorMessage!, localPakRes.ExitCode, warnings);

        HmmPak localPak;
        try
        {
            localPak = HmmPak.FromBytes(localPakRes.Value!);
        }
        catch (PakException ex)
        {
            return Result<ApplyReport>.Fail($"解析 pristine local.pak 失敗：{ex.Message}", ExitCodes.GeneralFailure, warnings);
        }

        var localPakLayered = new List<string>();
        foreach (var mod in _modules)
        {
            mod.ApplyLocalPak(localPak, config);
        }

        byte[] localPakBytes = localPak.ToBytes();
        var writeLocalPakRes = WriteAtomic(GamePaths.GetLocalPakPath(gameDir), localPakBytes, GamePaths.LocalPakFileName);
        if (!writeLocalPakRes.Success)
        {
            report.Files[GamePaths.LocalPakFileName] = new FileApplyResult { File = GamePaths.LocalPakFileName, Written = false, Layered = localPakLayered };
            report.FilesWritten = writtenFiles;
            string err = Strings.Get("Error_ApplyPartialFailure", GamePaths.LocalPakFileName, string.Join(", ", writtenFiles));
            return Result<ApplyReport>.Fail(err, writeLocalPakRes.ExitCode, warnings);
        }
        writtenFiles.Add(GamePaths.LocalPakFileName);
        report.Files[GamePaths.LocalPakFileName] = new FileApplyResult { File = GamePaths.LocalPakFileName, Written = true, Layered = localPakLayered };

        // 6. vxSettings.ini 重建與寫入
        var vxRes = _backupManager.ReadPristine(GameFile.VxSettings, gameDir);
        if (!vxRes.Success) return Result<ApplyReport>.Fail(vxRes.ErrorMessage!, vxRes.ExitCode, warnings);

        string vxText = IniEncoding.GetString(vxRes.Value!);
        var ini = IniFile.FromText(vxText);

        var vxLayered = new List<string>();
        if (config.Perf.NoObjectAnimations) vxLayered.Add("no_object_animations");
        if (config.Perf.NoWaterAnimation) vxLayered.Add("no_water_animation");
        if (!string.IsNullOrWhiteSpace(config.Perf.Resolution)) vxLayered.Add($"resolution ({config.Perf.Resolution})");

        foreach (var mod in _modules)
        {
            mod.ApplyVxSettings(ini, config, availableResolutions);
        }

        byte[] iniBytes = IniEncoding.GetBytes(ini.ToText());
        var writeVxRes = WriteAtomic(GamePaths.GetVxSettingsPath(gameDir), iniBytes, GamePaths.VxSettingsFileName);
        if (!writeVxRes.Success)
        {
            report.Files[GamePaths.VxSettingsFileName] = new FileApplyResult { File = GamePaths.VxSettingsFileName, Written = false, Layered = vxLayered };
            report.FilesWritten = writtenFiles;
            string err = Strings.Get("Error_ApplyPartialFailure", GamePaths.VxSettingsFileName, string.Join(", ", writtenFiles));
            return Result<ApplyReport>.Fail(err, writeVxRes.ExitCode, warnings);
        }
        writtenFiles.Add(GamePaths.VxSettingsFileName);
        report.Files[GamePaths.VxSettingsFileName] = new FileApplyResult { File = GamePaths.VxSettingsFileName, Written = true, Layered = vxLayered };

        report.FilesWritten = writtenFiles;

        if (config.Perf.Hires >= 2048)
        {
            warnings.Add(Strings.Get("Perf_HdCeilingWarning"));
        }

        return Result<ApplyReport>.Ok(report, warnings);
    }

    /// <summary>
    /// 還原全部五個目標檔案為原版。
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

        return _backupManager.RestoreAll(gameDir);
    }

    /// <summary>
    /// 檢查備份完整性與當前修補狀態（嚴格唯讀，零磁碟寫入）。
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
            string fileName = BackupManager.GetFileName(f);
            bool hasBackup = _backupManager.HasBackup(f);
            var provenance = _backupManager.GetBackupProvenance(f);
            var pState = _backupManager.GetFilePristineState(gameDir, f);

            report.HasBackup[f] = hasBackup;
            report.BackupProvenances[f] = provenance;
            report.PristineStates[f] = pState;
            report.IsPristine[f] = (pState == PristineState.Pristine);

            // 收集該檔案已套用之修補簽章
            var fileApplied = new List<string>();
            string targetPath = Path.Combine(gameDir, fileName);
            bool fileExists = File.Exists(targetPath);

            if (fileExists)
            {
                try
                {
                    byte[] bytes = File.ReadAllBytes(targetPath);
                    foreach (var sig in _backupManager.GetSignatures(f))
                    {
                        if (sig.IsApplied(bytes))
                        {
                            fileApplied.Add(sig.PatchId);
                            if (!report.AppliedPatches.Contains(sig.PatchId))
                            {
                                report.AppliedPatches.Add(sig.PatchId);
                            }
                        }
                    }
                }
                catch { }
            }

            // 判定該檔案依據當前設定預期之修補簽章
            var expectedPatches = GetExpectedPatchesForFile(f, effectiveConfig);

            // 比對 live 檔案是否與設定相符
            bool matchesConfig;
            if (!fileExists)
            {
                matchesConfig = false;
            }
            else
            {
                matchesConfig = fileApplied.OrderBy(x => x).SequenceEqual(expectedPatches.OrderBy(x => x));
            }

            var fileInfo = new FileVerificationInfo
            {
                File = fileName,
                HasBackup = hasBackup,
                BackupProvenance = provenance,
                PristineState = pState,
                AppliedPatches = fileApplied,
                ExpectedPatches = expectedPatches,
                MatchesConfig = matchesConfig
            };

            report.Files[fileName] = fileInfo;

            if (!hasBackup)
            {
                warnings.Add(Strings.Get("Warning_VerifyBackupMissing", fileName));
            }
            if (!matchesConfig)
            {
                warnings.Add(Strings.Get("Warning_VerifyConfigMismatch", fileName));
            }
            if (provenance is not null && !provenance.CoverageComplete)
            {
                warnings.Add(Strings.Get("Warning_BaselineCapturedIncompleteCoverage", fileName));
            }
        }

        if (!_backupManager.IsAllCoverageComplete())
        {
            warnings.Add(Strings.Get("Warning_DetectionIncomplete"));
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
                if (config.Perf.Hires > 0) list.Add("hires_zoom");
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
                // Phase 3 語言包簽章
                break;

            case GameFile.VxSettings:
                if (config.Perf.NoObjectAnimations || config.Perf.NoWaterAnimation || !string.IsNullOrWhiteSpace(config.Perf.Resolution))
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
