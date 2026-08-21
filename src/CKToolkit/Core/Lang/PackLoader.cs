using System.Reflection;
using System.Text;
using CKToolkit.Core.Common;

namespace CKToolkit.Core.Lang;

/// <summary>
/// 語言包載入與探索器 (SPEC.md §6.2, PHASE3.md)。
/// 負責從內嵌資源或磁碟目錄載入 pack.json 及各翻譯字典檔案。
/// </summary>
public static class PackLoader
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    /// <summary>
    /// 從組件內嵌資源載入語言包（如內建之 zh-TW 繁體中文語言包）。
    /// </summary>
    public static Result<LanguagePack> LoadEmbeddedPack(string packId = "zh-TW", Assembly? asm = null)
    {
        var targetAsm = asm ?? typeof(PackLoader).Assembly;
        var resourceNames = targetAsm.GetManifestResourceNames();

        string packJsonResName = FindResource(resourceNames, packId, "pack.json");
        if (string.IsNullOrEmpty(packJsonResName))
        {
            return Result<LanguagePack>.Fail($"內嵌資源中找不到語言包 '{packId}' 之 pack.json", ExitCodes.InvalidArgs);
        }

        string packJsonText = ReadResourceText(targetAsm, packJsonResName);
        var metaRes = LanguagePack.ParseMeta(packJsonText);
        if (!metaRes.Success)
        {
            return Result<LanguagePack>.Fail(metaRes.ErrorMessage!, metaRes.ExitCode);
        }

        var meta = metaRes.Value!;
        var pack = new LanguagePack
        {
            Meta = meta,
            IsBuiltIn = true,
            SourcePath = $"embedded:{packId}"
        };

        // 載入 ui.json
        if (!string.IsNullOrWhiteSpace(meta.Files.Ui))
        {
            string uiRes = FindResource(resourceNames, packId, meta.Files.Ui);
            if (!string.IsNullOrEmpty(uiRes))
            {
                pack.Translations.Merge(
                    Path.GetFileNameWithoutExtension(meta.Files.Ui),
                    ReadResourceText(targetAsm, uiRes),
                    $"內嵌:{meta.Files.Ui}");
            }
        }

        // 載入 help.json
        if (!string.IsNullOrWhiteSpace(meta.Files.Help))
        {
            string helpRes = FindResource(resourceNames, packId, meta.Files.Help);
            if (!string.IsNullOrEmpty(helpRes))
            {
                pack.Translations.Merge(
                    "help",
                    ReadResourceText(targetAsm, helpRes),
                    $"內嵌:{meta.Files.Help}");
            }
        }

        // 載入 campaigns
        if (meta.Files.Campaigns is not null)
        {
            foreach (string cName in meta.Files.Campaigns)
            {
                string cRes = FindResource(resourceNames, packId, cName);
                if (!string.IsNullOrEmpty(cRes))
                {
                    pack.Translations.Merge(
                        Path.GetFileNameWithoutExtension(cName),
                        ReadResourceText(targetAsm, cRes),
                        $"內嵌:{cName}");
                }
            }
        }

        // 檢查是否有內嵌之 credits.txt
        string creditsRes = FindResource(resourceNames, packId, "credits.txt");
        if (!string.IsNullOrEmpty(creditsRes))
        {
            pack.Translations.Credits = ReadResourceText(targetAsm, creditsRes);
        }

        return Result<LanguagePack>.Ok(pack);
    }

    /// <summary>
    /// 從外部目錄載入語言包。
    /// </summary>
    public static Result<LanguagePack> LoadFromDirectory(string dirPath)
    {
        if (!Directory.Exists(dirPath))
        {
            return Result<LanguagePack>.Fail($"語言包目錄不存在：{dirPath}", ExitCodes.InvalidArgs);
        }

        string packJsonPath = Path.Combine(dirPath, "pack.json");
        if (!File.Exists(packJsonPath))
        {
            return Result<LanguagePack>.Fail($"目錄缺少 pack.json：{packJsonPath}", ExitCodes.InvalidArgs);
        }

        string packJsonText;
        try
        {
            packJsonText = File.ReadAllText(packJsonPath, Utf8NoBom);
        }
        catch (Exception ex)
        {
            return Result<LanguagePack>.Fail($"讀取 pack.json 失敗：{ex.Message}", ExitCodes.GeneralFailure);
        }

        var metaRes = LanguagePack.ParseMeta(packJsonText);
        if (!metaRes.Success)
        {
            return Result<LanguagePack>.Fail(metaRes.ErrorMessage!, metaRes.ExitCode);
        }

        var meta = metaRes.Value!;
        var pack = new LanguagePack
        {
            Meta = meta,
            IsBuiltIn = false,
            SourcePath = Path.GetFullPath(dirPath)
        };

        // 載入 ui.json
        if (!string.IsNullOrWhiteSpace(meta.Files.Ui))
        {
            string uiPath = Path.Combine(dirPath, meta.Files.Ui);
            if (File.Exists(uiPath))
            {
                pack.Translations.Merge(
                    Path.GetFileNameWithoutExtension(meta.Files.Ui),
                    File.ReadAllText(uiPath, Utf8NoBom),
                    meta.Files.Ui);
            }
        }

        // 載入 help.json
        if (!string.IsNullOrWhiteSpace(meta.Files.Help))
        {
            string helpPath = Path.Combine(dirPath, meta.Files.Help);
            if (File.Exists(helpPath))
            {
                pack.Translations.Merge(
                    "help",
                    File.ReadAllText(helpPath, Utf8NoBom),
                    meta.Files.Help);
            }
        }

        // 載入 campaigns
        if (meta.Files.Campaigns is not null)
        {
            foreach (string cName in meta.Files.Campaigns)
            {
                string cPath = Path.Combine(dirPath, cName);
                if (File.Exists(cPath))
                {
                    pack.Translations.Merge(
                        Path.GetFileNameWithoutExtension(cName),
                        File.ReadAllText(cPath, Utf8NoBom),
                        cName);
                }
            }
        }

        // 載入 credits.txt
        string creditsPath = Path.Combine(dirPath, "credits.txt");
        if (File.Exists(creditsPath))
        {
            pack.Translations.Credits = File.ReadAllText(creditsPath, Utf8NoBom);
        }

        return Result<LanguagePack>.Ok(pack);
    }

    /// <summary>
    /// 探索並載入所有可用語言包（包含內嵌繁中 zh-TW 與外部 langpacks/ 目錄）。
    /// 同 ID 之外部語言包會覆寫內嵌語言包。
    /// </summary>
    public static Dictionary<string, LanguagePack> DiscoverAll(string? customBaseDir = null)
    {
        var packs = new Dictionary<string, LanguagePack>(StringComparer.OrdinalIgnoreCase);

        // 1. 載入內建 zh-TW
        var builtInRes = LoadEmbeddedPack("zh-TW");
        if (builtInRes.Success && builtInRes.Value is not null)
        {
            packs[builtInRes.Value.Meta.Id] = builtInRes.Value;
        }

        // 2. 掃描外部 langpacks 目錄
        string baseDir = customBaseDir ?? AppContext.BaseDirectory;
        string langpacksDir = Path.Combine(baseDir, "langpacks");

        if (Directory.Exists(langpacksDir))
        {
            foreach (string subDir in Directory.GetDirectories(langpacksDir))
            {
                var directory = new DirectoryInfo(subDir);
                // 匯入管線的 staging / rollback 目錄以點號開頭；復原失敗時必須保留，
                // 但絕不能被當成正式語言包載入。手動放入的 reparse point 也不追蹤。
                if (directory.Name.StartsWith(".", StringComparison.Ordinal) ||
                    (directory.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    continue;
                }

                string subPackJson = Path.Combine(subDir, "pack.json");
                if (File.Exists(subPackJson))
                {
                    var loaded = LoadFromDirectory(subDir);
                    if (loaded.Success && loaded.Value is not null)
                    {
                        packs[loaded.Value.Meta.Id] = loaded.Value;
                    }
                }
            }
        }

        return packs;
    }

    private static string FindResource(string[] names, string packId, string fileName)
    {
        string normPackId = packId.Replace('-', '_');
        string normFile = fileName.Replace('\\', '/');

        // 優先完全比對包含 packId 與 fileName 的資源名稱
        foreach (string n in names)
        {
            string clean = n.Replace('\\', '/');
            if (clean.Contains(packId, StringComparison.OrdinalIgnoreCase) &&
                clean.EndsWith(fileName, StringComparison.OrdinalIgnoreCase))
            {
                return n;
            }
            if (clean.Contains(normPackId, StringComparison.OrdinalIgnoreCase) &&
                clean.EndsWith(fileName, StringComparison.OrdinalIgnoreCase))
            {
                return n;
            }
        }

        return string.Empty;
    }

    private static string ReadResourceText(Assembly asm, string resourceName)
    {
        using var stream = asm.GetManifestResourceStream(resourceName);
        if (stream is null) return string.Empty;
        using var reader = new StreamReader(stream, Utf8NoBom);
        return reader.ReadToEnd();
    }
}
