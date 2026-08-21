using System.Text.RegularExpressions;
using CKToolkit.Core.Common;
using CKToolkit.I18n;

namespace CKToolkit.Core.Lang;

/// <summary>
/// 語言包安全驗證、匯入與管理服務。
/// 驗證宣告路徑之後才允許 PackLoader 讀檔，並以 staging + rollback 取代就地覆寫。
/// </summary>
public static partial class LangPackService
{
    private const int MaxPackIdLength = 64;

    [GeneratedRegex("^[a-zA-Z0-9_-]+$", RegexOptions.Compiled)]
    private static partial Regex ValidPackIdRegex();

    /// <summary>語言包 ID 只允許未經修剪的 ASCII 英數、底線與連字號。</summary>
    public static bool IsValidPackId(string? packId)
    {
        if (string.IsNullOrWhiteSpace(packId) || packId.Length > MaxPackIdLength) return false;
        if (!string.Equals(packId, packId.Trim(), StringComparison.Ordinal)) return false;
        if (packId.Contains("..", StringComparison.Ordinal)) return false;
        if (packId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return false;
        return ValidPackIdRegex().IsMatch(packId);
    }

    /// <summary>
    /// 先只解析 pack.json，再驗證每個宣告路徑，最後才讓 PackLoader 讀取翻譯內容。
    /// </summary>
    public static Result<LanguagePack> ValidatePackDirectory(string sourceDir)
    {
        if (string.IsNullOrWhiteSpace(sourceDir) || !Directory.Exists(sourceDir))
        {
            return Result<LanguagePack>.Fail(
                Strings.Get("Error_LangImportDirectoryMissing", sourceDir), ExitCodes.InvalidArgs);
        }

        string fullSourceDir;
        try
        {
            fullSourceDir = Path.GetFullPath(sourceDir)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch (Exception ex)
        {
            return Result<LanguagePack>.Fail(
                Strings.Get("Error_LangImportValidationFailed", ex.Message), ExitCodes.InvalidArgs);
        }

        if (HasReparsePoint(fullSourceDir, fullSourceDir, out string? reparsePath))
        {
            return Result<LanguagePack>.Fail(
                Strings.Get("Error_LangImportReparsePoint", reparsePath ?? fullSourceDir), ExitCodes.InvalidArgs);
        }

        string packJsonPath = Path.Combine(fullSourceDir, "pack.json");
        if (!File.Exists(packJsonPath))
        {
            return Result<LanguagePack>.Fail(
                Strings.Get("Error_LangImportPackJsonMissing", packJsonPath), ExitCodes.InvalidArgs);
        }
        if (HasReparsePoint(fullSourceDir, packJsonPath, out reparsePath))
        {
            return Result<LanguagePack>.Fail(
                Strings.Get("Error_LangImportReparsePoint", reparsePath ?? packJsonPath), ExitCodes.InvalidArgs);
        }

        Result<LanguagePackMeta> metaResult;
        try
        {
            metaResult = LanguagePack.ParseMeta(File.ReadAllText(packJsonPath));
        }
        catch (Exception ex)
        {
            return Result<LanguagePack>.Fail(
                Strings.Get("Error_LangImportValidationFailed", ex.Message), ExitCodes.InvalidArgs);
        }

        if (!metaResult.Success || metaResult.Value is null)
        {
            return Result<LanguagePack>.Fail(
                metaResult.ErrorMessage ?? Strings.Get("Error_LangImportValidationFailed", "pack.json"),
                metaResult.ExitCode);
        }

        LanguagePackMeta meta = metaResult.Value;
        if (!IsValidPackId(meta.Id))
        {
            return Result<LanguagePack>.Fail(
                Strings.Get("Error_LangImportInvalidPackId", meta.Id), ExitCodes.InvalidArgs);
        }

        var declaredFiles = new List<string> { meta.Files.Ui };
        if (!string.IsNullOrWhiteSpace(meta.Files.Help)) declaredFiles.Add(meta.Files.Help);
        if (meta.Files.Campaigns is { Count: > 0 })
            declaredFiles.AddRange(meta.Files.Campaigns.Where(c => !string.IsNullOrWhiteSpace(c)));

        foreach (string relativePath in declaredFiles.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            Result<string> pathResult = ResolvePackFile(fullSourceDir, relativePath, required: true);
            if (!pathResult.Success)
                return Result<LanguagePack>.Fail(pathResult.ErrorMessage!, pathResult.ExitCode);
        }

        foreach (string optionalName in new[] { "credits.txt", "glossary.md" })
        {
            string optionalPath = Path.Combine(fullSourceDir, optionalName);
            if (!File.Exists(optionalPath)) continue;
            Result<string> pathResult = ResolvePackFile(fullSourceDir, optionalName, required: false);
            if (!pathResult.Success)
                return Result<LanguagePack>.Fail(pathResult.ErrorMessage!, pathResult.ExitCode);
        }

        try
        {
            Result<LanguagePack> loadResult = PackLoader.LoadFromDirectory(fullSourceDir);
            if (!loadResult.Success || loadResult.Value is null)
            {
                return Result<LanguagePack>.Fail(
                    loadResult.ErrorMessage ?? Strings.Get("Error_LangImportValidationFailed", "pack.json"),
                    loadResult.ExitCode);
            }
            return loadResult;
        }
        catch (Exception ex)
        {
            return Result<LanguagePack>.Fail(
                Strings.Get("Error_LangImportValidationFailed", ex.Message), ExitCodes.InvalidArgs);
        }
    }

    /// <summary>
    /// 安全匯入至 langpacks/&lt;pack-id&gt;。既有目標必須明確同意覆寫；復原失敗時保留 rollback。
    /// </summary>
    public static Result<LanguagePack> ImportPack(
        string sourceDir,
        string? customTargetBaseDir = null,
        Func<string, string, bool>? overwritePrompt = null)
    {
        Result<LanguagePack> validation = ValidatePackDirectory(sourceDir);
        if (!validation.Success || validation.Value is null) return validation;

        LanguagePack pack = validation.Value;
        string fullBase;
        string langpacksDir;
        string fullSource;
        string fullTarget;
        try
        {
            fullBase = Path.GetFullPath(customTargetBaseDir ?? AppContext.BaseDirectory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            langpacksDir = Path.Combine(fullBase, "langpacks");
            fullSource = Path.GetFullPath(sourceDir)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            fullTarget = Path.GetFullPath(Path.Combine(langpacksDir, pack.Meta.Id))
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch (Exception ex)
        {
            return Result<LanguagePack>.Fail(
                Strings.Get("Error_LangImportFailed", ex.Message), ExitCodes.InvalidArgs);
        }

        if (string.Equals(fullSource, fullTarget, StringComparison.OrdinalIgnoreCase))
        {
            return Result<LanguagePack>.Fail(
                Strings.Get("Error_LangImportSamePath"), ExitCodes.InvalidArgs);
        }

        bool targetExisted = Directory.Exists(fullTarget);
        if (targetExisted)
        {
            if (HasReparsePoint(fullTarget, fullTarget, out string? targetReparse))
            {
                return Result<LanguagePack>.Fail(
                    Strings.Get("Error_LangImportReparsePoint", targetReparse ?? fullTarget), ExitCodes.InvalidArgs);
            }
            if (overwritePrompt is null)
            {
                return Result<LanguagePack>.Fail(
                    Strings.Get("Error_LangImportTargetExists", fullTarget), ExitCodes.InvalidArgs);
            }
            if (!overwritePrompt(pack.Meta.Id, fullTarget))
            {
                return Result<LanguagePack>.Fail(
                    Strings.Get("Error_LangImportCancelled"), ExitCodes.Success);
            }
        }

        try
        {
            Directory.CreateDirectory(langpacksDir);
        }
        catch (Exception ex)
        {
            return Result<LanguagePack>.Fail(
                Strings.Get("Error_LangImportFailed", ex.Message), ExitCodes.GeneralFailure);
        }
        if (HasReparsePoint(fullBase, langpacksDir, out string? langpacksReparse))
        {
            return Result<LanguagePack>.Fail(
                Strings.Get("Error_LangImportReparsePoint", langpacksReparse ?? langpacksDir), ExitCodes.InvalidArgs);
        }

        string stagingDir = Path.Combine(langpacksDir, $".staging_{pack.Meta.Id}_{Guid.NewGuid():N}");
        string? rollbackDir = null;

        try
        {
            Directory.CreateDirectory(stagingDir);
            CopyPackFile(fullSource, stagingDir, "pack.json");
            CopyPackFile(fullSource, stagingDir, pack.Meta.Files.Ui);
            if (!string.IsNullOrWhiteSpace(pack.Meta.Files.Help))
                CopyPackFile(fullSource, stagingDir, pack.Meta.Files.Help);
            foreach (string campaign in pack.Meta.Files.Campaigns ?? [])
                CopyPackFile(fullSource, stagingDir, campaign);
            CopyPackFileIfPresent(fullSource, stagingDir, "credits.txt");
            CopyPackFileIfPresent(fullSource, stagingDir, "glossary.md");

            Result<LanguagePack> stagedValidation = ValidatePackDirectory(stagingDir);
            if (!stagedValidation.Success)
            {
                throw new InvalidOperationException(
                    Strings.Get("Error_LangImportStagingInvalid", stagedValidation.ErrorMessage ?? "pack.json"));
            }

            if (targetExisted)
            {
                rollbackDir = Path.Combine(langpacksDir, $".rollback_{pack.Meta.Id}_{Guid.NewGuid():N}");
                Directory.Move(fullTarget, rollbackDir);
            }

            Directory.Move(stagingDir, fullTarget);
            Result<LanguagePack> installed = ValidatePackDirectory(fullTarget);
            if (!installed.Success || installed.Value is null)
            {
                throw new InvalidOperationException(
                    Strings.Get("Error_LangImportStagingInvalid", installed.ErrorMessage ?? "pack.json"));
            }

            var warnings = new List<string>();
            if (rollbackDir is not null && Directory.Exists(rollbackDir))
            {
                try
                {
                    Directory.Delete(rollbackDir, recursive: true);
                    rollbackDir = null;
                }
                catch
                {
                    warnings.Add(Strings.Get("Warning_LangImportRollbackCleanup", rollbackDir ?? "-"));
                }
            }

            return Result<LanguagePack>.Ok(installed.Value, warnings);
        }
        catch (Exception ex)
        {
            string? recoveryFailure = null;
            if (targetExisted && rollbackDir is not null && Directory.Exists(rollbackDir))
            {
                try
                {
                    if (Directory.Exists(fullTarget)) Directory.Delete(fullTarget, recursive: true);
                    Directory.Move(rollbackDir, fullTarget);
                    rollbackDir = null;
                }
                catch (Exception recoveryEx)
                {
                    recoveryFailure = Strings.Get(
                        "Error_LangImportRecoveryFailed", rollbackDir ?? "-", recoveryEx.Message);
                }
            }
            else if (!targetExisted && Directory.Exists(fullTarget))
            {
                try { Directory.Delete(fullTarget, recursive: true); } catch { }
            }

            string message = Strings.Get("Error_LangImportFailed", ex.Message);
            if (recoveryFailure is not null) message += Environment.NewLine + recoveryFailure;
            return Result<LanguagePack>.Fail(message, ExitCodes.GeneralFailure);
        }
        finally
        {
            if (Directory.Exists(stagingDir))
            {
                try { Directory.Delete(stagingDir, recursive: true); } catch { }
            }
            // rollbackDir 刻意不在 finally 刪除；復原失敗時它是唯一可恢復的舊版本。
        }
    }

    private static Result<string> ResolvePackFile(string sourceRoot, string relativePath, bool required)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathFullyQualified(relativePath))
        {
            return Result<string>.Fail(
                Strings.Get("Error_LangImportPathTraversal", relativePath), ExitCodes.InvalidArgs);
        }

        string[] segments = relativePath.Split(
            new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
            StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || segments.Any(s => s is "." or ".."))
        {
            return Result<string>.Fail(
                Strings.Get("Error_LangImportPathTraversal", relativePath), ExitCodes.InvalidArgs);
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(Path.Combine(sourceRoot, relativePath));
        }
        catch
        {
            return Result<string>.Fail(
                Strings.Get("Error_LangImportPathTraversal", relativePath), ExitCodes.InvalidArgs);
        }

        string sourcePrefix = sourceRoot.TrimEnd(
            Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(sourcePrefix, StringComparison.OrdinalIgnoreCase))
        {
            return Result<string>.Fail(
                Strings.Get("Error_LangImportPathTraversal", relativePath), ExitCodes.InvalidArgs);
        }
        if (required && !File.Exists(fullPath))
        {
            return Result<string>.Fail(
                Strings.Get("Error_LangImportFileNotFound", relativePath), ExitCodes.InvalidArgs);
        }
        if (File.Exists(fullPath) && HasReparsePoint(sourceRoot, fullPath, out string? reparsePath))
        {
            return Result<string>.Fail(
                Strings.Get("Error_LangImportReparsePoint", reparsePath ?? fullPath), ExitCodes.InvalidArgs);
        }

        return Result<string>.Ok(fullPath);
    }

    private static bool HasReparsePoint(string rootPath, string targetPath, out string? offendingPath)
    {
        try
        {
            offendingPath = null;
            string current = Path.GetFullPath(rootPath);
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                offendingPath = current;
                return true;
            }

            string relative = Path.GetRelativePath(current, Path.GetFullPath(targetPath));
            foreach (string segment in relative.Split(
                         new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                         StringSplitOptions.RemoveEmptyEntries))
            {
                current = Path.Combine(current, segment);
                if (!Directory.Exists(current) && !File.Exists(current)) continue;
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) == 0) continue;
                offendingPath = current;
                return true;
            }
            return false;
        }
        catch
        {
            // 權限或屬性讀取失敗時採 fail-closed；不可在無法確認的路徑上匯入。
            offendingPath = targetPath;
            return true;
        }
    }

    private static void CopyPackFile(string sourceBase, string targetBase, string relativePath)
    {
        string source = Path.Combine(sourceBase, relativePath);
        string target = Path.Combine(targetBase, relativePath);
        string? targetDirectory = Path.GetDirectoryName(target);
        if (!string.IsNullOrEmpty(targetDirectory)) Directory.CreateDirectory(targetDirectory);
        File.Copy(source, target, overwrite: true);
    }

    private static void CopyPackFileIfPresent(string sourceBase, string targetBase, string relativePath)
    {
        if (File.Exists(Path.Combine(sourceBase, relativePath)))
            CopyPackFile(sourceBase, targetBase, relativePath);
    }
}
