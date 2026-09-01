using CKToolkit.I18n;
namespace CKToolkit.Core.Lang;

/// <summary>
/// 將目標字元集字形光柵化並追加進原版 APF 字型。
///
/// 關鍵紀律 (SPEC.md §6, PHASE3.md)：
///   1. 原有的拉丁／斯拉夫字形一個位元組都不動，只在範圍表尾端接上新的字元範圍。
///   2. 字元範圍由語言包 (pack.json) 宣告驅動，絕不硬編 CJK 字元範圍常數。
///   3. 支援任意新語言包之動態字元集擴充。
/// </summary>
public static class FontBuilder
{
    /// <summary>
    /// 範圍之間允許的空隙。引擎查字形是線性掃描範圍表（提早在 first &gt; ch 時中止），
    /// 4 是檔案大小與查表成本的折衷。
    /// </summary>
    public const int RangeGap = 4;

    /// <summary>
    /// 把散落的碼位合併成 (first, count) 範圍清單。
    /// </summary>
    public static List<int[]> MakeRanges(IEnumerable<int> codepoints, int gap = RangeGap)
    {
        var sorted = new List<int>(new HashSet<int>(codepoints));
        sorted.Sort();
        var result = new List<int[]>();
        if (sorted.Count == 0) return result;

        int start = sorted[0];
        int prev = sorted[0];
        for (int i = 1; i < sorted.Count; i++)
        {
            int cp = sorted[i];
            if (cp - prev > gap)
            {
                result.Add([start, prev - start + 1]);
                start = cp;
            }
            prev = cp;
        }
        result.Add([start, prev - start + 1]);
        return result;
    }

    public sealed class FontBuildResult
    {
        public int Added { get; set; }
        public List<int> Missing { get; } = [];
        public List<int> AddedRangeFirsts { get; set; } = [];
        public FontPatchRecord PatchRecord { get; set; } = new();
    }

    /// <summary>
    /// 就地把 codepoints 加進 font。
    /// </summary>
    public static FontBuildResult AddGlyphs(
        ApfFont font,
        IEnumerable<int> codepoints,
        string face,
        int sizeDelta = 0,
        bool? bold = null,
        Action<int>? onProgress = null)
    {
        int size = font.PixelSize + sizeDelta;
        bool isBold = bold ?? font.Bold;
        int baseline = font.Ascent; // 沿用原字型的 ascent 當基線

        var already = font.Covered();
        var wanted = new HashSet<int>();
        foreach (int cp in codepoints)
        {
            if (!already.Contains(cp))
            {
                wanted.Add(cp);
            }
        }

        var result = new FontBuildResult();
        if (wanted.Count == 0)
        {
            result.PatchRecord = font.CreatePatchRecord();
            return result;
        }

        int maxWidth = font.Metrics[6];

        using (var gdi = new GdiFont(face, size, isBold, baseline))
        {
            if (!string.Equals(gdi.ActualFace, face, StringComparison.OrdinalIgnoreCase))
            {
                bool isCompatible =
                    (face.Contains("正黑體") && gdi.ActualFace.Contains("JhengHei")) ||
                    (face.Contains("JhengHei") && gdi.ActualFace.Contains("正黑體")) ||
                    (face.Contains("雅黑") && gdi.ActualFace.Contains("YaHei")) ||
                    (face.Contains("YaHei") && gdi.ActualFace.Contains("雅黑"));

                if (!isCompatible)
                {
                    throw new InvalidOperationException(Strings.Get("Error_FontFaceUnavailable", face, gdi.ActualFace));
                }
            }

            int done = 0;
            foreach (var span in MakeRanges(wanted))
            {
                int first = span[0], count = span[1];
                var range = new GlyphRange(first)
                {
                    IsOriginal = false
                };

                for (int cp = first; cp < first + count; cp++)
                {
                    if (!gdi.TryGetGlyph(cp, out var glyph) || glyph is null)
                    {
                        if (wanted.Contains(cp)) result.Missing.Add(cp);
                        range.Glyphs.Add(new Glyph { Top = baseline });
                        continue;
                    }

                    range.Glyphs.Add(glyph);
                    if (glyph.Width > maxWidth) maxWidth = glyph.Width;
                    result.Added++;
                    done++;

                    if (done % 500 == 0)
                    {
                        onProgress?.Invoke(done);
                    }
                }
                font.Ranges.Add(range);
                result.AddedRangeFirsts.Add(first);
            }
        }

        font.Metrics[6] = maxWidth;
        font.SortRanges();
        result.PatchRecord = font.CreatePatchRecord();
        return result;
    }

    /// <summary>
    /// 對字型中既有之範圍追加字形（重疊擴充情況）。
    /// 保留原始 RawBlock 與原始字形數，供後續精確反轉。
    /// </summary>
    public static void ExtendRangeWithGlyphs(
        ApfFont font,
        int rangeFirst,
        IEnumerable<Glyph> additionalGlyphs)
    {
        var targetRange = font.Ranges.FirstOrDefault(r => r.First == rangeFirst)
            ?? throw new ArgumentException(Strings.Get("Error_FontRangeNotFound", $"0x{rangeFirst:X}"), nameof(rangeFirst));

        if (!targetRange.OriginalCount.HasValue)
        {
            targetRange.OriginalCount = targetRange.Glyphs.Count;
        }
        if (targetRange.OriginalRawBlock is null && targetRange.RawBlock is not null)
        {
            targetRange.OriginalRawBlock = (byte[])targetRange.RawBlock.Clone();
        }

        targetRange.RawBlock = null; // 重疊擴充後必須重新 Dump
        int maxWidth = font.Metrics[6];

        foreach (var g in additionalGlyphs)
        {
            targetRange.Glyphs.Add(g);
            if (g.Width > maxWidth) maxWidth = g.Width;
        }

        font.Metrics[6] = maxWidth;
    }
}
