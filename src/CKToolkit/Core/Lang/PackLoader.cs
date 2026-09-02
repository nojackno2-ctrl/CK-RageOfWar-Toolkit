using System.Reflection;
using System.Text;
using CKToolkit.Core.Common;
using CKToolkit.I18n;

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
            return Result<LanguagePack>.Fail(Strings.Get("Error_LangPackEmbeddedNotFound", packId), ExitCodes.InvalidArgs);
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
            return Result<LanguagePack>.Fail(Strings.Get("Error_LangPackDirMissing", dirPath), ExitCodes.InvalidArgs);
        }

        string packJsonPath = Path.Combine(dirPath, "pack.json");
        if (!File.Exists(packJsonPath))
        {
            return Result<LanguagePack>.Fail(Strings.Get("Error_LangPackJsonMissing", packJsonPath), ExitCodes.InvalidArgs);
        }

        string packJsonText;
        try
        {
            packJsonText = File.ReadAllText(packJsonPath, Utf8NoBom);
        }
        catch (Exception ex)
        {
            return Result<LanguagePack>.Fail(Strings.Get("Error_LangPackJsonReadFailed", ex.Message), ExitCodes.GeneralFailure);
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
    /// 內嵌語言包快取。組件的內嵌資源在執行期是不可變的，因此只在第一次存取時掃描解析；
    /// 外部 langpacks/ 目錄則每次重新掃描，以便匯入的語言包立即生效。
    /// </summary>
    private static readonly Lazy<Dictionary<string, LanguagePack>> EmbeddedPacks =
        new(LoadAllEmbeddedPacks, isThreadSafe: true);

    private const string EmbeddedPackPrefix = "CKToolkit.LangPacks.";
    private const string EmbeddedPackManifest = "/pack.json";

    /// <summary>
    /// 純動態掃描組件內嵌資源，找出所有內建語言包。
    /// 這裡刻意不保留任何語言 ID 白名單：新增語言只要把資料夾放進 assets/langpacks/，
    /// csproj 的萬用字元就會嵌入它、這裡就會自動發現它，一行程式碼都不必改（AGENTS.md §1）。
    /// </summary>
    private static Dictionary<string, LanguagePack> LoadAllEmbeddedPacks()
    {
        var packs = new Dictionary<string, LanguagePack>(StringComparer.OrdinalIgnoreCase);
        var targetAsm = typeof(PackLoader).Assembly;

        foreach (string res in targetAsm.GetManifestResourceNames())
        {
            string clean = res.Replace('\\', '/');
            if (!clean.StartsWith(EmbeddedPackPrefix, StringComparison.OrdinalIgnoreCase) ||
                !clean.EndsWith(EmbeddedPackManifest, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string packId = clean.Substring(
                EmbeddedPackPrefix.Length,
                clean.Length - EmbeddedPackPrefix.Length - EmbeddedPackManifest.Length);
            if (string.IsNullOrWhiteSpace(packId)) continue;

            var loaded = LoadEmbeddedPack(packId, targetAsm);
            if (loaded.Success && loaded.Value is not null)
            {
                packs[loaded.Value.Meta.Id] = loaded.Value;
            }
        }

        return packs;
    }

    /// <summary>
    /// 語言包 ID -> 遊戲端語系身分（local.pak 的語系資料夾、vxSettings.ini 的 [Language] Default）。
    ///
    /// 這是整個專案唯一一處把「工具箱的語言包 ID」翻成「遊戲看得懂的語系名稱」的地方。
    /// 從前 PatchPipeline 與 LangModule 各自硬編 zh-TW -> CHINESE/chinese，於是 zh-CN、ja-JP、
    /// es-ES、it-IT、ru-RU 安裝後 verify 永遠回報「設定不符」——實際寫入的是 pack.json 的
    /// gameLangFolder（例如 SCHINESE），期望值卻拿語言包 ID（zh-CN）去比。
    /// 任何需要這組名稱的程式碼一律走這個函式，不得再自行推導。
    /// </summary>
    /// <returns>Folder 為大寫語系資料夾名，Key 為小寫 vxSettings 語系代號；packId 為空時回傳兩個空字串。</returns>
    public static (string Folder, string Key) ResolveGameLangIdentity(string? packId)
    {
        if (string.IsNullOrWhiteSpace(packId)) return (string.Empty, string.Empty);

        string id = packId.Trim();
        if (DiscoverAll().TryGetValue(id, out var pack) &&
            !string.IsNullOrWhiteSpace(pack.Meta.GameLangFolder) &&
            !string.IsNullOrWhiteSpace(pack.Meta.GameLangKey))
        {
            return (pack.Meta.GameLangFolder.ToUpperInvariant(), pack.Meta.GameLangKey.ToLowerInvariant());
        }

        // 語言包不在清單中（外部包被刪掉，或設定檔指向不存在的 ID）。此時沒有權威來源可查，
        // 只能退回以 ID 本身推導——重點是「期望」與「實際」兩邊都走同一條退路，
        // 才不會又製造出假的不符警告。
        return (id.ToUpperInvariant(), id.ToLowerInvariant());
    }

    /// <summary>
    /// 探索並載入所有可用語言包（所有內嵌語言包 + 外部 langpacks/ 目錄）。
    /// 同 ID 之外部語言包會覆寫內嵌語言包。
    /// </summary>
    public static Dictionary<string, LanguagePack> DiscoverAll(string? customBaseDir = null)
    {
        // 1. 內嵌語言包：組件內容在執行期不會變，解析一次就夠。
        //    每個內嵌包要反序列化約 3,458 條翻譯，而一次 apply 就會呼叫 DiscoverAll 兩次
        //    (ApplyLocalPak + ApplyVxSettings)，不快取等於重複做數萬次 JSON 解析。
        var packs = new Dictionary<string, LanguagePack>(EmbeddedPacks.Value, StringComparer.OrdinalIgnoreCase);

        // 2. 掃描外部 langpacks 目錄（每次重新掃描，讓匯入的語言包立即生效）
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
