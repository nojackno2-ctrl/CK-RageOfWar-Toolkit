using System.Text;

namespace CKToolkit.Core.Lang;

/// <summary>
/// local/fonts/*.apf 的 ABCF 點陣字型讀寫器與資料結構。
///
/// 檔頭格式 (SPEC.md §6, 逆向工程分析)：
///   0x00 char[4] "ABCF"
///   0x04 int     字型資料區位移（與 Metrics[3] 相同）
///   0x08 int     產生器的最大字元格寬（原版都是 32）
///   0x0C int     產生器的最大字元格高（39 或 44）
///   0x10 int     找不到字形時替代用的字元碼（31）
///   0x14 int     -1
///   0x18 int     formatting 筆數（都是 1）
///   0x1C uint    formatting 陣列位移（= 0x20 + 兩個字串長度）
///   0x20 cstr    face name、cstr family name
/// Metrics（20 個 int，其實是兩段）：
///   [0..4]  20 bytes 的 formatting 紀錄
///           [0] 字級 [1] italic [2] bold
///           [3] 字型資料區的檔案位移（= formatting 位移 + 20）
///           [4] 字型資料區長度；引擎照這個長度一次讀進記憶體，範圍表也在裡面，
///               所以加了範圍一定要更新成 60 + 16 * 範圍數，否則新範圍不會生效
///   [5..19] 字型資料區前 60 bytes：[5] 行高 [6] 最大字形寬 [10] ascent [11] descent
///           [19] 字元範圍數
/// 範圍表 n × 16 bytes：uint 位移、uint 長度、uint 起始字元、uint 字元數
/// 每個範圍區塊：
///   uint kernOffset = 16 + 32 * 字元數
///   uint kernCount
///   uint bitmapOffset = kernOffset + 12 * kernCount
///   uint bitmapSize
///   字形描述 × 字元數（各 32 bytes，8 個 int）
///     [0] A 間距 [1] B 墨寬 [2] C 間距 [3] 0
///     [4] top    [5] 寬度-1 [6] bottom [7] 點陣資料位移
///   字距對 × kernCount（各 12 bytes）
///   點陣 RLE 資料
/// </summary>
public sealed class ApfFont
{
    public const int MaxLevel = 14;

    private static readonly Encoding NameEncoding;

