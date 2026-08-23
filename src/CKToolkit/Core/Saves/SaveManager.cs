using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CKToolkit.Core.Common;
using CKToolkit.I18n;

namespace CKToolkit.Core.Saves;

public sealed record GameSaveInfo(
    string Profile,
    string FileName,
    long SizeBytes,
    DateTime LastWriteTimeUtc,
    string FullPath,
    string? ScreenshotPath)
{
    public bool HasScreenshot => ScreenshotPath is not null;
}

public sealed record SaveProfileInfo(string Name, bool IsDefault, IReadOnlyList<GameSaveInfo> Saves);

public sealed record SaveCatalog(string ProfilesRoot, string? DefaultProfile, IReadOnlyList<SaveProfileInfo> Profiles)
{
    public int SaveCount => Profiles.Sum(profile => profile.Saves.Count);
}

public sealed record SaveExportResult(string ArchivePath, string Profile, string SaveFileName, long ArchiveSizeBytes);

public sealed record SaveImportResult(
    string Profile,
    string SaveFileName,
    string SavePath,
    string? ScreenshotPath,
    string SourceProfile,
    string SourceSaveFileName);

public sealed record SaveDeleteResult(string Profile, string SaveFileName, string RecoveryArchivePath);

public sealed record PlayerProfileData(
    string Profile,
    string DisplayName,
    int Color,
    int Race,
    int Games,
    string PlayerIniPath);

public sealed record PlayerProfileUpdate(string DisplayName, int Color, int Race);

public sealed class SaveArchiveManifest
{
    [JsonPropertyName("formatVersion")]
    public int FormatVersion { get; set; } = SaveManager.ArchiveFormatVersion;

    [JsonPropertyName("product")]
    public string Product { get; set; } = SaveManager.ArchiveProduct;

    [JsonPropertyName("exportedAtUtc")]
    public DateTime ExportedAtUtc { get; set; }

    [JsonPropertyName("sourceProfile")]
    public string SourceProfile { get; set; } = string.Empty;

    [JsonPropertyName("saveFileName")]
    public string SaveFileName { get; set; } = string.Empty;

    [JsonPropertyName("saveLastWriteTimeUtc")]
    public DateTime SaveLastWriteTimeUtc { get; set; }

    [JsonPropertyName("screenshotFileName")]
    public string? ScreenshotFileName { get; set; }

    [JsonPropertyName("files")]
    public List<SaveArchiveFile> Files { get; set; } = [];
}

public sealed class SaveArchiveFile
{
    [JsonPropertyName("entry")]
    public string Entry { get; set; } = string.Empty;

    [JsonPropertyName("length")]
    public long Length { get; set; }

    [JsonPropertyName("sha256")]
    public string Sha256 { get; set; } = string.Empty;
}

/// <summary>
/// 管理遊戲安裝目錄內 <c>profiles\&lt;player&gt;\*.adv</c> 玩家存檔。
/// 存檔操作不碰原廠 Adventures/Scenarios、profiles.ini 或 currentadv.bfhp；玩家資料編輯
/// 僅更新 player.ini 已確認的名稱、顏色、種族六個鏡像鍵，其他內容逐行保留。
/// </summary>
public static class SaveManager
{
    public const int ArchiveFormatVersion = 1;
    public const string ArchiveProduct = "CKToolkit.CelticKingsSave";
    public const string ArchiveExtension = ".cksave";

    private const long MaxSaveBytes = 256L * 1024 * 1024;
    private const long MaxScreenshotBytes = 16L * 1024 * 1024;
    private const long MaxManifestBytes = 64L * 1024;
    private const string ManifestEntryName = "manifest.json";

    private static readonly JsonSerializerOptions ManifestJsonOptions = new()
    {
        WriteIndented = true
    };

    public static string GetProfilesRoot(string gameDir) => Path.Combine(gameDir, "profiles");

