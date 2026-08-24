using System.Diagnostics;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using CKToolkit.Core.Common;
using CKToolkit.I18n;

namespace CKToolkit.Core.Lang;

/// <summary>
/// 語言包安裝、還原、狀態查詢與範本匯出核心 (SPEC.md §6, PHASE3.md)。
/// </summary>
public static class LangInstaller
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    /// <summary>
    /// 遊戲原廠內建語系資料夾清單。
    ///
    /// ⚠ 這份清單是「哪些語系目錄不是我們裝的」的唯一依據，寫錯會直接害到使用者：
    /// 少列一個原廠語系，完全原版的 local.pak 就會被當成裝了語言包，
    /// 進而被 <c>PatchState.InspectLocalPak</c> 判為 Unrecognised 而拒絕操作。
    ///
    /// 內容以 Steam 版原版 local.pak 實際列舉為準（924 個項目）：
    ///   ADVENTURES 835、SCENARIOS 60、FONTS 12、BULGARIAN 5、FRENCH 5、GERMAN 5、ENGLISH 2
    /// 特別注意 BULGARIAN——它確實是原廠內容，早期版本漏列導致原版遊戲被誤判。
    /// SPANISH / ITALIAN / RUSSIAN 在 Steam 版並不存在，保留是為了容納其他發行版本。
    ///
    /// SelfTest 有一組測試會拿真實 local.pak 逐一比對根目錄，漏列會直接測試失敗。
    /// 不要憑印象增刪這份清單。
    /// </summary>
    public static readonly HashSet<string> StockLanguages = new(StringComparer.OrdinalIgnoreCase)
    {
        "ENGLISH", "GERMAN", "FRENCH", "BULGARIAN", "SPANISH", "ITALIAN", "RUSSIAN"
    };

    /// <summary>
    /// pak 內部非語系之標準根目錄名稱。
    /// </summary>
    public static readonly HashSet<string> NonLanguageRoots = new(StringComparer.OrdinalIgnoreCase)
    {
        "FONTS", "ADVENTURES", "SCENARIOS", "DATA", "GFX", "SOUNDS", "MUSIC"
    };

    /// <summary>
    /// local.pak 內部之字型修補清冊檔案名稱 (SPEC.md §6)。
    /// </summary>
    public const string MarkerPath = @"FONTS\.patch_marker.json";

    private static readonly JsonSerializerOptions ManifestJsonOpts = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>
    /// 將語言包安裝進 local.pak。
    /// </summary>
    public static void Install(
        HmmPak localPak,
        LanguagePack pack,
        string? fontFaceOverride = null,
        Action<string>? log = null)
    {
        // 若先前已安裝過語言包，先完整還原以確保乾淨基礎
        if (localPak.Contains(MarkerPath) || GetInstalledLanguages(localPak).Count > 0)
        {
            Uninstall(localPak);
        }

        string templateLang = string.IsNullOrWhiteSpace(pack.Meta.TemplateLang) ? "GERMAN" : pack.Meta.TemplateLang.ToUpperInvariant();
        string targetLang = pack.Meta.GameLangFolder.ToUpperInvariant();

        // 若模板語言為 ENGLISH 但 local.pak 中無 ENGLISH\*.XML 翻譯表（如 Steam 原版），
        // 回退使用第一個包含翻譯表的官方語言（如 GERMAN）作為結構底本
        if (templateLang.Equals("ENGLISH", StringComparison.OrdinalIgnoreCase))
        {
            if (!HasTranslationTables(localPak, "ENGLISH"))
            {
                var detectedStock = DetectStockLanguages(localPak);
                string? fallback = FindStructureTemplateLanguage(localPak, detectedStock);
                templateLang = !string.IsNullOrEmpty(fallback) ? fallback : "GERMAN";
            }
        }

        log?.Invoke($"產生語言檔案 (來源模板: {templateLang}, 目標資料夾: {targetLang})...");

        // 1. 產生翻譯 XML 檔案
        int doneTotal = 0, entryTotal = 0;
        var existingEntries = localPak.Entries.ToList();
        var addedEntries = new List<string>();

        foreach (var entry in existingEntries)
        {
            string[] parts = entry.Name.Split('\\');
            string newName;

            if (parts[0].Equals(templateLang, StringComparison.OrdinalIgnoreCase))
            {
                newName = targetLang + "\\" + string.Join("\\", parts, 1, parts.Length - 1);
            }
            else if (parts.Length > 2 && parts[2].Equals(templateLang, StringComparison.OrdinalIgnoreCase))
            {
                var copy = (string[])parts.Clone();
                copy[2] = targetLang;
                newName = string.Join("\\", copy);
            }
            else
            {
                continue;
            }

            byte[] data = localPak.Read(entry.Name);
            if (newName.EndsWith(".XML", StringComparison.OrdinalIgnoreCase) && LocXml.IsTranslationTable(data))
            {
                data = LocXml.Rebuild(data, pack.Translations.Lookup, out int d, out int t);
                doneTotal += d;
                entryTotal += t;
            }

            localPak.Write(newName, data, entry.MTime);
            addedEntries.Add(newName);
        }

        // 1.5 產生額外戰役/劇本 XML 檔案 (Return to the Throne, Defenders, Invaders, The Fall of Avalon, Ascendency)
        foreach (var (pakPrefix, relPath, templateData) in ExtraCampaignTemplates.GetTemplates())
        {
            string newName = $"{pakPrefix}\\{targetLang}\\{relPath}";
            byte[] data = templateData;
            if (newName.EndsWith(".XML", StringComparison.OrdinalIgnoreCase) && LocXml.IsTranslationTable(data))
            {
                data = LocXml.Rebuild(data, pack.Translations.Lookup, out int d, out int t);
                doneTotal += d;
                entryTotal += t;
            }

            localPak.Write(newName, data);
            addedEntries.Add(newName);
        }

        log?.Invoke($"  文字條目：{doneTotal}/{entryTotal} 已翻譯（其餘保留英文）");

        // 2. 說明文件 HELP.XML (以 ENGLISH\HELP.XML 為原文底本)
        if (localPak.Contains(@"ENGLISH\HELP.XML"))
        {
            byte[] engHelp = localPak.Read(@"ENGLISH\HELP.XML");
            byte[] helpBytes = LocXml.RebuildHelp(engHelp, pack.Translations.Help, out int hd, out int ht);
            localPak.Write(targetLang + @"\HELP.XML", helpBytes);
            addedEntries.Add(targetLang + @"\HELP.XML");
            log?.Invoke($"  說明文件：{hd}/{ht} 段已翻譯");
        }

        // 3. 工作人員名單 CREDITS.TXT
        if (!string.IsNullOrEmpty(pack.Translations.Credits))
        {
            localPak.Write(targetLang + @"\CREDITS.TXT", Utf8NoBom.GetBytes(pack.Translations.Credits));
            addedEntries.Add(targetLang + @"\CREDITS.TXT");
        }
        else if (localPak.Contains(@"ENGLISH\CREDITS.TXT"))
        {
            localPak.Write(targetLang + @"\CREDITS.TXT", localPak.Read(@"ENGLISH\CREDITS.TXT"));
            addedEntries.Add(targetLang + @"\CREDITS.TXT");
        }

        // 4. 光柵化字型並記錄修補清冊
        var charset = pack.GetAllCodepoints();
        string fontFace = !string.IsNullOrWhiteSpace(fontFaceOverride)
            ? fontFaceOverride
            : pack.Meta.Font.Face;

        if (string.IsNullOrWhiteSpace(fontFace))
        {
            fontFace = "微軟正黑體";
        }

        if (!GdiFont.Exists(fontFace) && pack.Meta.Font.FallbackFaces is { Count: > 0 })
        {
            foreach (string fb in pack.Meta.Font.FallbackFaces)
            {
                if (GdiFont.Exists(fb))
                {
                    fontFace = fb;
                    break;
                }
            }
        }

        log?.Invoke($"光柵化字型 (字體: {fontFace}, 字元數: {charset.Count})...");
        var sw = Stopwatch.StartNew();
        var missingTotal = new HashSet<int>();
        var manifest = new FontPatchManifest { PackId = pack.Meta.Id, AddedEntries = addedEntries };

        foreach (var entry in existingEntries)
        {
            if (!entry.Name.StartsWith("FONTS\\", StringComparison.OrdinalIgnoreCase)) continue;
            if (!entry.Name.EndsWith(".APF", StringComparison.OrdinalIgnoreCase)) continue;

            byte[] fontData = localPak.Read(entry.Name);
            var font = ApfFont.Load(fontData);

            // 管線在套用前一律先正規化，所以這裡拿到的字型應該已經是原版；
            // 這道保險只擋「同一個記憶體物件被重複加工」的情形。
            if (font.HasInMemoryAdditions)
            {
                font.StripAddedRanges();
            }

            var res = FontBuilder.AddGlyphs(
                font,
                charset,
                fontFace,
                pack.Meta.Font.SizeAdjust,
                font.Bold);

            foreach (int m in res.Missing) missingTotal.Add(m);

            manifest.Fonts[entry.Name] = res.PatchRecord;

            byte[] dumped = font.Dump();
            localPak.Write(entry.Name, dumped, entry.MTime);
            log?.Invoke($"  {entry.Name,-26} +{res.Added,5} 字形 ({dumped.Length / 1024.0:F1} KB)");
        }

        // 寫入自我描述之修補清冊 marker
        string manifestJson = JsonSerializer.Serialize(manifest, ManifestJsonOpts);
        localPak.WriteText(MarkerPath, manifestJson);

        log?.Invoke($"  字型光柵化完成，耗時 {sw.Elapsed.TotalSeconds:F1} 秒" +
                    (missingTotal.Count > 0 ? $"，缺字 {missingTotal.Count} 個" : ""));
    }

    /// <summary>
    /// 從 local.pak 移除所有非原廠之自訂語言包並依據修補清冊還原 APF 字型至 100% 原版。
    /// </summary>
    public static void Uninstall(HmmPak localPak)
    {
        // 1. 讀取修補清冊。清冊是「我們到底加了什麼」的權威來源，必須先讀。
        FontPatchManifest? manifest = null;
        if (localPak.Contains(MarkerPath))
        {
            try
            {
                string markerJson = localPak.ReadText(MarkerPath);
                manifest = JsonSerializer.Deserialize<FontPatchManifest>(markerJson);
            }
            catch
            {
                manifest = null;
            }
        }

        // 2. 移除安裝時新增的所有項目。
        //
        // 一律以清冊記錄為準，不要用「根目錄名稱不在原廠白名單內」之類的推測：
        // 語言檔案不是只放在頂層 CHINESE\，SCENARIOS\<地圖>\CHINESE\ 與
        // ADVENTURES\<戰役>\CHINESE\ 底下也有。舊版的推測規則只涵蓋 ADVENTURES，
        // 漏掉 SCENARIOS 的 20 個項目，還原後的 local.pak 比原版多 10,799 位元組。
        if (manifest is { AddedEntries.Count: > 0 })
        {
            foreach (string name in manifest.AddedEntries)
            {
                if (localPak.Contains(name))
                {
                    localPak.Remove(name);
                }
            }
        }
        else
        {
            // 後備路徑：清冊沒有項目清單（早期版本寫的 marker）。
            // 任何一段路徑是「非原廠語系目錄」就視為我們加的。
            var customEntries = new List<string>();
            foreach (string name in localPak.Names())
            {
                string[] parts = name.Split('\\');
                bool isCustomLang =
                    (!StockLanguages.Contains(parts[0]) && !NonLanguageRoots.Contains(parts[0])) ||
                    (parts.Length > 2 && NonLanguageRoots.Contains(parts[0]) &&
                     !StockLanguages.Contains(parts[2]) && !parts[2].Contains('.'));

                if (isCustomLang)
                {
                    customEntries.Add(name);
                }
            }

            foreach (string name in customEntries)
            {
                localPak.Remove(name);
            }
        }

        // 3. 依據清冊記錄精確還原所有 APF 字型
        foreach (var name in localPak.Names().ToList())
        {
            if (name.StartsWith("FONTS\\", StringComparison.OrdinalIgnoreCase) &&
                name.EndsWith(".APF", StringComparison.OrdinalIgnoreCase))
            {
                byte[] fontData = localPak.Read(name);
                var font = ApfFont.Load(fontData);

                var fontRecord = manifest?.GetFontRecord(name);
                if (fontRecord != null)
                {
                    font.StripAddedRanges(fontRecord);
                    localPak.Write(name, font.Dump());
                }

                // 沒有清冊記錄就不能碰這個字型。APF 位元組裡沒有任何欄位能區分
                // 我們加的字形與遊戲原廠字形，所以「猜著剝離」只會把原廠字形也刪掉
                // （這正是先前用碼位門檻判斷時吃掉兩個原版範圍的那個 bug）。
                // 清冊遺失時 PatchState.InspectLocalPak 會回報 Unrecognised 讓上層拒絕操作，
                // 這裡什麼都不做是刻意的。
            }
        }

        // 3. 刪除 marker 清冊
        if (localPak.Contains(MarkerPath))
        {
            localPak.Remove(MarkerPath);
        }
    }

    /// <summary>
    /// 偵測 local.pak 中實際存在之官方語言清單。
    /// 依據 pak 內部真實存在的路徑列舉，預設以 ENGLISH 優先，其餘依字母排序。
    /// </summary>
    public static List<string> DetectStockLanguages(HmmPak localPak)
    {
        var detected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string name in localPak.Names())
        {
            string[] parts = name.Split('\\');
            if (parts.Length == 0) continue;

            if (StockLanguages.Contains(parts[0]))
            {
                detected.Add(parts[0].ToUpperInvariant());
            }
            else if (parts.Length > 2 && NonLanguageRoots.Contains(parts[0]) && StockLanguages.Contains(parts[2]))
            {
                detected.Add(parts[2].ToUpperInvariant());
            }
        }

        return detected
            .OrderBy(lang => lang.Equals("ENGLISH", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(lang => lang, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// GUI 匯出清單：非英文語系本身必須有翻譯表；ENGLISH 可借用任一官方翻譯表的結構。
    /// </summary>
    public static List<string> DetectExportableStockLanguages(HmmPak localPak)
    {
        List<string> detected = DetectStockLanguages(localPak);
        bool hasAnyTables = detected.Any(language => HasTranslationTables(localPak, language));
        return detected.Where(language =>
                language.Equals("ENGLISH", StringComparison.OrdinalIgnoreCase)
                    ? hasAnyTables
                    : HasTranslationTables(localPak, language))
            .ToList();
    }

    private static string? FindStructureTemplateLanguage(HmmPak localPak, IReadOnlyCollection<string> detected)
    {
        // GERMAN 是本工具歷來使用且已完整驗證的結構底本；只有它不存在時才依序回退。
        string[] preferred = ["GERMAN", "FRENCH", "BULGARIAN", "SPANISH", "ITALIAN", "RUSSIAN"];
        return preferred.FirstOrDefault(language =>
                   detected.Contains(language, StringComparer.OrdinalIgnoreCase) &&
                   HasTranslationTables(localPak, language))
               ?? detected.FirstOrDefault(language =>
                   !language.Equals("ENGLISH", StringComparison.OrdinalIgnoreCase) &&
                   HasTranslationTables(localPak, language));
    }

    private static bool HasTranslationTables(HmmPak localPak, string language)
    {
        foreach (string name in localPak.Names())
        {
            string[] parts = name.Split('\\');
            bool belongsToLanguage =
                (parts.Length > 0 && parts[0].Equals(language, StringComparison.OrdinalIgnoreCase)) ||
                (parts.Length > 2 && parts[2].Equals(language, StringComparison.OrdinalIgnoreCase));
            if (!belongsToLanguage || !name.EndsWith(".XML", StringComparison.OrdinalIgnoreCase)) continue;
            if (LocXml.IsTranslationTable(localPak.Read(name))) return true;
        }
        return false;
    }

    /// <summary>
    /// 從遊戲 local.pak 既有語系匯出未翻譯之語言包骨架範本 (SPEC.md §6.2)。
    /// 翻譯資料模型以英文原文為穩定 key/source；預設從 ENGLISH 匯出（value 亦預填英文）。
    /// 若選取其他官方語言，key 仍是英文原文，value 預填該官方語言之 result（缺少時回退英文）。
    /// </summary>
    public static void ExportTemplate(
        HmmPak localPak,
        string templateLang,
        string outDir,
        Action<string>? log = null)
    {
        string tLang = string.IsNullOrWhiteSpace(templateLang) ? "ENGLISH" : templateLang.Trim().ToUpperInvariant();
        var detectedLanguages = DetectStockLanguages(localPak);

        if (!detectedLanguages.Contains(tLang, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(Strings.Get("Error_LangExportLangNotFound", tLang));
        }

        Directory.CreateDirectory(outDir);

        bool isEnglishSelected = tLang.Equals("ENGLISH", StringComparison.OrdinalIgnoreCase);

        // 決定要掃描哪個語系的 XML 來萃取翻譯條目
        string tableScanLang = tLang;
        if (isEnglishSelected)
        {
            if (!HasTranslationTables(localPak, "ENGLISH"))
            {
                // Steam 版 local.pak 的 XML 結構位於 GERMAN / FRENCH / BULGARIAN
                string? fallback = FindStructureTemplateLanguage(localPak, detectedLanguages);
                if (string.IsNullOrEmpty(fallback))
                    throw new InvalidOperationException(Strings.Get("Error_LangExportNoTranslationTables"));
                tableScanLang = fallback;
            }
        }
        else if (!HasTranslationTables(localPak, tLang))
        {
            throw new InvalidOperationException(Strings.Get("Error_LangExportSourceHasNoTables", tLang));
        }

        var buckets = new SortedDictionary<string, SortedDictionary<string, string>>(StringComparer.Ordinal);

        foreach (var entry in localPak.Entries)
        {
            string[] parts = entry.Name.Split('\\');
            string bucket;
            if (parts[0].Equals(tableScanLang, StringComparison.OrdinalIgnoreCase))
            {
                bucket = "ui";
            }
            else if (parts.Length > 2 && parts[2].Equals(tableScanLang, StringComparison.OrdinalIgnoreCase))
            {
                bucket = "campaign-" + parts[1].ToLowerInvariant().Replace(' ', '-');
            }
            else
            {
                continue;
            }

            byte[] data = localPak.Read(entry.Name);
            if (!LocXml.IsTranslationTable(data)) continue;

            foreach (var attrs in LocXml.Entries(data))
            {
                string src = LocXml.SourceText(attrs);
                if (src.Trim().Length == 0) continue;

                if (!buckets.TryGetValue(bucket, out var map))
                {
                    map = new SortedDictionary<string, string>(StringComparer.Ordinal);
                    buckets[bucket] = map;
                }

                // 英文模式：key 與 value 均為英文原文
                // 其他官方語言模式：key 為英文原文，value 預填該官方語言之 result (缺少時回退英文)
                if (isEnglishSelected)
                {
                    map[src] = src;
                }
                else
                {
                    string val = attrs.TryGetValue("result", out string? r) && !string.IsNullOrWhiteSpace(r)
                        ? r
                        : src;
                    map[src] = val;
                }
            }
        }

        var jsonOpts = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        var campaignsList = new List<string>();

        foreach (var (key, dict) in buckets)
        {
            string fileName = key + ".json";
            string filePath = Path.Combine(outDir, fileName);
            string json = JsonSerializer.Serialize(dict, jsonOpts);
            File.WriteAllText(filePath, json, Utf8NoBom);

            if (key.StartsWith("campaign-"))
            {
                campaignsList.Add(fileName);
            }

            log?.Invoke($"  {fileName,-35} {dict.Count,5} 條詞彙");
        }

        // 匯出 help.json
        if (localPak.Contains(@"ENGLISH\HELP.XML"))
        {
            var helpSegs = new SortedDictionary<string, string>(StringComparer.Ordinal);
            byte[] helpXml = localPak.Read(@"ENGLISH\HELP.XML");
            foreach (string seg in LocXml.HelpSegments(helpXml))
            {
                helpSegs[seg] = seg;
            }

            string helpPath = Path.Combine(outDir, "help.json");
            File.WriteAllText(helpPath, JsonSerializer.Serialize(helpSegs, jsonOpts), Utf8NoBom);
            log?.Invoke($"  {"help.json",-35} {helpSegs.Count,5} 段說明文字");
        }

        // 產生 pack.json 骨架
        string packTemplateLang = isEnglishSelected ? tableScanLang : tLang;
        var stubMeta = new LanguagePackMeta
        {
            Id = "new-lang",
            Name = "New Language",
            NativeName = "Language Name",
            Version = "1.0.0",
            Authors = ["Translator Name"],
            GameLangFolder = "CUSTOM",
            GameLangKey = "custom",
            TemplateLang = packTemplateLang,
            Font = new FontMeta
            {
                Face = "Arial",
                FallbackFaces = ["Tahoma", "Segoe UI"],
                Ranges = ["0020-007F", "00A0-00FF"],
                SizeAdjust = 0
            },
            Files = new FilesMeta
            {
                Ui = "ui.json",
                Help = "help.json",
                Campaigns = campaignsList
            }
        };

        string stubJson = JsonSerializer.Serialize(stubMeta, jsonOpts);
        File.WriteAllText(Path.Combine(outDir, "pack.json"), stubJson, Utf8NoBom);
        log?.Invoke($"  {"pack.json",-35} 語言包設定檔骨架 (模板: {packTemplateLang})");
        log?.Invoke($"語言包範本已成功匯出至：{outDir}");
    }

    /// <summary>
    /// 取得目前 local.pak 中安裝之自訂語言包 ID 清單。
    /// </summary>
    public static List<string> GetInstalledLanguages(HmmPak localPak)
    {
        var langs = new List<string>();
        foreach (string name in localPak.Names())
        {
            string[] parts = name.Split('\\');
            if (parts.Length > 0 && !StockLanguages.Contains(parts[0]) && !NonLanguageRoots.Contains(parts[0]))
            {
                if (!langs.Contains(parts[0], StringComparer.OrdinalIgnoreCase))
                {
                    langs.Add(parts[0]);
                }
            }
        }
        return langs;
    }
}
