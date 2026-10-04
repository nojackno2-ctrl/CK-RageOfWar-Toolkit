using System.Drawing.Drawing2D;

namespace CKToolkit.Gui.Layout;

/// <summary>
/// 帶標題的卡片，取代 <see cref="GroupBox"/>（ISSUE-101）。
///
/// GroupBox 的標題列高度與內容區由系統決定，對外回報的偏好高度又不含內部停靠面板，
/// 結果就是外層只能猜一個 <c>MinimumSize</c>——猜錯就切掉內容。卡片自己量標題、
/// 自己量內容，回報的高度永遠剛好裝得下。
/// </summary>
public sealed class Card : StackPanel
{
    private static readonly Color BorderColor = Color.FromArgb(226, 232, 240);
    private static readonly Color TitleColor = Color.FromArgb(30, 41, 59);
    private Font? _titleFont;

    public Card()
    {
        BackColor = Color.White;
        Padding = new Padding(14, 10, 14, 12);
        Margin = new Padding(0, 0, 0, 12);
    }

    /// <summary>標題字串；空字串時不留標題列。</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public string Title
    {
        get => Text;
        set
        {
            if (Text == value) return;
            Text = value;
            PerformLayout();
            Parent?.PerformLayout();
            Invalidate();
        }
    }

    private Font TitleFont => _titleFont ??= new Font(Font.FontFamily, Font.SizeInPoints * 1.1f, FontStyle.Bold);

    protected override void OnFontChanged(EventArgs e)
    {
        _titleFont?.Dispose();
        _titleFont = null;
        base.OnFontChanged(e);
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        _titleFont?.Dispose();
        _titleFont = null;
        base.OnDpiChangedAfterParent(e);
    }

    private int TitleHeight(int outerWidth)
    {
        if (string.IsNullOrEmpty(Text)) return 0;
        int width = Math.Max(1, outerWidth - Padding.Horizontal);
        Size size = TextRenderer.MeasureText(Text, TitleFont, new Size(width, int.MaxValue),
            TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
        return size.Height + LogicalToDeviceUnits(8);
    }

    internal override Rectangle GetContentRectangle(Size outer)
    {
        int title = TitleHeight(outer.Width);
        return new Rectangle(Padding.Left, Padding.Top + title,
            Math.Max(0, outer.Width - Padding.Horizontal),
            Math.Max(0, outer.Height - Padding.Vertical - title));
    }

    internal override int GetChromeHeight(int outerWidth) => Padding.Vertical + TitleHeight(outerWidth);

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        int radius = LogicalToDeviceUnits(6);
        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using (GraphicsPath path = RoundedRectangle(rect, radius))
        using (var pen = new Pen(BorderColor))
            e.Graphics.DrawPath(pen, path);

        if (!string.IsNullOrEmpty(Text))
        {
            var titleRect = new Rectangle(Padding.Left, Padding.Top,
                Math.Max(1, Width - Padding.Horizontal), TitleHeight(Width));
            TextRenderer.DrawText(e.Graphics, Text, TitleFont, titleRect, TitleColor,
                TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.Left | TextFormatFlags.Top);
        }
    }

    private static GraphicsPath RoundedRectangle(Rectangle r, int radius)
    {
        var path = new GraphicsPath();
        int d = Math.Max(1, radius * 2);
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _titleFont?.Dispose();
        base.Dispose(disposing);
    }
}
