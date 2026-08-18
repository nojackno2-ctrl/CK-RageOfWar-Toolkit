using System.Text;
using System.Text.RegularExpressions;

namespace CKToolkit.Core.Lang;

/// <summary>
/// 處理 *.LOC.XML / *.CONV.XML 這類 &lt;translationtable&gt; 翻譯表與 HELP.XML。
///
/// 關鍵紀律 (SPEC.md §6.3, PHASE3.md)：
///   1. 拿原有模板語系（如德文）的檔案當結構底本，只換掉 result="..." 的內容，
///      其他屬性與排版一個位元組都不動，確保結構與引擎預期完全一致。
///   2. HELP.XML 必須採用嚴格的非自閉合 entry 正規表達式：
///      (&lt;entry\b(?![^&gt;]*?/&gt;)[^&gt;]*&gt;)(.*?)(&lt;/entry&gt;)
///      舊式寫法會將 &lt;entry/&gt; 誤判為開頭標籤造成屬性溢出污染鍵值。
/// </summary>
public static partial class LocXml
{
    [GeneratedRegex(@"<translationtableentry\b[^>]*?/>", RegexOptions.Singleline | RegexOptions.Compiled)]
    private static partial Regex EntryRegex();

    [GeneratedRegex(@"([\w:]+)=""(.*?)""", RegexOptions.Singleline | RegexOptions.Compiled)]
    private static partial Regex AttrRegex();

    [GeneratedRegex(@"(\bresult="")(.*?)("")", RegexOptions.Singleline | RegexOptions.Compiled)]
    private static partial Regex ResultRegex();

    [GeneratedRegex(@"(<entry\b(?![^>]*?/>)[^>]*>)(.*?)(</entry>)", RegexOptions.Singleline | RegexOptions.Compiled)]
    private static partial Regex HelpEntryRegex();

    private static readonly UTF8Encoding Utf8 = new(false, true);

    public static string Unescape(string s)
    {
        return s.Replace("&lt;", "<")
                .Replace("&gt;", ">")
                .Replace("&quot;", "\"")
                .Replace("&apos;", "'")
                .Replace("&amp;", "&");
    }

    public static string Escape(string s)
    {
        return s.Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;");
    }

    public static Dictionary<string, string> ParseAttributes(string tag)
    {
        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match m in AttrRegex().Matches(tag))
        {
            dict[m.Groups[1].Value] = Unescape(m.Groups[2].Value);
        }
        return dict;
    }

    /// <summary>取出這筆的英文原文。</summary>
    public static string SourceText(Dictionary<string, string> attrs)
    {
        if (attrs.TryGetValue("justtext", out string? justText)) return justText;
        return attrs.TryGetValue("text", out string? text) ? text : string.Empty;
    }

    public static bool IsTranslationTable(byte[] data)
    {
        int n = Math.Min(data.Length, 64);
        string head = Encoding.ASCII.GetString(data, 0, n);
        return head.IndexOf("<translationtable", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    public static IEnumerable<Dictionary<string, string>> Entries(byte[] data)
    {
        string text;
        try { text = Utf8.GetString(data); }
        catch (DecoderFallbackException) { yield break; }

        foreach (Match m in EntryRegex().Matches(text))
        {
            yield return ParseAttributes(m.Value);
        }
    }

    /// <summary>用 translate(attrs) 重寫每筆的 result；回傳 null 時退回英文原文。</summary>
    public static byte[] Rebuild(
        byte[] data,
        Func<Dictionary<string, string>, string?> translate,
        out int done,
        out int total)
    {
        done = 0;
        total = 0;
        string text;
        try { text = Utf8.GetString(data); }
        catch (DecoderFallbackException) { return data; }

        int localDone = 0, localTotal = 0;
        string result = EntryRegex().Replace(text, m =>
        {
            localTotal++;
            var attrs = ParseAttributes(m.Value);
            string? translated = translate(attrs);
            if (!string.IsNullOrEmpty(translated))
            {
                localDone++;
            }
            else
            {
                translated = SourceText(attrs);
            }
            string escaped = Escape(translated);
            return ResultRegex().Replace(m.Value,
                r => r.Groups[1].Value + escaped + r.Groups[3].Value, 1);
        });

        done = localDone;
        total = localTotal;
        return Utf8.GetBytes(result);
    }

    /// <summary>翻譯 help.xml 裡 &lt;entry ...&gt;文字&lt;/entry&gt; 的內容。</summary>
    public static byte[] RebuildHelp(
        byte[] english,
        IDictionary<string, string> table,
        out int done,
        out int total)
    {
        done = 0;
        total = 0;
        string text;
        try { text = Utf8.GetString(english); }
        catch (DecoderFallbackException) { return english; }

        int localDone = 0, localTotal = 0;
        string result = HelpEntryRegex().Replace(text, m =>
        {
            string inner = m.Groups[2].Value;
            string stripped = inner.Trim();
            if (stripped.Length == 0) return m.Value;
            localTotal++;

            if (!table.TryGetValue(Unescape(stripped), out string? zh) || string.IsNullOrEmpty(zh))
            {
                return m.Value;
            }
            localDone++;

            string lead = inner[..(inner.Length - inner.TrimStart().Length)];
            string tail = inner[inner.TrimEnd().Length..];
            return m.Groups[1].Value + lead + Escape(zh) + tail + m.Groups[3].Value;
        });

        done = localDone;
        total = localTotal;
        return Utf8.GetBytes(result);
    }

    public static IEnumerable<string> HelpSegments(byte[] english)
    {
        string text;
        try { text = Utf8.GetString(english); }
        catch (DecoderFallbackException) { yield break; }

        foreach (Match m in HelpEntryRegex().Matches(text))
        {
            string s = Unescape(m.Groups[2].Value.Trim());
            if (s.Length > 0) yield return s;
        }
    }
}
