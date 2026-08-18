using System.Text;
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
/// 驗證報告資料結構。
/// </summary>
public sealed class VerificationReport
{
    public bool GameFound { get; set; }
    public string GameDir { get; set; } = string.Empty;
    public Dictionary<GameFile, bool> HasBackup { get; set; } = new();
    public Dictionary<GameFile, PristineState> PristineStates { get; set; } = new();
    public Dictionary<GameFile, bool> IsPristine { get; set; } = new();
    public List<string> AppliedPatches { get; set; } = [];
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

    /// <summary>
    /// 執行完整修補套用流程。
    /// </summary>
    public Result ApplyAll(string gameDir, ToolkitConfig config)
    {
        if (!GamePaths.IsGameDir(gameDir))
        {
            return Result.Fail(Strings.Get("Error_GameNotFound"), ExitCodes.GameNotFound);
        }

        // 1. 確保五大目標檔案均有原版備份
        var backupRes = _backupManager.EnsureAllBackups(gameDir);
        if (!backupRes.Success)
        {
            return backupRes;
        }

        var warnings = new List<string>(backupRes.Warnings);

        // 2. Exe 重建與寫入
        var exeRes = _backupManager.ReadPristine(GameFile.Exe, gameDir);
        if (!exeRes.Success) return exeRes;
        byte[] exeBytes = exeRes.Value!;

        foreach (var mod in _modules)
        {
            mod.ApplyExe(ref exeBytes, config);
        }

        var writeExeRes = WriteAtomic(GamePaths.GetExePath(gameDir), exeBytes, GamePaths.ExeFileName);
        if (!writeExeRes.Success) return writeExeRes;

        // 3. Launcher 重建與寫入
        var launcherRes = _backupManager.ReadPristine(GameFile.Launcher, gameDir);
        if (!launcherRes.Success) return launcherRes;
        byte[] launcherBytes = launcherRes.Value!;

        foreach (var mod in _modules)
        {
            mod.ApplyLauncher(ref launcherBytes, config);
        }

        var writeLauncherRes = WriteAtomic(GamePaths.GetLauncherPath(gameDir), launcherBytes, GamePaths.LauncherFileName);
        if (!writeLauncherRes.Success) return writeLauncherRes;

        // 4. data.pak 重建與寫入
        var dataPakRes = _backupManager.ReadPristine(GameFile.DataPak, gameDir);
        if (!dataPakRes.Success) return dataPakRes;

        HmmPak dataPak;
        try
        {
            dataPak = HmmPak.FromBytes(dataPakRes.Value!);
        }
        catch (PakException ex)
        {
            return Result.Fail($"解析 pristine data.pak 失敗：{ex.Message}", ExitCodes.GeneralFailure);
        }

        foreach (var mod in _modules)
        {
            mod.ApplyDataPak(dataPak, config);
        }

        byte[] dataPakBytes = dataPak.ToBytes();
        var writeDataPakRes = WriteAtomic(GamePaths.GetDataPakPath(gameDir), dataPakBytes, GamePaths.DataPakFileName);
        if (!writeDataPakRes.Success) return writeDataPakRes;

        // 5. local.pak 重建與寫入
        var localPakRes = _backupManager.ReadPristine(GameFile.LocalPak, gameDir);
        if (!localPakRes.Success) return localPakRes;

        HmmPak localPak;
        try
        {
            localPak = HmmPak.FromBytes(localPakRes.Value!);
        }
        catch (PakException ex)
        {
            return Result.Fail($"解析 pristine local.pak 失敗：{ex.Message}", ExitCodes.GeneralFailure);
        }

        foreach (var mod in _modules)
        {
            mod.ApplyLocalPak(localPak, config);
        }

        byte[] localPakBytes = localPak.ToBytes();
        var writeLocalPakRes = WriteAtomic(GamePaths.GetLocalPakPath(gameDir), localPakBytes, GamePaths.LocalPakFileName);
        if (!writeLocalPakRes.Success) return writeLocalPakRes;

        // 6. vxSettings.ini 重建與寫入
        var vxRes = _backupManager.ReadPristine(GameFile.VxSettings, gameDir);
        if (!vxRes.Success) return vxRes;

        string vxText = IniEncoding.GetString(vxRes.Value!);
        var ini = IniFile.FromText(vxText);

        foreach (var mod in _modules)
        {
            mod.ApplyVxSettings(ini, config, null);
        }

        byte[] iniBytes = IniEncoding.GetBytes(ini.ToText());
        var writeVxRes = WriteAtomic(GamePaths.GetVxSettingsPath(gameDir), iniBytes, GamePaths.VxSettingsFileName);
        if (!writeVxRes.Success) return writeVxRes;

        return Result.Ok(warnings);
    }

    /// <summary>
    /// 還原全部五個目標檔案為原版。
    /// </summary>
    public Result<List<string>> RestoreAll(string gameDir)
    {
        if (!GamePaths.IsGameDir(gameDir))
        {
            return Result<List<string>>.Fail(Strings.Get("Error_GameNotFound"), ExitCodes.GameNotFound);
        }

        return _backupManager.RestoreAll(gameDir);
    }

    /// <summary>
    /// 檢查備份完整性與當前修補狀態。
    /// </summary>
    public Result<VerificationReport> Verify(string gameDir)
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

        foreach (GameFile f in Enum.GetValues<GameFile>())
        {
            report.HasBackup[f] = _backupManager.HasBackup(f);
            var pState = _backupManager.GetFilePristineState(gameDir, f);
            report.PristineStates[f] = pState;
            report.IsPristine[f] = (pState == PristineState.Pristine);
        }

        // 檢查已套用的修補簽章
        foreach (var sig in _backupManager.Signatures)
        {
            string targetPath = Path.Combine(gameDir, BackupManager.GetFileName(sig.AppliesTo));
            if (File.Exists(targetPath))
            {
                try
                {
                    byte[] bytes = File.ReadAllBytes(targetPath);
                    if (sig.IsApplied(bytes))
                    {
                        report.AppliedPatches.Add(sig.PatchId);
                    }
                }
                catch { }
            }
        }

        return Result<VerificationReport>.Ok(report);
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
