using CKToolkit.I18n;
using System.Runtime.InteropServices;
using System.Text;

namespace CKToolkit.Core.Lang;

/// <summary>
/// 用 Windows GDI 光柵化字形，產生 APF 需要的灰階點陣與 ABC 間距。
///
/// 原始字型的灰階只有 0,2,4,…,14 八階。把原檔與 GDI 的 GGO_GRAY8_BITMAP（0..64）
/// 逐像素比對後還原出的量化規則就是 ((raw + 4) / 9) * 2，照著做才能和原版字形一致。
/// </summary>
public sealed class GdiFont : IDisposable
{
    private const uint GgoGray8Bitmap = 6;
    private const uint GdiError = 0xFFFFFFFF;
    private const byte AntialiasedQuality = 4;
    private const byte DefaultCharset = 1;
    private const byte OutTtPrecis = 4;

    private static readonly byte[] GrayToLevel = BuildGrayTable();

    private IntPtr _hdc;
    private IntPtr _hfont;
    private IntPtr _oldFont;
    private IntPtr _glyphBuffer = IntPtr.Zero;
    private int _glyphBufferCapacity = 0;

    public int Ascent { get; }
    public int Descent { get; }
    public int Height { get; }
    public int Baseline { get; }
    public string ActualFace { get; }

    /// <param name="face">字型名稱（如 "微軟正黑體"）</param>
    /// <param name="pixelSize">字級（像素）</param>
    /// <param name="bold">是否為粗體</param>
    /// <param name="baseline">
    /// 混排時所有字型必須共用同一條基線（用原 APF 的 ascent），
    /// 否則中文會相對英文上下亂跳。
    /// </param>
    public GdiFont(string face, int pixelSize, bool bold, int baseline = -1)
    {
        var lf = new LogFont
        {
            lfHeight = -Math.Abs(pixelSize),
            lfWeight = bold ? 700 : 400,
            lfCharSet = DefaultCharset,
            lfOutPrecision = OutTtPrecis,
            lfQuality = AntialiasedQuality,
            lfFaceName = face
        };
        _hfont = CreateFontIndirect(ref lf);
        if (_hfont == IntPtr.Zero)
        {
            throw new InvalidOperationException(Strings.Get("Error_FontCreateFailed", face, pixelSize));
        }

        _hdc = CreateCompatibleDC(IntPtr.Zero);
        _oldFont = SelectObject(_hdc, _hfont);

        GetTextMetrics(_hdc, out var tm);
        Ascent = tm.tmAscent;
        Descent = tm.tmDescent;
        Height = tm.tmHeight;
        Baseline = baseline < 0 ? tm.tmAscent : baseline;

        var buf = new StringBuilder(64);
        GetTextFace(_hdc, 64, buf);
        ActualFace = buf.ToString();
    }

    /// <summary>取得字形。找不到時回傳 false。</summary>
    public bool TryGetGlyph(int codepoint, out Glyph? glyph)
    {
        glyph = null;
        var mat = Mat2Identity();

        uint need = GetGlyphOutline(_hdc, (uint)codepoint, GgoGray8Bitmap,
                                    out var gm, 0, IntPtr.Zero, ref mat);
        if (need == GdiError)
        {
            return false;
        }

        if (need == 0)
        {
            // 無墨（空白字元）
            glyph = new Glyph
            {
                A = 0,
                B = Math.Max((int)gm.gmCellIncX, 0),
                C = 0,
                Top = Baseline,
                Width = 0,
                Height = 0,
                Pixels = []
            };
            return true;
        }

        if ((int)need > _glyphBufferCapacity)
        {
            if (_glyphBuffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_glyphBuffer);
            }
            int newCap = Math.Max((int)need, 4096);
            _glyphBuffer = Marshal.AllocHGlobal(newCap);
            _glyphBufferCapacity = newCap;
        }

        uint got = GetGlyphOutline(_hdc, (uint)codepoint, GgoGray8Bitmap,
                                   out gm, need, _glyphBuffer, ref mat);
        if (got == GdiError)
        {
            return false;
        }

        int w = (int)gm.gmBlackBoxX;
        int h = (int)gm.gmBlackBoxY;
        int pitch = (w + 3) & ~3;
        var pixels = new byte[w * h];