    public static string GetDefaultTrashRoot() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CKToolkit",
        "SaveTrash");

    /// <summary>唯讀列舉；不存在 profiles 時回傳空清冊，不建立任何目錄。</summary>
    public static Result<SaveCatalog> Inspect(string gameDir)
    {
        if (!GamePaths.IsGameDir(gameDir))
            return Result<SaveCatalog>.Fail(Strings.Get("Error_GameNotFound"), ExitCodes.GameNotFound);

        try
        {
            string profilesRoot = Path.GetFullPath(GetProfilesRoot(gameDir));
            if (!Directory.Exists(profilesRoot))
                return Result<SaveCatalog>.Ok(new SaveCatalog(profilesRoot, null, []));

            var rootInfo = new DirectoryInfo(profilesRoot);
            if ((rootInfo.Attributes & FileAttributes.ReparsePoint) != 0)
                return Result<SaveCatalog>.Fail(Strings.Get("Save_Error_ReparsePoint"));

            string? defaultProfile = ReadDefaultProfile(profilesRoot);
            var profiles = new List<SaveProfileInfo>();

            foreach (DirectoryInfo profileDir in rootInfo.EnumerateDirectories().OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase))
            {
                if (!IsSimpleName(profileDir.Name) || (profileDir.Attributes & FileAttributes.ReparsePoint) != 0)
                    continue;

                var saves = new List<GameSaveInfo>();
                foreach (FileInfo file in profileDir.EnumerateFiles()
                    .Where(f => f.Extension.Equals(".adv", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(f => f.LastWriteTimeUtc))
                {
                    if ((file.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                    string screenshot = file.FullName + ".bmp";
                    string? screenshotPath = IsRegularFile(screenshot) ? screenshot : null;
                    saves.Add(new GameSaveInfo(
                        profileDir.Name,
                        file.Name,
                        file.Length,
                        file.LastWriteTimeUtc,
                        file.FullName,
                        screenshotPath));
                }

                profiles.Add(new SaveProfileInfo(
                    profileDir.Name,
                    string.Equals(defaultProfile, profileDir.Name, StringComparison.OrdinalIgnoreCase),
                    saves));
            }

            return Result<SaveCatalog>.Ok(new SaveCatalog(profilesRoot, defaultProfile, profiles));
        }
        catch (Exception ex)
        {
            return Result<SaveCatalog>.Fail(Strings.Get("Save_Error_Inspect", ex.Message));
        }
    }

    public static Result<SaveExportResult> ExportSave(
        string gameDir,
        string profile,
        string saveFileName,
        string archivePath,
        bool overwrite = false)
    {
        if (GamePaths.IsGameRunning(gameDir))
            return Result<SaveExportResult>.Fail(Strings.Get("Error_GameRunning"), ExitCodes.FileLocked);

        Result<GameSaveInfo> selectedResult = ResolveSave(gameDir, profile, saveFileName);
        if (!selectedResult.Success || selectedResult.Value is null)
            return Result<SaveExportResult>.Fail(selectedResult.ErrorMessage ?? Strings.Get("Save_Error_SaveMissing"));

        GameSaveInfo selected = selectedResult.Value;
        try
        {
            string fullArchivePath = Path.GetFullPath(archivePath);
            if (!Path.GetExtension(fullArchivePath).Equals(ArchiveExtension, StringComparison.OrdinalIgnoreCase))
                return Result<SaveExportResult>.Fail(Strings.Get("Save_Error_ArchiveExtension", ArchiveExtension), ExitCodes.InvalidArgs);

            string? outputDirectory = Path.GetDirectoryName(fullArchivePath);
            if (string.IsNullOrWhiteSpace(outputDirectory) || !Directory.Exists(outputDirectory))
                return Result<SaveExportResult>.Fail(Strings.Get("Save_Error_OutputDirectoryMissing", outputDirectory ?? string.Empty), ExitCodes.InvalidArgs);
            if (File.Exists(fullArchivePath) && !overwrite)
                return Result<SaveExportResult>.Fail(Strings.Get("Save_Error_ArchiveExists", fullArchivePath), ExitCodes.InvalidArgs);

            byte[] saveBytes = ReadBoundedFile(selected.FullPath, MaxSaveBytes, "Save_Error_SaveTooLarge");
            byte[]? screenshotBytes = selected.ScreenshotPath is null
                ? null
                : ReadBoundedFile(selected.ScreenshotPath, MaxScreenshotBytes, "Save_Error_ScreenshotTooLarge");

            string saveEntryName = "save/" + selected.FileName;
            string? screenshotFileName = selected.ScreenshotPath is null ? null : Path.GetFileName(selected.ScreenshotPath);
            string? screenshotEntryName = screenshotFileName is null ? null : "save/" + screenshotFileName;
            var manifest = new SaveArchiveManifest
            {
                ExportedAtUtc = DateTime.UtcNow,
                SourceProfile = selected.Profile,
                SaveFileName = selected.FileName,
                SaveLastWriteTimeUtc = selected.LastWriteTimeUtc,
                ScreenshotFileName = screenshotFileName,
                Files =
                [
                    DescribeArchiveFile(saveEntryName, saveBytes)
                ]
            };
            if (screenshotBytes is not null && screenshotEntryName is not null)
                manifest.Files.Add(DescribeArchiveFile(screenshotEntryName, screenshotBytes));

            string tempPath = Path.Combine(outputDirectory, ".cktoolkit-save-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
                using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: false))
                {
                    WriteEntry(archive, saveEntryName, saveBytes);
                    if (screenshotBytes is not null && screenshotEntryName is not null)
                        WriteEntry(archive, screenshotEntryName, screenshotBytes);

                    byte[] manifestBytes = JsonSerializer.SerializeToUtf8Bytes(manifest, ManifestJsonOptions);
                    WriteEntry(archive, ManifestEntryName, manifestBytes);
                }

                File.Move(tempPath, fullArchivePath, overwrite);
            }
            finally
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }

            return Result<SaveExportResult>.Ok(new SaveExportResult(
                fullArchivePath,
                selected.Profile,
                selected.FileName,
                new FileInfo(fullArchivePath).Length));
        }
        catch (InvalidDataException ex)
        {
            return Result<SaveExportResult>.Fail(ex.Message);
        }
        catch (Exception ex)
        {
            return Result<SaveExportResult>.Fail(Strings.Get("Save_Error_Export", ex.Message));
        }
    }

    /// <summary>匯入永不覆寫既有存檔；撞名時改用下一個可用的數字槽。</summary>
    public static Result<SaveImportResult> ImportSave(string gameDir, string profile, string archivePath)
    {
        if (GamePaths.IsGameRunning(gameDir))
            return Result<SaveImportResult>.Fail(Strings.Get("Error_GameRunning"), ExitCodes.FileLocked);

        Result<string> profileResult = ResolveProfileDirectory(gameDir, profile);
        if (!profileResult.Success || profileResult.Value is null)
            return Result<SaveImportResult>.Fail(profileResult.ErrorMessage ?? Strings.Get("Save_Error_ProfileMissing", profile));

        Result<ArchivePayload> archiveResult = ReadArchive(archivePath);
        if (!archiveResult.Success || archiveResult.Value is null)
            return Result<SaveImportResult>.Fail(archiveResult.ErrorMessage ?? Strings.Get("Save_Error_ArchiveInvalid"));

        string profileDirectory = profileResult.Value;
        ArchivePayload payload = archiveResult.Value;
        string targetFileName = ChooseAvailableSaveFileName(profileDirectory, payload.Manifest.SaveFileName);
        string targetSavePath = Path.Combine(profileDirectory, targetFileName);
        string? targetScreenshotPath = payload.ScreenshotBytes is null ? null : targetSavePath + ".bmp";
        string tempSavePath = Path.Combine(profileDirectory, ".cktoolkit-import-" + Guid.NewGuid().ToString("N") + ".tmp");
        string? tempScreenshotPath = payload.ScreenshotBytes is null
            ? null
            : Path.Combine(profileDirectory, ".cktoolkit-import-" + Guid.NewGuid().ToString("N") + ".tmp");
        bool saveMoved = false;
        bool screenshotMoved = false;

        try
        {
            File.WriteAllBytes(tempSavePath, payload.SaveBytes);
            if (tempScreenshotPath is not null && payload.ScreenshotBytes is not null)
                File.WriteAllBytes(tempScreenshotPath, payload.ScreenshotBytes);

            File.Move(tempSavePath, targetSavePath);
            saveMoved = true;
            if (tempScreenshotPath is not null && targetScreenshotPath is not null)
            {
                File.Move(tempScreenshotPath, targetScreenshotPath);
                screenshotMoved = true;
            }

            File.SetLastWriteTimeUtc(targetSavePath, payload.Manifest.SaveLastWriteTimeUtc);
            if (targetScreenshotPath is not null)
                File.SetLastWriteTimeUtc(targetScreenshotPath, payload.Manifest.SaveLastWriteTimeUtc);

            return Result<SaveImportResult>.Ok(new SaveImportResult(
                profile,
                targetFileName,
                targetSavePath,
                targetScreenshotPath,
                payload.Manifest.SourceProfile,
                payload.Manifest.SaveFileName));
        }
        catch (Exception ex)
        {
            TryDelete(tempSavePath);
            if (tempScreenshotPath is not null) TryDelete(tempScreenshotPath);
            if (screenshotMoved && targetScreenshotPath is not null) TryDelete(targetScreenshotPath);
            if (saveMoved) TryDelete(targetSavePath);
            return Result<SaveImportResult>.Fail(Strings.Get("Save_Error_Import", ex.Message));
        }
    }

    /// <summary>先產生並驗證可匯回的 recovery archive，再移除原存檔。</summary>
    public static Result<SaveDeleteResult> DeleteSave(
        string gameDir,
        string profile,
        string saveFileName,
        string? trashRoot = null)
    {
        if (GamePaths.IsGameRunning(gameDir))
            return Result<SaveDeleteResult>.Fail(Strings.Get("Error_GameRunning"), ExitCodes.FileLocked);

        Result<GameSaveInfo> selectedResult = ResolveSave(gameDir, profile, saveFileName);
        if (!selectedResult.Success || selectedResult.Value is null)
            return Result<SaveDeleteResult>.Fail(selectedResult.ErrorMessage ?? Strings.Get("Save_Error_SaveMissing"));

        GameSaveInfo selected = selectedResult.Value;
        try
        {
            string recoveryRoot = Path.GetFullPath(trashRoot ?? GetDefaultTrashRoot());
            Directory.CreateDirectory(recoveryRoot);
            string baseName = $"{DateTime.Now:yyyyMMdd-HHmmss}-{SanitizeToken(profile)}-{SanitizeToken(Path.GetFileNameWithoutExtension(selected.FileName))}";
            string recoveryPath = Path.Combine(recoveryRoot, baseName + "-" + Guid.NewGuid().ToString("N")[..8] + ArchiveExtension);

            Result<SaveExportResult> export = ExportSave(gameDir, profile, selected.FileName, recoveryPath);
            if (!export.Success)
                return Result<SaveDeleteResult>.Fail(export.ErrorMessage ?? Strings.Get("Save_Error_DeleteRecoveryFailed"));

            Result<ArchivePayload> verification = ReadArchive(recoveryPath);
            if (!verification.Success)
                return Result<SaveDeleteResult>.Fail(Strings.Get("Save_Error_DeleteRecoveryFailed"));

            File.Delete(selected.FullPath);
            if (selected.ScreenshotPath is not null && File.Exists(selected.ScreenshotPath))
                File.Delete(selected.ScreenshotPath);

            return Result<SaveDeleteResult>.Ok(new SaveDeleteResult(profile, selected.FileName, recoveryPath));
        }
        catch (Exception ex)
        {
            return Result<SaveDeleteResult>.Fail(Strings.Get("Save_Error_Delete", ex.Message));
        }
    }

    public static Result<PlayerProfileData> GetPlayerProfile(string gameDir, string profile)
    {
        Result<string> profileResult = ResolveProfileDirectory(gameDir, profile);
        if (!profileResult.Success || profileResult.Value is null)
            return Result<PlayerProfileData>.Fail(profileResult.ErrorMessage ?? Strings.Get("Save_Error_ProfileMissing", profile));

        string playerIniPath = Path.Combine(profileResult.Value, "player.ini");
        if (!File.Exists(playerIniPath))
            return Result<PlayerProfileData>.Fail(Strings.Get("Save_Error_PlayerIniMissing", profile));
        if (!IsRegularFile(playerIniPath))
            return Result<PlayerProfileData>.Fail(Strings.Get("Save_Error_ReparsePoint"));

        try
        {
            Encoding encoding = Encoding.GetEncoding(1252);
            IniFile ini = IniFile.FromText(encoding.GetString(File.ReadAllBytes(playerIniPath)));
            string displayName = ini.GetValue("Player", "name", profile);
            if (!TryParseBoundedInt(ini.GetValue("Player", "color"), 0, 7, out int color))
                return Result<PlayerProfileData>.Fail(Strings.Get("Save_Error_PlayerColor"));
            if (!TryParseBoundedInt(ini.GetValue("Player", "race"), 0, 2, out int race))
                return Result<PlayerProfileData>.Fail(Strings.Get("Save_Error_PlayerRace"));
            int games = ParseBoundedInt(ini.GetValue("Player", "games"), 0, int.MaxValue, 0);
            return Result<PlayerProfileData>.Ok(new PlayerProfileData(
                profile,
                displayName,
                color,
                race,
                games,
                playerIniPath));
        }
        catch (Exception ex)
        {
            return Result<PlayerProfileData>.Fail(Strings.Get("Save_Error_PlayerRead", ex.Message));
        }
    }

    public static Result<PlayerProfileData> UpdatePlayerProfile(
        string gameDir,
        string profile,
        PlayerProfileUpdate update)
    {
        if (GamePaths.IsGameRunning(gameDir))
            return Result<PlayerProfileData>.Fail(Strings.Get("Error_GameRunning"), ExitCodes.FileLocked);

        string displayName = update.DisplayName.Trim();
        if (displayName.Length is < 1 or > 32 || displayName.IndexOfAny(['\r', '\n', '[', ']', '=']) >= 0)
            return Result<PlayerProfileData>.Fail(Strings.Get("Save_Error_PlayerName"), ExitCodes.InvalidArgs);
        if (update.Color is < 0 or > 7)
            return Result<PlayerProfileData>.Fail(Strings.Get("Save_Error_PlayerColor"), ExitCodes.InvalidArgs);
        if (update.Race is < 0 or > 2)
            return Result<PlayerProfileData>.Fail(Strings.Get("Save_Error_PlayerRace"), ExitCodes.InvalidArgs);

        Result<PlayerProfileData> currentResult = GetPlayerProfile(gameDir, profile);
        if (!currentResult.Success || currentResult.Value is null)
            return Result<PlayerProfileData>.Fail(currentResult.ErrorMessage ?? Strings.Get("Save_Error_PlayerIniMissing", profile));

        string playerIniPath = currentResult.Value.PlayerIniPath;
        string? directory = Path.GetDirectoryName(playerIniPath);
        if (directory is null)
            return Result<PlayerProfileData>.Fail(Strings.Get("Save_Error_PlayerWrite", playerIniPath));

        string tempPath = Path.Combine(directory, ".cktoolkit-player-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            Encoding encoding = Encoding.GetEncoding(1252);
            IniFile ini = IniFile.FromText(encoding.GetString(File.ReadAllBytes(playerIniPath)));
            string color = update.Color.ToString(System.Globalization.CultureInfo.InvariantCulture);
            string race = update.Race.ToString(System.Globalization.CultureInfo.InvariantCulture);
            ini.SetValue("Player", "name", displayName);
            ini.SetValue("Player", "color", color);
            ini.SetValue("Player", "race", race);
            ini.SetValue("Player 0", "plrname", displayName);
            ini.SetValue("Player 0", "plrcolor", color);
            ini.SetValue("Player 0", "plrnation", race);

            File.WriteAllBytes(tempPath, encoding.GetBytes(ini.ToText()));
            File.Move(tempPath, playerIniPath, overwrite: true);
            return GetPlayerProfile(gameDir, profile);
        }
        catch (Exception ex)
        {
            TryDelete(tempPath);
            return Result<PlayerProfileData>.Fail(Strings.Get("Save_Error_PlayerWrite", ex.Message));
        }
    }

    private static Result<GameSaveInfo> ResolveSave(string gameDir, string profile, string saveFileName)
    {
        Result<string> profileResult = ResolveProfileDirectory(gameDir, profile);
        if (!profileResult.Success || profileResult.Value is null)
            return Result<GameSaveInfo>.Fail(profileResult.ErrorMessage ?? Strings.Get("Save_Error_ProfileMissing", profile));

        string normalizedName = NormalizeSaveFileName(saveFileName);
        if (!IsSimpleName(normalizedName) || !Path.GetExtension(normalizedName).Equals(".adv", StringComparison.OrdinalIgnoreCase))
            return Result<GameSaveInfo>.Fail(Strings.Get("Save_Error_InvalidSaveName", saveFileName), ExitCodes.InvalidArgs);

        string path = Path.Combine(profileResult.Value, normalizedName);
        if (!File.Exists(path))
            return Result<GameSaveInfo>.Fail(Strings.Get("Save_Error_SaveMissingNamed", profile, normalizedName), ExitCodes.InvalidArgs);

        var info = new FileInfo(path);
        if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
            return Result<GameSaveInfo>.Fail(Strings.Get("Save_Error_ReparsePoint"));
        string screenshot = path + ".bmp";
        if (File.Exists(screenshot) && !IsRegularFile(screenshot))
            return Result<GameSaveInfo>.Fail(Strings.Get("Save_Error_ReparsePoint"));
        return Result<GameSaveInfo>.Ok(new GameSaveInfo(
            profile,
            info.Name,
            info.Length,
            info.LastWriteTimeUtc,
            info.FullName,
            IsRegularFile(screenshot) ? screenshot : null));
    }

    internal static Result<string> ResolveProfileDirectory(string gameDir, string profile)
    {
        if (!GamePaths.IsGameDir(gameDir))
            return Result<string>.Fail(Strings.Get("Error_GameNotFound"), ExitCodes.GameNotFound);
        if (!IsSimpleName(profile))
            return Result<string>.Fail(Strings.Get("Save_Error_InvalidProfile", profile), ExitCodes.InvalidArgs);

        try
        {
            string root = Path.GetFullPath(GetProfilesRoot(gameDir));
            if (!Directory.Exists(root))
                return Result<string>.Fail(Strings.Get("Save_Error_ProfilesMissing"));
            if ((new DirectoryInfo(root).Attributes & FileAttributes.ReparsePoint) != 0)
                return Result<string>.Fail(Strings.Get("Save_Error_ReparsePoint"));

            string full = Path.GetFullPath(Path.Combine(root, profile));
            if (!string.Equals(Path.GetDirectoryName(full), root, StringComparison.OrdinalIgnoreCase) || !Directory.Exists(full))
                return Result<string>.Fail(Strings.Get("Save_Error_ProfileMissing", profile), ExitCodes.InvalidArgs);
            if ((new DirectoryInfo(full).Attributes & FileAttributes.ReparsePoint) != 0)
                return Result<string>.Fail(Strings.Get("Save_Error_ReparsePoint"));
            return Result<string>.Ok(full);
        }
        catch (Exception ex)
        {
            return Result<string>.Fail(Strings.Get("Save_Error_Inspect", ex.Message));
        }
    }

    private static Result<ArchivePayload> ReadArchive(string archivePath)
    {
        try
        {
            string fullPath = Path.GetFullPath(archivePath);
            if (!File.Exists(fullPath))
                return Result<ArchivePayload>.Fail(Strings.Get("Save_Error_ArchiveMissing", fullPath), ExitCodes.InvalidArgs);
            if (!Path.GetExtension(fullPath).Equals(ArchiveExtension, StringComparison.OrdinalIgnoreCase))
                return Result<ArchivePayload>.Fail(Strings.Get("Save_Error_ArchiveExtension", ArchiveExtension), ExitCodes.InvalidArgs);

            using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
            if (archive.Entries.Count is < 2 or > 3)
                return Result<ArchivePayload>.Fail(Strings.Get("Save_Error_ArchiveInvalid"));

            var entries = archive.Entries.ToDictionary(entry => entry.FullName, StringComparer.Ordinal);
            if (entries.Count != archive.Entries.Count || !entries.TryGetValue(ManifestEntryName, out ZipArchiveEntry? manifestEntry))
                return Result<ArchivePayload>.Fail(Strings.Get("Save_Error_ArchiveInvalid"));
            if (manifestEntry.Length <= 0 || manifestEntry.Length > MaxManifestBytes)
                return Result<ArchivePayload>.Fail(Strings.Get("Save_Error_ArchiveInvalid"));

            SaveArchiveManifest? manifest;
            using (Stream manifestStream = manifestEntry.Open())
            {
                manifest = JsonSerializer.Deserialize<SaveArchiveManifest>(manifestStream, ManifestJsonOptions);
            }
            if (manifest is null || manifest.Files is null ||
                manifest.FormatVersion != ArchiveFormatVersion || manifest.Product != ArchiveProduct)
                return Result<ArchivePayload>.Fail(Strings.Get("Save_Error_ArchiveInvalid"));
            if (!IsSimpleName(manifest.SourceProfile) || !IsSimpleName(manifest.SaveFileName) ||
                !Path.GetExtension(manifest.SaveFileName).Equals(".adv", StringComparison.OrdinalIgnoreCase))
                return Result<ArchivePayload>.Fail(Strings.Get("Save_Error_ArchiveInvalid"));
            if (manifest.ScreenshotFileName is not null &&
                (!IsSimpleName(manifest.ScreenshotFileName) ||
                 !manifest.ScreenshotFileName.Equals(manifest.SaveFileName + ".bmp", StringComparison.OrdinalIgnoreCase)))
                return Result<ArchivePayload>.Fail(Strings.Get("Save_Error_ArchiveInvalid"));

            string saveEntryName = "save/" + manifest.SaveFileName;
            string? screenshotEntryName = manifest.ScreenshotFileName is null ? null : "save/" + manifest.ScreenshotFileName;
            var expectedEntries = new HashSet<string>(StringComparer.Ordinal) { ManifestEntryName, saveEntryName };
            if (screenshotEntryName is not null) expectedEntries.Add(screenshotEntryName);
            var expectedPayloadEntries = new HashSet<string>(expectedEntries, StringComparer.Ordinal);
            expectedPayloadEntries.Remove(ManifestEntryName);
            var manifestPayloadEntries = manifest.Files.Select(file => file.Entry).ToList();
            if (!expectedEntries.SetEquals(entries.Keys) ||
                manifestPayloadEntries.Count != manifestPayloadEntries.Distinct(StringComparer.Ordinal).Count() ||
                !expectedPayloadEntries.SetEquals(manifestPayloadEntries))
                return Result<ArchivePayload>.Fail(Strings.Get("Save_Error_ArchiveInvalid"));

            byte[] saveBytes = ReadAndVerifyArchiveEntry(entries[saveEntryName], manifest, MaxSaveBytes);
            if (saveBytes.Length == 0)
                return Result<ArchivePayload>.Fail(Strings.Get("Save_Error_ArchiveInvalid"));
            byte[]? screenshotBytes = screenshotEntryName is null
                ? null
                : ReadAndVerifyArchiveEntry(entries[screenshotEntryName], manifest, MaxScreenshotBytes);

            return Result<ArchivePayload>.Ok(new ArchivePayload(manifest, saveBytes, screenshotBytes));
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            return Result<ArchivePayload>.Fail(Strings.Get("Save_Error_ArchiveInvalidDetail", ex.Message));
        }
    }

    private static byte[] ReadAndVerifyArchiveEntry(
        ZipArchiveEntry entry,
        SaveArchiveManifest manifest,
        long maximumLength)
    {
        SaveArchiveFile? descriptor = manifest.Files.SingleOrDefault(file => file.Entry == entry.FullName);
        if (descriptor is null || entry.Length < 0 || entry.Length > maximumLength || descriptor.Length != entry.Length)
            throw new InvalidDataException(Strings.Get("Save_Error_ArchiveInvalid"));

        using Stream input = entry.Open();
        using var output = new MemoryStream(entry.Length > int.MaxValue ? 0 : (int)entry.Length);
        input.CopyTo(output);
        byte[] bytes = output.ToArray();
        if (bytes.LongLength != descriptor.Length || !Sha256(bytes).Equals(descriptor.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(Strings.Get("Save_Error_ArchiveChecksum", entry.FullName));
        return bytes;
    }

    private static SaveArchiveFile DescribeArchiveFile(string entry, byte[] bytes) => new()
    {
        Entry = entry,
        Length = bytes.LongLength,
        Sha256 = Sha256(bytes)
    };

    private static void WriteEntry(ZipArchive archive, string entryName, byte[] bytes)
    {
        ZipArchiveEntry entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        using Stream output = entry.Open();
        output.Write(bytes);
    }

    private static byte[] ReadBoundedFile(string path, long maximumLength, string tooLargeKey)
    {
        var info = new FileInfo(path);
        if (info.Length <= 0 || info.Length > maximumLength)
            throw new InvalidDataException(Strings.Get(tooLargeKey, info.Length, maximumLength));
        return File.ReadAllBytes(path);
    }

    private static string ChooseAvailableSaveFileName(string profileDirectory, string preferredName)
    {
        string preferredPath = Path.Combine(profileDirectory, preferredName);
        if (!File.Exists(preferredPath)) return preferredName;

        var usedNumbers = new HashSet<int>();
        foreach (string path in Directory.EnumerateFiles(profileDirectory))
        {
            if (!Path.GetExtension(path).Equals(".adv", StringComparison.OrdinalIgnoreCase)) continue;
            if (int.TryParse(Path.GetFileNameWithoutExtension(path), out int number) && number > 0)
                usedNumbers.Add(number);
        }
        int slot = 1;
        while (usedNumbers.Contains(slot)) slot++;
        return slot.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".adv";
    }

    private static string? ReadDefaultProfile(string profilesRoot)
    {
        string path = Path.Combine(profilesRoot, "profiles.ini");
        if (!File.Exists(path)) return null;
        foreach (string rawLine in File.ReadLines(path))
        {
            string line = rawLine.Trim();
            if (!line.StartsWith("default=", StringComparison.OrdinalIgnoreCase)) continue;
            string value = line["default=".Length..].Trim().Replace('\\', '/').TrimEnd('/');
            string candidate = value[(value.LastIndexOf('/') + 1)..];
            return IsSimpleName(candidate) ? candidate : null;
        }
        return null;
    }

    private static string NormalizeSaveFileName(string saveFileName)
    {
        string trimmed = saveFileName.Trim();
        return string.IsNullOrEmpty(Path.GetExtension(trimmed)) ? trimmed + ".adv" : trimmed;
    }

    private static bool IsSimpleName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value is "." or "..") return false;
        if (!string.Equals(value, Path.GetFileName(value), StringComparison.Ordinal)) return false;
        return value.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
    }

    private static string SanitizeToken(string value)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        var chars = value.Select(c => invalid.Contains(c) ? '_' : c).ToArray();
        string sanitized = new(chars);
        return string.IsNullOrWhiteSpace(sanitized) ? "save" : sanitized;
    }

    private static string Sha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    private static int ParseBoundedInt(string? value, int minimum, int maximum, int fallback)
    {
        return int.TryParse(value, System.Globalization.NumberStyles.Integer,
                   System.Globalization.CultureInfo.InvariantCulture, out int parsed) &&
               parsed >= minimum && parsed <= maximum
            ? parsed
            : fallback;
    }

    private static bool TryParseBoundedInt(string? value, int minimum, int maximum, out int parsed)
    {
        return int.TryParse(value, System.Globalization.NumberStyles.Integer,
                   System.Globalization.CultureInfo.InvariantCulture, out parsed) &&
               parsed >= minimum && parsed <= maximum;
    }

    private static bool IsRegularFile(string path)
    {
        if (!File.Exists(path)) return false;
        try { return (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0; }
        catch { return false; }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private sealed record ArchivePayload(SaveArchiveManifest Manifest, byte[] SaveBytes, byte[]? ScreenshotBytes);
}
