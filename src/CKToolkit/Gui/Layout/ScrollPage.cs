using System.Windows.Forms.Layout;

namespace CKToolkit.Gui.Layout;

/// <summary>
/// 每一個分頁的共同底座（ISSUE-101）：內容放在 <see cref="Content"/> 這個 <see cref="StackPanel"/> 裡，
/// 寬度跟著分頁走、高度由內容量出來；放不下才出捲軸，<b>永遠不裁切</b>。
///
/// 捲軸要不要出現是由這裡一次算定的，而不是交給 <see cref="ScrollableControl"/> 依子控制項邊界去推：
/// 先以「有直向捲軸」的較窄寬度量一次高度，裝得下才改用全寬。這樣不會發生
/// 「文字變寬 → 行數變少 → 捲軸消失 → 又變窄」的來回震盪。
///
/// 內容裡若有 <see cref="StackPanel.AddFill"/> 的控制項（表格、報告框），視窗夠高時它們吃掉剩餘高度，
/// 視窗太矮時保留最小高度並改由分頁捲動。
/// </summary>
public class ScrollPage : UserControl
{
    private static readonly PageLayoutEngine Engine = new();
    private bool _inLayout;

    public ScrollPage()
    {
        AutoScroll = true;
        BackColor = Color.White;
        Padding = new Padding(16);
        Content = new StackPanel { BackColor = Color.Transparent, Margin = Padding.Empty };
        Controls.Add(Content);
    }

    /// <summary>分頁的根堆疊。所有內容都加在這裡。</summary>
    protected internal StackPanel Content { get; }

    /// <summary>內容最窄可以到幾個邏輯像素；再窄就出橫向捲軸。</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int MinimumContentLogicalWidth { get; set; } = 520;

    public override LayoutEngine LayoutEngine => Engine;

    private sealed class PageLayoutEngine : LayoutEngine
    {
        public override bool Layout(object container, LayoutEventArgs layoutEventArgs)
        {
            var page = (ScrollPage)container;
            if (page._inLayout) return false;
            page._inLayout = true;
            try
            {
                page.LayoutContent();
            }
            finally
            {
                page._inLayout = false;
            }
            return false;
        }
    }

    private void LayoutContent()
    {
        // 以整個控制項大小當視窗，自己決定捲軸，不依賴「目前」捲軸是否顯示。
        // 建構子裡設定 AutoScroll 就會觸發第一次排版，那時 Content 還沒建立。
        if (Content is null) return;
        Size outer = Size;
        if (outer.Width <= 0 || outer.Height <= 0) return;

        int scrollBarWidth = SystemInformation.GetVerticalScrollBarWidthForDpi(DeviceDpi);
        int scrollBarHeight = SystemInformation.GetHorizontalScrollBarHeightForDpi(DeviceDpi);
        int padH = Padding.Horizontal, padV = Padding.Vertical;
        int minWidth = Math.Max(LogicalToDeviceUnits(MinimumContentLogicalWidth), Content.GetMinimumContentWidth());

        // 第一輪：假設不需要直向捲軸。
        int width = Math.Max(outer.Width - padH, minWidth);
        bool horizontal = width > outer.Width - padH;
        int viewportHeight = outer.Height - (horizontal ? scrollBarHeight : 0);
        int height = Content.GetPreferredSize(new Size(width, 0)).Height;
        bool vertical = height + padV > viewportHeight;

        if (vertical)
        {
            // 第二輪：留出直向捲軸的寬度重新量。
            width = Math.Max(outer.Width - scrollBarWidth - padH, minWidth);
            horizontal = width > outer.Width - scrollBarWidth - padH;
            viewportHeight = outer.Height - (horizontal ? scrollBarHeight : 0);
            height = Content.GetPreferredSize(new Size(width, 0)).Height;
        }

        if (Content.HasFillChild())
            height = Math.Max(height, viewportHeight - padV);

        var extent = new Size(horizontal ? width + padH : 0, vertical ? height + padV : 0);
        if (AutoScrollMinSize != extent) AutoScrollMinSize = extent;

        Point origin = AutoScrollPosition;
        var bounds = new Rectangle(origin.X + Padding.Left, origin.Y + Padding.Top, width, height);
        if (Content.Bounds != bounds) Content.Bounds = bounds;
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        PerformLayout();
    }
}
