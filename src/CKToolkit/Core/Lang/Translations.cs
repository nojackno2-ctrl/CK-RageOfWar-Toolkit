using System.Text.Json;

namespace CKToolkit.Core.Lang;

/// <summary>
/// 翻譯資料庫與查詢管理。
///
/// 支援三種 JSON 字典：
///   1. 檔名為 help.json：線上說明段落，存入 Help
///   2. 檔名以 -context 結尾（如 ui-context.json）：完整索引鍵（含 @情境），存入 ByKey
///   3. 其他（如 ui.json, campaign-*.json）：英文原文為鍵，存入 ByText
/// </summary>
public sealed class Translations
{
    public Dictionary<string, string> ByText { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> ByKey { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> Help { get; } = new(StringComparer.Ordinal);
    public string? Credits { get; set; }
    public List<string> Sources { get; } = [];

    public int PhraseCount => ByText.Count + ByKey.Count;

    public void Merge(string stem, string json, string label)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Object) return;

        Dictionary<string, string> target;
        if (string.Equals(stem, "help", StringComparison.OrdinalIgnoreCase))
            target = Help;
        else if (stem.EndsWith("-context", StringComparison.OrdinalIgnoreCase))
            target = ByKey;
        else
            target = ByText;

        int added = 0;
        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            if (prop.Name.StartsWith('_')) continue; // 註解或中繼資料
            if (prop.Value.ValueKind != JsonValueKind.String) continue;

            string val = prop.Value.GetString() ?? string.Empty;
            if (string.IsNullOrEmpty(val)) continue;

            target[prop.Name] = val;
            added++;
        }

        if (added > 0)
        {
            Sources.Add($"{label}({added})");
        }
    }

    public string? Lookup(Dictionary<string, string> attrs)
    {
        if (attrs.TryGetValue("text", out string? key))
        {
            if (ByKey.TryGetValue(key, out string? byKey) && !string.IsNullOrEmpty(byKey))
                return byKey;
        }

        string srcText = LocXml.SourceText(attrs);
        return ByText.TryGetValue(srcText, out string? zh) ? zh : null;
    }

    public IEnumerable<string> AllText()
    {
        foreach (var v in ByText.Values) yield return v;
        foreach (var v in ByKey.Values) yield return v;
        foreach (var v in Help.Values) yield return v;
        if (!string.IsNullOrEmpty(Credits)) yield return Credits;
    }
}
