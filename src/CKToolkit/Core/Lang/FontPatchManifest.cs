using System.Text.Json.Serialization;

namespace CKToolkit.Core.Lang;

/// <summary>
/// 記錄單一 APF 範圍的起始碼位與字元數。
/// </summary>
public sealed class RangeSpanRecord
{
    [JsonPropertyName("first")]
    public int First { get; set; }

    [JsonPropertyName("count")]
    public int Count { get; set; }
}

/// <summary>
/// 記錄現有範圍被追加字形（重疊情況）時的原始字形數與原始 RawBlock。
/// </summary>
public sealed class ModifiedRangeRecord
{
    [JsonPropertyName("first")]
    public int First { get; set; }

    [JsonPropertyName("originalCount")]
    public int OriginalCount { get; set; }

    [JsonPropertyName("originalRawBlock")]
    public string? OriginalRawBlockBase64 { get; set; }
}

/// <summary>
/// 記錄單一 APF 字型被語言包安裝所追加之範圍與修改資訊 (SPEC.md §6)。
/// 供 Uninstall / Normalise 依據事實精確反轉，絕不依賴字元碼門檻。
/// </summary>
public sealed class FontPatchRecord
{
    /// <summary>
    /// 原版字型之 Metrics[6] (MaxWidth)。
    /// </summary>
    [JsonPropertyName("originalMaxWidth")]
    public int? OriginalMaxWidth { get; set; }

    /// <summary>
    /// 完全由修補所新增之範圍起始碼 (First)。
    /// </summary>
    [JsonPropertyName("addedRangeFirsts")]
    public List<int> AddedRangeFirsts { get; set; } = [];

    /// <summary>
    /// 若現有範圍被追加字形（重疊情況），記錄其 First、原版字形數與原版 RawBlock。
    /// </summary>
    [JsonPropertyName("modifiedRanges")]
    public List<ModifiedRangeRecord> ModifiedRanges { get; set; } = [];

    /// <summary>
    /// 原版範圍清單快照 (First, Count)，用於防禦性校驗與一致性比對。
    /// </summary>
    [JsonPropertyName("originalRanges")]
    public List<RangeSpanRecord> OriginalRanges { get; set; } = [];
}

/// <summary>
/// 記錄於 local.pak 內 FONTS\.patch_marker.json 之修補清冊。
/// 自我描述 (self-describing)：修補後的封裝檔自帶還原所需之精確資訊。
/// </summary>
public sealed class FontPatchManifest
{
    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    [JsonPropertyName("packId")]
    public string? PackId { get; set; }

    [JsonPropertyName("fonts")]
    public Dictionary<string, FontPatchRecord> Fonts { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 安裝時新增到 local.pak 的所有項目名稱，解除安裝時據此精確移除。
    ///
    /// ⚠ 語言檔案不是只放在頂層的 <c>CHINESE\</c>：模板語系在 <c>SCENARIOS\&lt;地圖&gt;\GERMAN\</c>
    /// 這種巢狀路徑下也有內容，安裝時會一併複製成 <c>SCENARIOS\&lt;地圖&gt;\CHINESE\</c>。
    /// 早期版本的解除安裝只刪頂層語系目錄，於是留下 20 個巢狀項目，
    /// 還原後的 local.pak 比原版多了 10,799 位元組。用記錄取代推測就不會漏。
    /// </summary>
    [JsonPropertyName("addedEntries")]
    public List<string> AddedEntries { get; set; } = [];

    public FontPatchRecord? GetFontRecord(string fontPath)
    {
        string norm = fontPath.Replace('/', '\\');
        if (Fonts.TryGetValue(norm, out var rec)) return rec;

        string fileName = Path.GetFileName(norm);
        foreach (var (k, v) in Fonts)
        {
            if (Path.GetFileName(k).Equals(fileName, StringComparison.OrdinalIgnoreCase))
                return v;
        }
        return null;
    }
}
