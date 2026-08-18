using CKToolkit.I18n;

namespace CKToolkit.Core.Common;

/// <summary>
/// 遊戲核心五大檔案列舉。
/// </summary>
public enum GameFile
{
    Exe,
    Launcher,
    DataPak,
    LocalPak,
    VxSettings
}

/// <summary>
/// 檔案原版狀態判定列舉 (SPEC.md §3)。
/// </summary>
public enum PristineState
{
    /// <summary>
    /// 未知：修補特徵庫尚未完整註冊，無法斷定檔案是否為原版。
    /// </summary>
    Unknown,

    /// <summary>
    /// 純淨原版：特徵庫已完整涵蓋該目標檔案，且所有簽章皆未偵測到修補。
    /// </summary>
    Pristine,

    /// <summary>
    /// 已修補：偵測到至少一項已套用之修補簽章。
    /// </summary>
    Patched
}

/// <summary>
/// 修補特徵偵測介面。各模組（Perf、Lang、Trainer）向 BackupManager 註冊其修補簽章。
/// </summary>
public interface IPatchSignature
{
    string PatchId { get; }
    GameFile AppliesTo { get; }
    bool IsApplied(byte[] fileBytes);
}

/// <summary>
/// 舊備份候選檔案資訊結構（僅供檢視與明確選擇遷移，不自動採用）。
/// </summary>
public sealed class LegacyBackupCandidate
{
    public GameFile File { get; init; }
    public string FileName { get; init; } = string.Empty;
    public string SourcePath { get; init; } = string.Empty;
    public long Size { get; init; }
    public DateTime LastModified { get; init; }
    public string SourceProject { get; init; } = string.Empty;
}

/// <summary>
/// 統一備份層 (SPEC.md §3)。
///
/// 關鍵責任：
///   1. 單一備份目錄 `<exe 所在目錄>/backup/`。
///   2. 跨模組 Pristine 判定：必須在所有預期特徵皆已註冊（Coverage 完整）且皆未偵測到修改時才判定為 Pristine；
///      特徵庫不完整時回傳 Unknown，永不以空註冊表判定為原版。
///   3. 備份過期偵測：僅在 Coverage 完整且現行檔案為 Pristine 但與備份內容不同（Steam 遊戲更新）時，
///      才允許自動將舊備份改名為 .superseded 並重新擷取基準；Coverage 不完整時嚴格拒絕重新擷取以防覆蓋原版。
///   4. 缺少備份且無法證明為 Pristine 時，明確拒絕並要求 Steam 驗證完整性。
///   5. 嚴格唯讀查詢保證：檢視與狀態查詢絕不建立目錄、不抓取備份、不寫入檔案。
///   6. 舊備份遷移必須為明確操作，掃描並列出候選者由使用者或呼叫端指定，絕不自動套用。
/// </summary>
public sealed class BackupManager
{
    public const string ExeName = GamePaths.ExeFileName;
    public const string LauncherName = GamePaths.LauncherFileName;
    public const string DataPakName = GamePaths.DataPakFileName;
    public const string LocalPakName = GamePaths.LocalPakFileName;
    public const string VxSettingsName = GamePaths.VxSettingsFileName;

    /// <summary>
    /// 各目標檔案預期之修補簽章清單 (SPEC.md §3)。
    /// 唯有當這些簽章全數註冊至 BackupManager 後，Coverage 才算完整。
    /// </summary>
    public static readonly IReadOnlyDictionary<GameFile, IReadOnlyList<string>> ExpectedSignatureIds =
        new Dictionary<GameFile, IReadOnlyList<string>>
        {
            [GameFile.Exe] = ["laa", "video_fix", "hires_zoom", "res_writeback", "key_map"],
            [GameFile.Launcher] = ["launcher_display", "launcher_mode_table"],
            [GameFile.DataPak] = ["resolutions_append", "trainer_marker"],
            [GameFile.LocalPak] = ["langpack_installed"],
            [GameFile.VxSettings] = ["vxsettings_custom"]
        };