    static ApfFont()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        NameEncoding = Encoding.GetEncoding(1252);
    }

    public int[] Unk { get; } = new int[6];
    public string Face { get; set; } = string.Empty;
    public string Family { get; set; } = string.Empty;
    public int[] Metrics { get; } = new int[20];
    public int OrigMaxWidth { get; set; }
    public List<GlyphRange> Ranges { get; } = [];

    public int PixelSize => Metrics[0];
    public bool Bold => Metrics[2] != 0;
    public int Ascent => Metrics[10];
    public int Descent => Metrics[11];
    public int LineHeight => Metrics[5];

    /// <summary>
    /// 這個記憶體物件上是否有「本次工作階段」加進去、尚未剝離的字形。
    ///
    /// ⚠ 這是純記憶體狀態，**不會**寫進字型位元組，因此 Dump 之後再 Load 一律回傳 false。
    /// 絕對不要拿它來判斷「磁碟上這個字型是否被修補過」——APF 格式本身沒有任何欄位
    /// 能區分我們加的字形與遊戲原廠字形，那個問題只能靠 local.pak 內的清冊
    /// (<see cref="LangInstaller.MarkerPath"/>) 回答。誤用會讓已修補的字型被判成原版，
    /// 進而在既有字形上再疊一層，檔案從此偏移且無法還原。
    /// </summary>
    public bool HasInMemoryAdditions =>
        Ranges.Any(r => !r.IsOriginal || (r.OriginalCount.HasValue && r.Glyphs.Count > r.OriginalCount.Value));

    // ------------------------------------------------------------ 讀取

    public static ApfFont Load(byte[] data)
    {
        if (data.Length < 0x24 || data[0] != (byte)'A' || data[1] != (byte)'B'
            || data[2] != (byte)'C' || data[3] != (byte)'F')
        {
            throw new InvalidDataException("不是 APF 字型：Magic 不符");
        }

        var font = new ApfFont();
        for (int i = 0; i < 6; i++)
        {
            font.Unk[i] = BitConverter.ToInt32(data, 4 + 4 * i);
        }
        int metricsOffset = (int)BitConverter.ToUInt32(data, 0x1C);

        int p = 0x20;
        font.Face = ReadCString(data, ref p);
        font.Family = ReadCString(data, ref p);
        if (p != metricsOffset)
        {
            throw new InvalidDataException("metrics 位移不符");
        }

        for (int i = 0; i < 20; i++)
        {
            font.Metrics[i] = BitConverter.ToInt32(data, metricsOffset + 4 * i);
        }

        font.OrigMaxWidth = font.Metrics[6];

        int rangeTable = metricsOffset + 80;
        int rangeCount = font.Metrics[19];
        for (int i = 0; i < rangeCount; i++)
        {
            int off = (int)BitConverter.ToUInt32(data, rangeTable + 16 * i);
            int size = (int)BitConverter.ToUInt32(data, rangeTable + 16 * i + 4);
            int first = (int)BitConverter.ToUInt32(data, rangeTable + 16 * i + 8);
            int count = (int)BitConverter.ToUInt32(data, rangeTable + 16 * i + 12);
            font.Ranges.Add(LoadRange(data, off, size, first, count));
        }

        return font;
    }

    private static GlyphRange LoadRange(byte[] data, int off, int size, int first, int count)
    {
        int kernOffset = (int)BitConverter.ToUInt32(data, off);
        int kernCount = (int)BitConverter.ToUInt32(data, off + 4);
        int bitmapOffset = (int)BitConverter.ToUInt32(data, off + 8);
        int bitmapSize = (int)BitConverter.ToUInt32(data, off + 12);

        var raw = new int[count][];
        for (int i = 0; i < count; i++)
        {
            var g = new int[8];
            for (int j = 0; j < 8; j++)
            {
                g[j] = BitConverter.ToInt32(data, off + 16 + 32 * i + 4 * j);
            }
            raw[i] = g;
        }

        var range = new GlyphRange(first);
        for (int i = 0; i < count; i++)
        {
            int[] g = raw[i];
            int start = g[7];
            int end = i + 1 < count ? raw[i + 1][7] : bitmapSize;
            int w = g[5] + 1;
            int h = g[6] - g[4] + 1;
            if (w <= 0 || h <= 0) { w = 0; h = 0; }

            var glyph = new Glyph
            {
                A = g[0],
                B = g[1],
                C = g[2],
                Top = g[4],
                Width = w,
                Height = h
            };
            if (w * h > 0 && end > start && off + bitmapOffset + end <= data.Length)
            {
                var encoded = new byte[end - start];
                Buffer.BlockCopy(data, off + bitmapOffset + start, encoded, 0, encoded.Length);
                glyph.Pixels = RleDecode(encoded, w * h);
            }
            else
            {
                glyph.Pixels = [];
            }
            range.Glyphs.Add(glyph);
        }

        if (kernCount > 0 && off + kernOffset + 12 * kernCount <= data.Length)
        {
            range.Kerning = new byte[12 * kernCount];
            Buffer.BlockCopy(data, off + kernOffset, range.Kerning, 0, range.Kerning.Length);
            range.KernCount = kernCount;
        }

        // 保存原廠原始區塊位元組以達成 100% 逐位元組精確反轉
        if (off + size <= data.Length)
        {
            range.RawBlock = new byte[size];
            Buffer.BlockCopy(data, off, range.RawBlock, 0, size);
        }
        range.IsOriginal = true;
        range.OriginalCount = count;

        return range;
    }

    // ------------------------------------------------------------ 寫出

    public byte[] Dump()
    {
        byte[] faceBytes = NameEncoding.GetBytes(Face);
        byte[] familyBytes = NameEncoding.GetBytes(Family);
        int namesLen = faceBytes.Length + 1 + familyBytes.Length + 1;
        int metricsOffset = 0x20 + namesLen;

        var metrics = (int[])Metrics.Clone();
        metrics[19] = Ranges.Count;
        metrics[3] = metricsOffset + 20;
        metrics[4] = 60 + 16 * Ranges.Count;

        var unk = (int[])Unk.Clone();
        unk[0] = metricsOffset + 20;

        var blocks = new byte[Ranges.Count][];
        for (int i = 0; i < Ranges.Count; i++)
        {
            blocks[i] = Ranges[i].RawBlock ?? DumpRange(Ranges[i]);
        }

        int headLen = 0x20 + namesLen + 80;
        int tableLen = 16 * Ranges.Count;
        int totalBlocks = 0;
        foreach (var b in blocks) totalBlocks += b.Length;

        var outBuf = new byte[headLen + tableLen + totalBlocks];
        int p = 0;
        outBuf[p++] = (byte)'A'; outBuf[p++] = (byte)'B';
        outBuf[p++] = (byte)'C'; outBuf[p++] = (byte)'F';
        for (int i = 0; i < 6; i++) WriteI32(outBuf, ref p, unk[i]);
        WriteI32(outBuf, ref p, metricsOffset);
        Buffer.BlockCopy(faceBytes, 0, outBuf, p, faceBytes.Length);
        p += faceBytes.Length + 1;
        Buffer.BlockCopy(familyBytes, 0, outBuf, p, familyBytes.Length);
        p += familyBytes.Length + 1;
        for (int i = 0; i < 20; i++) WriteI32(outBuf, ref p, metrics[i]);

        int dataOffset = headLen + tableLen;
        for (int i = 0; i < Ranges.Count; i++)
        {
            WriteI32(outBuf, ref p, dataOffset);
            WriteI32(outBuf, ref p, blocks[i].Length);
            WriteI32(outBuf, ref p, Ranges[i].First);
            WriteI32(outBuf, ref p, Ranges[i].Count);
            dataOffset += blocks[i].Length;
        }

        foreach (var b in blocks)
        {
            Buffer.BlockCopy(b, 0, outBuf, p, b.Length);
            p += b.Length;
        }
        return outBuf;
    }

    public static byte[] DumpRange(GlyphRange range)
    {
        int n = range.Glyphs.Count;
        var bitmaps = new byte[n][];
        int bitmapTotal = 0;
        var table = new byte[32 * n];
        int tp = 0;

        for (int i = 0; i < n; i++)
        {
            Glyph g = range.Glyphs[i];
            byte[] enc = (g.Width > 0 && g.Height > 0) ? RleEncode(g.Pixels) : [];
            bitmaps[i] = enc;

            int top, bottom, widthMinusOne;
            if (g.Width > 0 && g.Height > 0)
            {
                top = g.Top;
                bottom = g.Top + g.Height - 1;
                widthMinusOne = g.Width - 1;
            }
            else
            {
                top = g.Top;
                bottom = g.Top - 1;
                widthMinusOne = -1;
            }

            WriteI32(table, ref tp, g.A);
            WriteI32(table, ref tp, g.B);
            WriteI32(table, ref tp, g.C);
            WriteI32(table, ref tp, 0);
            WriteI32(table, ref tp, top);
            WriteI32(table, ref tp, widthMinusOne);
            WriteI32(table, ref tp, bottom);
            WriteI32(table, ref tp, bitmapTotal);
            bitmapTotal += enc.Length;
        }

        int kernOffset = 16 + 32 * n;
        int bitmapOffset = kernOffset + range.Kerning.Length;
        var block = new byte[bitmapOffset + bitmapTotal];
        int p = 0;
        WriteI32(block, ref p, kernOffset);
        WriteI32(block, ref p, range.KernCount);
        WriteI32(block, ref p, bitmapOffset);
        WriteI32(block, ref p, bitmapTotal);
        Buffer.BlockCopy(table, 0, block, p, table.Length);
        p += table.Length;
        if (range.Kerning.Length > 0)
        {
            Buffer.BlockCopy(range.Kerning, 0, block, p, range.Kerning.Length);
            p += range.Kerning.Length;
        }
        foreach (var b in bitmaps)
        {
            Buffer.BlockCopy(b, 0, block, p, b.Length);
            p += b.Length;
        }
        return block;
    }

    // ------------------------------------------------------------ 反轉正規化

    /// <summary>
    /// 精確剝離所有附加之語言包字元範圍與重疊追加字形，並還原標頭與 Metrics。
    /// 若提供 FontPatchRecord，則依據記錄之 AddedRangeFirsts 與 ModifiedRanges 精確移除；
    /// 若未提供，則依據記憶體物件之 !IsOriginal 與 OriginalCount 進行剝離。
    /// 配合 RawBlock 保證 100% 逐位元組還原為原廠原版 APF 字型。
    /// </summary>
    public void StripAddedRanges(FontPatchRecord? record = null)
    {
        if (record != null)
        {
            var addedFirsts = new HashSet<int>(record.AddedRangeFirsts);
            Ranges.RemoveAll(r => addedFirsts.Contains(r.First));

            foreach (var mod in record.ModifiedRanges)
            {
                var target = Ranges.FirstOrDefault(r => r.First == mod.First);
                if (target != null)
                {
                    if (target.Glyphs.Count > mod.OriginalCount)
                    {
                        target.Glyphs.RemoveRange(mod.OriginalCount, target.Glyphs.Count - mod.OriginalCount);
                    }
                    if (!string.IsNullOrEmpty(mod.OriginalRawBlockBase64))
                    {
                        target.RawBlock = Convert.FromBase64String(mod.OriginalRawBlockBase64);
                    }
                }
            }
        }
        else
        {
            Ranges.RemoveAll(r => !r.IsOriginal);

            foreach (var r in Ranges)
            {
                if (r.OriginalCount.HasValue && r.Glyphs.Count > r.OriginalCount.Value)
                {
                    r.Glyphs.RemoveRange(r.OriginalCount.Value, r.Glyphs.Count - r.OriginalCount.Value);
                    if (r.OriginalRawBlock != null)
                    {
                        r.RawBlock = r.OriginalRawBlock;
                    }
                }
            }
        }

        Metrics[19] = Ranges.Count;
        Metrics[4] = 60 + 16 * Ranges.Count;

        int maxW = 0;
        foreach (var r in Ranges)
        {
            foreach (var g in r.Glyphs)
            {
                if (g.Width > maxW) maxW = g.Width;
            }
        }
        Metrics[6] = record?.OriginalMaxWidth ?? (OrigMaxWidth != 0 ? OrigMaxWidth : maxW);
        OrigMaxWidth = Metrics[6];
    }

    /// <summary>
    /// 依據當前記憶體字型物件之修改狀態，產生 FontPatchRecord 清冊。
    /// </summary>
    public FontPatchRecord CreatePatchRecord()
    {
        var rec = new FontPatchRecord
        {
            OriginalMaxWidth = OrigMaxWidth != 0 ? OrigMaxWidth : Metrics[6]
        };
        foreach (var r in Ranges)
        {
            if (!r.IsOriginal)
            {
                rec.AddedRangeFirsts.Add(r.First);
            }
            else if (r.OriginalCount.HasValue && r.Glyphs.Count > r.OriginalCount.Value)
            {
                rec.ModifiedRanges.Add(new ModifiedRangeRecord
                {
                    First = r.First,
                    OriginalCount = r.OriginalCount.Value,
                    OriginalRawBlockBase64 = r.OriginalRawBlock != null ? Convert.ToBase64String(r.OriginalRawBlock) : null
                });
                rec.OriginalRanges.Add(new RangeSpanRecord { First = r.First, Count = r.OriginalCount.Value });
            }
            else
            {
                rec.OriginalRanges.Add(new RangeSpanRecord { First = r.First, Count = r.OriginalCount ?? r.Count });
            }
        }
        return rec;
    }

    // ------------------------------------------------------------ RLE

    /// <summary>
    /// RLE 解碼：
    ///   b &lt; 0x20  → 灰階 0，長度 (b &amp; 0x1F) + 1（1..32）
    ///   b &gt;= 0xE0 → 灰階 14，長度 (b &amp; 0x1F) + 1（1..32）
    ///   其他        → 灰階 b &gt;&gt; 4（2..13），長度 (b &amp; 0x0F) + 1（1..16）
    /// </summary>
    public static byte[] RleDecode(byte[] data, int pixelCount)
    {
        var outBuf = new byte[pixelCount];
        var span = outBuf.AsSpan();
        int p = 0;
        foreach (byte b in data)
        {
            byte value;
            int count;
            if (b < 0x20) { value = 0; count = (b & 0x1F) + 1; }
            else if (b >= 0xE0) { value = MaxLevel; count = (b & 0x1F) + 1; }
            else { value = (byte)(b >> 4); count = (b & 0x0F) + 1; }

            int fill = Math.Min(count, pixelCount - p);
            if (fill > 0)
            {
                span.Slice(p, fill).Fill(value);
                p += fill;
            }
            if (p >= pixelCount) break;
        }
        return outBuf; // 原檔中極少數字形資料被截斷，其餘補 0
    }

    public static byte[] RleEncode(byte[] pixels)
    {
        if (pixels.Length == 0) return [];

        byte[] rented = System.Buffers.ArrayPool<byte>.Shared.Rent(pixels.Length + 16);
        try
        {
            int outPos = 0;
            int i = 0;
            while (i < pixels.Length)
            {
                byte v = pixels[i];
                int j = i + 1;
                while (j < pixels.Length && pixels[j] == v) j++;
                int count = j - i;

                int baseByte, cap;
                if (v == 0) { baseByte = 0x00; cap = 32; }
                else if (v >= MaxLevel) { baseByte = 0xE0; cap = 32; }
                else { baseByte = v << 4; cap = 16; }

                while (count > 0)
                {
                    int take = Math.Min(count, cap);
                    rented[outPos++] = (byte)(baseByte | (take - 1));
                    count -= take;
                }
                i = j;
            }

            var result = new byte[outPos];
            Buffer.BlockCopy(rented, 0, result, 0, outPos);
            return result;
        }
        finally
        {
            System.Buffers.ArrayPool<byte>.Shared.Return(rented);
        }
    }

    // ------------------------------------------------------------ 工具

    public HashSet<int> Covered()
    {
        var set = new HashSet<int>();
        foreach (var r in Ranges)
        {
            for (int cp = r.First; cp < r.First + r.Count; cp++)
                set.Add(cp);
        }
        return set;
    }

    public void SortRanges()
    {
        Ranges.Sort((a, b) => a.First.CompareTo(b.First));
    }

    public Glyph? FindGlyph(int codepoint)
    {
        foreach (var r in Ranges)
        {
            if (codepoint >= r.First && codepoint < r.First + r.Count)
                return r.Glyphs[codepoint - r.First];
        }
        return null;
    }

    /// <summary>
    /// 逐欄位深層比對兩個 ApfFont 記憶體模型（Face, Family, Unk, Metrics, Ranges, Kerning, Glyphs, Pixels, RawBlock）。
    /// </summary>
    public bool ModelEquals(ApfFont other, out string diffReason)
    {
        if (Face != other.Face)
        {
            diffReason = $"Face differs: '{Face}' != '{other.Face}'";
            return false;
        }
        if (Family != other.Family)
        {
            diffReason = $"Family differs: '{Family}' != '{other.Family}'";
            return false;
        }
        for (int i = 0; i < 6; i++)
        {
            if (Unk[i] != other.Unk[i])
            {
                diffReason = $"Unk[{i}] differs: {Unk[i]} != {other.Unk[i]}";
                return false;
            }
        }
        for (int i = 0; i < 20; i++)
        {
            if (Metrics[i] != other.Metrics[i])
            {
                diffReason = $"Metrics[{i}] differs: {Metrics[i]} != {other.Metrics[i]}";
                return false;
            }
        }
        if (Ranges.Count != other.Ranges.Count)
        {
            diffReason = $"Ranges.Count differs: {Ranges.Count} != {other.Ranges.Count}";
            return false;
        }
        for (int i = 0; i < Ranges.Count; i++)
        {
            var r1 = Ranges[i];
            var r2 = other.Ranges[i];
            if (r1.First != r2.First)
            {
                diffReason = $"Ranges[{i}].First differs: 0x{r1.First:X} != 0x{r2.First:X}";
                return false;
            }
            if (r1.Count != r2.Count)
            {
                diffReason = $"Ranges[{i}].Count differs: {r1.Count} != {r2.Count}";
                return false;
            }
            if (r1.KernCount != r2.KernCount)
            {
                diffReason = $"Ranges[{i}].KernCount differs: {r1.KernCount} != {r2.KernCount}";
                return false;
            }
            if (!r1.Kerning.AsSpan().SequenceEqual(r2.Kerning))
            {
                diffReason = $"Ranges[{i}].Kerning bytes differ";
                return false;
            }
            if (r1.RawBlock is not null && r2.RawBlock is not null && !r1.RawBlock.AsSpan().SequenceEqual(r2.RawBlock))
            {
                diffReason = $"Ranges[{i}].RawBlock bytes differ";
                return false;
            }
            for (int j = 0; j < r1.Glyphs.Count; j++)
            {
                var g1 = r1.Glyphs[j];
                var g2 = r2.Glyphs[j];
                if (g1.A != g2.A || g1.B != g2.B || g1.C != g2.C ||
                    g1.Top != g2.Top || g1.Width != g2.Width || g1.Height != g2.Height ||
                    !g1.Pixels.AsSpan().SequenceEqual(g2.Pixels))
                {
                    diffReason = $"Ranges[{i}].Glyphs[{j}] (char 0x{r1.First + j:X}) differs: (A={g1.A},B={g1.B},C={g1.C},Top={g1.Top},W={g1.Width},H={g1.Height}) != (A={g2.A},B={g2.B},C={g2.C},Top={g2.Top},W={g2.Width},H={g2.Height})";
                    return false;
                }
            }
        }
        diffReason = string.Empty;
        return true;
    }

    /// <summary>
    /// 診斷兩個 APF 位元組陣列的第一個差異位置與對應結構名稱。
    /// </summary>
    public static string DiagnoseByteDifference(byte[] expected, byte[] actual)
    {
        if (expected.Length != actual.Length)
        {
            int minLen = Math.Min(expected.Length, actual.Length);
            for (int i = 0; i < minLen; i++)
            {
                if (expected[i] != actual[i])
                {
                    string structName = DescribeApfOffset(expected, i);
                    return $"長度不同 (expected {expected.Length}, actual {actual.Length}); 第一個相異位移: 0x{i:X4} ({i}) [結構: {structName}], expected=0x{expected[i]:X2} ({expected[i]}), actual=0x{actual[i]:X2} ({actual[i]})";
                }
            }
            return $"長度不同 (expected {expected.Length}, actual {actual.Length}); 前 {minLen} 位元組皆相同，第 0x{minLen:X4} 位移起截斷或多餘";
        }

        for (int i = 0; i < expected.Length; i++)
        {
            if (expected[i] != actual[i])
            {
                string structName = DescribeApfOffset(expected, i);
                return $"第一個相異位移: 0x{i:X4} ({i}) [結構: {structName}], expected=0x{expected[i]:X2} ({expected[i]}), actual=0x{actual[i]:X2} ({actual[i]})";
            }
        }

        return "無差異 (逐位元組相同)";
    }

    /// <summary>
    /// 說明 APF 檔案中指定 offset 所對應之二進位結構區段。
    /// </summary>
    public static string DescribeApfOffset(byte[] data, int offset)
    {
        if (offset < 4) return "Header Magic (0x00..0x03)";
        if (offset < 0x1C) return $"Header Unk[{(offset - 4) / 4}] (0x04..0x1B)";
        if (offset < 0x20) return "Header metricsOffset (0x1C..0x1F)";

        int metricsOffset = data.Length >= 0x20 ? (int)BitConverter.ToUInt32(data, 0x1C) : 0x20;
        if (offset < metricsOffset) return $"Header Face/Family 字串區 (0x20..0x{metricsOffset:X2})";

        if (offset < metricsOffset + 80)
        {
            int metricIdx = (offset - metricsOffset) / 4;
            int metricByte = (offset - metricsOffset) % 4;
            string metricName = metricIdx switch
            {
                0 => "PixelSize",
                1 => "Italic",
                2 => "Bold",
                3 => "FontDataOffset",
                4 => "FontDataSize",
                5 => "LineHeight",
                6 => "MaxWidth",
                10 => "Ascent",
                11 => "Descent",
                19 => "RangeCount",
                _ => $"Reserved[{metricIdx}]"
            };
            return $"Metrics[{metricIdx}] ({metricName}, byte {metricByte} at offset 0x{offset:X2})";
        }

        int rangeCount = data.Length >= metricsOffset + 80 ? BitConverter.ToInt32(data, metricsOffset + 76) : 0;
        int rangeTableEnd = metricsOffset + 80 + 16 * rangeCount;
        if (offset < rangeTableEnd)
        {
            int rangeIdx = (offset - (metricsOffset + 80)) / 16;
            int fieldIdx = ((offset - (metricsOffset + 80)) % 16) / 4;
            string fieldName = fieldIdx switch
            {
                0 => "offset",
                1 => "size",
                2 => "first",
                3 => "count",
                _ => "unknown"
            };
            return $"Range Table [Entry #{rangeIdx}.{fieldName}] (offset 0x{offset:X2})";
        }

        return $"Glyph Block Data (offset 0x{offset:X2}, 距範圍表結尾 +0x{offset - rangeTableEnd:X})";
    }

    private static string ReadCString(byte[] data, ref int pos)
    {
        int start = pos;
        while (pos < data.Length && data[pos] != 0) pos++;
        string s = NameEncoding.GetString(data, start, pos - start);
        pos++;
        return s;
    }

    private static void WriteI32(byte[] buf, ref int pos, int value)
    {
        buf[pos++] = (byte)value;
        buf[pos++] = (byte)(value >> 8);
        buf[pos++] = (byte)(value >> 16);
        buf[pos++] = (byte)(value >> 24);
    }
}

public sealed class Glyph
{
    public int A { get; set; }          // 左間距（GDI 的 A）
    public int B { get; set; }          // 墨寬（GDI 的 B）
    public int C { get; set; }          // 右間距（GDI 的 C）
    public int Top { get; set; }        // 由行首往下的列
    public int Width { get; set; }
    public int Height { get; set; }
    public byte[] Pixels { get; set; } = []; // 長度 Width*Height，值 0..14

    public int Advance => A + B + C;
}

public sealed class GlyphRange(int first)
{
    public int First { get; set; } = first;
    public List<Glyph> Glyphs { get; set; } = [];
    public byte[] Kerning { get; set; } = [];
    public int KernCount { get; set; }
    public byte[]? RawBlock { get; set; }
    public bool IsOriginal { get; set; } = true;
    public int? OriginalCount { get; set; }
    public byte[]? OriginalRawBlock { get; set; }

    public int Count => Glyphs.Count;
}