        unsafe
        {
            var rawSpan = new ReadOnlySpan<byte>((void*)_glyphBuffer, (int)need);
            for (int y = 0; y < h; y++)
            {
                int src = y * pitch;
                int dst = y * w;
                for (int x = 0; x < w; x++)
                {
                    byte v = rawSpan[src + x];
                    pixels[dst + x] = v <= 64 ? GrayToLevel[v] : (byte)ApfFont.MaxLevel;
                }
            }
        }

        int a = gm.gmptGlyphOriginX;
        glyph = new Glyph
        {
            A = a,
            B = w,
            C = gm.gmCellIncX - a - w,
            Top = Baseline - gm.gmptGlyphOriginY,
            Width = w,
            Height = h,
            Pixels = pixels
        };
        return true;
    }

    public void Dispose()
    {
        if (_glyphBuffer != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_glyphBuffer);
            _glyphBuffer = IntPtr.Zero;
            _glyphBufferCapacity = 0;
        }
        if (_hdc != IntPtr.Zero)
        {
            SelectObject(_hdc, _oldFont);
            DeleteDC(_hdc);
            _hdc = IntPtr.Zero;
        }
        if (_hfont != IntPtr.Zero)
        {
            DeleteObject(_hfont);
            _hfont = IntPtr.Zero;
        }
    }

    /// <summary>檢查系統是否真的有這個字型（GDI 找不到時會悄悄代換成別的）。</summary>
    public static bool Exists(string face)
    {
        try
        {
            using var f = new GdiFont(face, 16, false);
            return string.Equals(f.ActualFace, face, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static byte[] BuildGrayTable()
    {
        var table = new byte[65];
        for (int v = 0; v <= 64; v++)
        {
            int level = ((v + 4) / 9) * 2;
            table[v] = (byte)Math.Min(level, ApfFont.MaxLevel);
        }
        return table;
    }

    private static Mat2 Mat2Identity() => new()
    {
        eM11 = new Fixed { fract = 0, value = 1 },
        eM12 = new Fixed { fract = 0, value = 0 },
        eM21 = new Fixed { fract = 0, value = 0 },
        eM22 = new Fixed { fract = 0, value = 1 }
    };

    // ------------------------------------------------------------ P/Invoke

    [StructLayout(LayoutKind.Sequential)]
    private struct Fixed { public ushort fract; public short value; }

    [StructLayout(LayoutKind.Sequential)]
    private struct Mat2 { public Fixed eM11, eM12, eM21, eM22; }

    [StructLayout(LayoutKind.Sequential)]
    private struct GlyphMetrics
    {
        public uint gmBlackBoxX;
        public uint gmBlackBoxY;
        public int gmptGlyphOriginX;
        public int gmptGlyphOriginY;
        public short gmCellIncX;
        public short gmCellIncY;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct LogFont
    {
        public int lfHeight, lfWidth, lfEscapement, lfOrientation, lfWeight;
        public byte lfItalic, lfUnderline, lfStrikeOut, lfCharSet;
        public byte lfOutPrecision, lfClipPrecision, lfQuality, lfPitchAndFamily;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string lfFaceName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct TextMetric
    {
        public int tmHeight, tmAscent, tmDescent, tmInternalLeading, tmExternalLeading;
        public int tmAveCharWidth, tmMaxCharWidth, tmWeight, tmOverhang;
        public int tmDigitizedAspectX, tmDigitizedAspectY;
        public char tmFirstChar, tmLastChar, tmDefaultChar, tmBreakChar;
        public byte tmItalic, tmUnderlined, tmStruckOut, tmPitchAndFamily, tmCharSet;
    }

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode, EntryPoint = "CreateFontIndirectW")]
    private static extern IntPtr CreateFontIndirect(ref LogFont lf);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr obj);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetTextMetricsW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTextMetrics(IntPtr hdc, out TextMetric tm);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetTextFaceW")]
    private static extern int GetTextFace(IntPtr hdc, int count, StringBuilder faceName);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetGlyphOutlineW")]
    private static extern uint GetGlyphOutline(IntPtr hdc, uint ch, uint format,
                                               out GlyphMetrics gm, uint cbBuffer,
                                               IntPtr buffer, ref Mat2 mat);
}