    private readonly string _backupDir;
    private readonly List<IPatchSignature> _signatures = new();

    public BackupManager(string? backupDir = null)
    {
        // 唯讀建構：絕不建立目錄，絕不自動遷移舊備份
        _backupDir = backupDir ?? Path.Combine(AppContext.BaseDirectory, "backup");
    }

    public string BackupDir => _backupDir;

    public IReadOnlyList<IPatchSignature> Signatures => _signatures;

    public static string GetFileName(GameFile file) => file switch
    {
        GameFile.Exe => ExeName,
        GameFile.Launcher => LauncherName,
        GameFile.DataPak => DataPakName,
        GameFile.LocalPak => LocalPakName,
        GameFile.VxSettings => VxSettingsName,
        _ => throw new ArgumentOutOfRangeException(nameof(file))
    };

    public string GetBackupPath(GameFile file) =>
        Path.Combine(_backupDir, GetFileName(file) + ".orig");

    public bool HasBackup(GameFile file) => File.Exists(GetBackupPath(file));

    // ---- 唯讀讀取備份 API --------------------------------------------------

    /// <summary>
    /// 讀取既有之原版備份內容。若尚無備份則回傳失敗，絕不自動建立目錄或擷取檔案。
    /// </summary>
    public Result<byte[]> ReadExistingBackup(GameFile file)
    {
        string backupPath = GetBackupPath(file);
        if (!File.Exists(backupPath))
        {
            return Result<byte[]>.Fail(
                Strings.Get("Error_NoBackupYet", GetFileName(file)),
                ExitCodes.BackupMissingNeedsSteamVerify);
        }

        try
        {
            byte[] bytes = File.ReadAllBytes(backupPath);
            return Result<byte[]>.Ok(bytes);
        }
        catch (Exception ex)
        {
            return Result<byte[]>.Fail(
                Strings.Get("Error_FileLocked", GetFileName(file)) + $" ({ex.Message})",
                ExitCodes.FileLocked);
        }
    }

    // ---- 簽章註冊與 Coverage 判定 -----------------------------------------

    public void RegisterSignature(IPatchSignature signature)
    {
        if (_signatures.Any(s => string.Equals(s.PatchId, signature.PatchId, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"修補簽章 {signature.PatchId} 已經註冊過");
        }
        _signatures.Add(signature);
    }

    public IReadOnlyList<IPatchSignature> GetSignatures(GameFile file) =>
        _signatures.Where(s => s.AppliesTo == file).ToList();

    public IReadOnlyList<string> GetExpectedSignatures(GameFile file) =>
        ExpectedSignatureIds.TryGetValue(file, out var list) ? list : [];

    /// <summary>
    /// 檢查特定檔案的修補簽章是否已全部註冊完畢。
    /// </summary>
    public bool IsCoverageComplete(GameFile file)
    {
        var expected = GetExpectedSignatures(file);
        if (expected.Count == 0) return true;
        var registered = _signatures
            .Where(s => s.AppliesTo == file)
            .Select(s => s.PatchId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return expected.All(registered.Contains);
    }

    /// <summary>
    /// 取得特定檔案尚未註冊的預期簽章清單。
    /// </summary>
    public IReadOnlyList<string> GetMissingSignatures(GameFile file)
    {
        var expected = GetExpectedSignatures(file);
        var registered = _signatures
            .Where(s => s.AppliesTo == file)
            .Select(s => s.PatchId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return expected.Where(id => !registered.Contains(id)).ToList();
    }

    /// <summary>
    /// 檢查所有五大目標檔案之 Coverage 是否全數完整。
    /// </summary>
    public bool IsAllCoverageComplete()
    {
        return Enum.GetValues<GameFile>().All(IsCoverageComplete);
    }

    // ---- Pristine 狀態檢查 ------------------------------------------------

    /// <summary>
    /// 檢查給定檔案內容之原版狀態。
    /// 若有任何已註冊簽章回報 Applied -> Patched。
    /// 若簽章涵蓋未完整 (Coverage incomplete) -> Unknown。
    /// 僅當所有預期簽章皆已註冊且皆未偵測到修補時 -> Pristine。
    /// </summary>
    public PristineState IsPristine(GameFile file, byte[] fileBytes)
    {
        var relevant = GetSignatures(file);
        foreach (var sig in relevant)
        {
            try
            {
                if (sig.IsApplied(fileBytes))
                {
                    return PristineState.Patched;
                }
            }
            catch
            {
                // 簽章探測異常視同已修改
                return PristineState.Patched;
            }
        }

        if (!IsCoverageComplete(file))
        {
            return PristineState.Unknown;
        }

        return PristineState.Pristine;
    }

    /// <summary>
    /// 檢查遊戲目錄中指定檔案之原版狀態。
    /// </summary>
    public PristineState GetFilePristineState(string gameDir, GameFile file)
    {
        string filePath = Path.Combine(gameDir, GetFileName(file));
        if (!File.Exists(filePath)) return PristineState.Unknown;
        try
        {
            byte[] bytes = File.ReadAllBytes(filePath);
            return IsPristine(file, bytes);
        }
        catch
        {
            return PristineState.Unknown;
        }
    }

    /// <summary>
    /// 相容舊呼叫：檢查檔案是否確定為原版（僅 PristineState.Pristine 為 true）。
    /// </summary>
    public bool IsFilePristine(string gameDir, GameFile file) =>
        GetFilePristineState(gameDir, file) == PristineState.Pristine;

    // ---- 備份建立與過期重新擷取 -------------------------------------------

    /// <summary>
    /// 確保指定目標檔案已建立乾淨原版備份。
    /// 此為套用管線之寫入準備路徑，絕不可於唯讀狀態查詢中呼叫。
    /// </summary>
    public Result EnsureBackup(GameFile file, string gameDir)
    {
        string fileName = GetFileName(file);
        string livePath = Path.Combine(gameDir, fileName);
        string backupPath = GetBackupPath(file);

        if (HasBackup(file))
        {
            if (File.Exists(livePath))
            {
                byte[] liveBytes;
                try
                {
                    liveBytes = File.ReadAllBytes(livePath);
                }
                catch (Exception ex)
                {
                    return Result.Fail(Strings.Get("Error_FileLocked", fileName) + $" ({ex.Message})", ExitCodes.FileLocked);
                }

                byte[] backupBytes;
                try
                {
                    backupBytes = File.ReadAllBytes(backupPath);
                }
                catch (Exception ex)
                {
                    return Result.Fail(Strings.Get("Error_FileLocked", fileName) + $" ({ex.Message})", ExitCodes.FileLocked);
                }

                if (!liveBytes.AsSpan().SequenceEqual(backupBytes))
                {
                    // 現行檔案與備份不同：檢查是否可安全重新擷取
                    if (!IsCoverageComplete(file))
                    {
                        // 守護：Coverage 不完整時嚴格拒絕重新擷取，防止以被修改的檔案覆蓋掉乾淨備份
                        return Result.Ok([Strings.Get("Warning_StaleBackupRecaptureRefusedIncompleteCoverage", fileName)]);
                    }

                    var state = IsPristine(file, liveBytes);
                    if (state == PristineState.Pristine)
                    {
                        // 確定為乾淨原版且與備份不同 -> 遊戲已被 Steam 更新
                        string supersededPath = backupPath + ".superseded";
                        try
                        {
                            File.Copy(backupPath, supersededPath, overwrite: true);
                            File.Copy(livePath, backupPath, overwrite: true);
                        }
                        catch (Exception ex)
                        {
                            return Result.Fail(Strings.Get("Error_FileLocked", fileName) + $" ({ex.Message})", ExitCodes.FileLocked);
                        }

                        return Result.Ok([Strings.Get("Backup_Superseded", fileName)]);
                    }
                }
            }

            return Result.Ok();
        }

        // 尚無備份
        if (!File.Exists(livePath))
        {
            return Result.Fail(Strings.Get("Error_GameNotFound") + $" ({livePath})", ExitCodes.GameNotFound);
        }

        byte[] currentBytes;
        try
        {
            currentBytes = File.ReadAllBytes(livePath);
        }
        catch (Exception ex)
        {
            return Result.Fail(Strings.Get("Error_FileLocked", fileName) + $" ({ex.Message})", ExitCodes.FileLocked);
        }

        if (!IsCoverageComplete(file))
        {
            // 特徵庫不完整時，無法證明來源為原版，嚴格拒絕建立基準
            return Result.Fail(
                Strings.Get("Error_CannotEstablishBaselineIncompleteCoverage", fileName),
                ExitCodes.BackupMissingNeedsSteamVerify);
        }

        var currentState = IsPristine(file, currentBytes);
        if (currentState != PristineState.Pristine)
        {
            return Result.Fail(
                Strings.Get("Error_BackupMissingIntegrityNeeded", fileName),
                ExitCodes.BackupMissingNeedsSteamVerify);
        }

        // 明確建立基準
        try
        {
            Directory.CreateDirectory(_backupDir);
            File.Copy(livePath, backupPath, overwrite: false);
        }
        catch (Exception ex)
        {
            return Result.Fail(Strings.Get("Error_FileLocked", fileName) + $" ({ex.Message})", ExitCodes.FileLocked);
        }

        return Result.Ok([Strings.Get("Backup_BaselineEstablished", fileName)]);
    }

    /// <summary>
    /// 確保所有五個目標檔案均有備份。
    /// </summary>
    public Result EnsureAllBackups(string gameDir)
    {
        var allWarnings = new List<string>();
        foreach (GameFile f in Enum.GetValues<GameFile>())
        {
            var res = EnsureBackup(f, gameDir);
            if (!res.Success)
            {
                return res;
            }
            if (res.Warnings.Count > 0)
            {
                allWarnings.AddRange(res.Warnings);
            }
        }
        return Result.Ok(allWarnings);
    }

    /// <summary>
    /// 取得原版檔案內容供管線套用。
    /// 若已有備份則直接讀取；若無備份則透過 EnsureBackup 驗證並建立基準。
    /// </summary>
    public Result<byte[]> ReadPristine(GameFile file, string gameDir)
    {
        if (HasBackup(file))
        {
            return ReadExistingBackup(file);
        }

        var ensure = EnsureBackup(file, gameDir);
        if (!ensure.Success)
        {
            return Result<byte[]>.Fail(ensure.ErrorMessage!, ensure.ExitCode, ensure.Warnings);
        }

        var read = ReadExistingBackup(file);
        if (!read.Success)
        {
            return read;
        }

        return Result<byte[]>.Ok(read.Value!, ensure.Warnings);
    }

    /// <summary>
    /// 從備份還原所有目標檔案至遊戲目錄。
    /// </summary>
    public Result<List<string>> RestoreAll(string gameDir)
    {
        var restored = new List<string>();

        foreach (GameFile f in Enum.GetValues<GameFile>())
        {
            string fileName = GetFileName(f);
            string backupPath = GetBackupPath(f);
            string targetPath = Path.Combine(gameDir, fileName);

            if (!File.Exists(backupPath)) continue;

            string tempPath = targetPath + ".cktmp";
            try
            {
                File.Copy(backupPath, tempPath, overwrite: true);
                File.Move(tempPath, targetPath, overwrite: true);
                restored.Add(fileName);
            }
            catch (Exception ex)
            {
                try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
                return Result<List<string>>.Fail(
                    Strings.Get("Error_FileLocked", fileName) + $" ({ex.Message})",
                    ExitCodes.FileLocked);
            }
        }

        return Result<List<string>>.Ok(restored);
    }

    // ---- 舊備份明確掃描與遷移 (SPEC.md §3) -----------------------------------

    /// <summary>
    /// 掃描前身專案之舊備份候選檔案（唯讀，不作任何變更）。
    /// </summary>
    public IReadOnlyList<LegacyBackupCandidate> FindLegacyBackupCandidates()
    {
        var candidates = new List<LegacyBackupCandidate>();
        var searchRoots = new (string Dir, string ProjectName)[]
        {
            (Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CKTrainer", "backup"), "CKTrainer (AppData)"),
            (Path.Combine(AppContext.BaseDirectory, "CKTrainer", "backup"), "CKTrainer"),
            (Path.Combine(AppContext.BaseDirectory, "CKPatcher", "backup"), "CKPatcher"),
            (Path.Combine(AppContext.BaseDirectory, "backup"), "CKToolkit"),
        };

        foreach (var (dir, proj) in searchRoots)
        {
            if (!Directory.Exists(dir)) continue;

            foreach (GameFile f in Enum.GetValues<GameFile>())
            {
                string fn = GetFileName(f);
                string[] possibleFiles = [Path.Combine(dir, fn + ".orig"), Path.Combine(dir, fn)];

                foreach (string pf in possibleFiles)
                {
                    if (File.Exists(pf))
                    {
                        try
                        {
                            var fi = new FileInfo(pf);
                            candidates.Add(new LegacyBackupCandidate
                            {
                                File = f,
                                FileName = fn,
                                SourcePath = pf,
                                Size = fi.Length,
                                LastModified = fi.LastWriteTime,
                                SourceProject = proj
                            });
                        }
                        catch { }
                    }
                }
            }
        }

        return candidates;
    }

    /// <summary>
    /// 明確遷移指定之舊備份候選檔案至統一備份目錄。
    /// 遷移前嚴格驗證 Coverage 與 Pristine 狀態，非原版或無法證明者拒絕遷移。
    /// </summary>
    public Result MigrateLegacyBackup(LegacyBackupCandidate candidate, bool overwrite = false)
    {
        if (!File.Exists(candidate.SourcePath))
        {
            return Result.Fail(Strings.Get("Error_GeneralFailure", $"舊備份來源檔案不存在：{candidate.SourcePath}"), ExitCodes.GeneralFailure);
        }

        string targetBackup = GetBackupPath(candidate.File);
        if (File.Exists(targetBackup) && !overwrite)
        {
            return Result.Fail(Strings.Get("Error_GeneralFailure", $"目標備份已存在：{targetBackup}"), ExitCodes.GeneralFailure);
        }

        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(candidate.SourcePath);
        }
        catch (Exception ex)
        {
            return Result.Fail(Strings.Get("Error_FileLocked", candidate.FileName) + $" ({ex.Message})", ExitCodes.FileLocked);
        }

        if (!IsCoverageComplete(candidate.File))
        {
            return Result.Fail(
                Strings.Get("Error_CannotEstablishBaselineIncompleteCoverage", candidate.FileName),
                ExitCodes.BackupMissingNeedsSteamVerify);
        }

        var state = IsPristine(candidate.File, bytes);
        if (state != PristineState.Pristine)
        {
            return Result.Fail(
                Strings.Get("Error_LegacyBackupNotVanilla", candidate.FileName, candidate.SourcePath),
                ExitCodes.BackupMissingNeedsSteamVerify);
        }

        try
        {
            Directory.CreateDirectory(_backupDir);
            File.Copy(candidate.SourcePath, targetBackup, overwrite: true);
        }
        catch (Exception ex)
        {
            return Result.Fail(Strings.Get("Error_FileLocked", candidate.FileName) + $" ({ex.Message})", ExitCodes.FileLocked);
        }

        return Result.Ok([Strings.Get("Backup_LegacyMigrated", candidate.FileName, candidate.SourcePath, candidate.Size)]);
    }
}
