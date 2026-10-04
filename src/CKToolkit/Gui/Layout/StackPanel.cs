using System.Windows.Forms.Layout;

namespace CKToolkit.Gui.Layout;

/// <summary>
/// 由上而下堆疊子控制項的容器——整個 GUI 版面的骨幹（ISSUE-101）。
///
/// 規則只有三條，而且全部在<b>每一次</b>版面計算時依「當下的字型與 DPI」重新量測，
/// 所以換一台電腦、換一個縮放比例、換一種介面語言都不會有任何一個數字是舊的：
/// <list type="number">
///   <item>每個子控制項的寬度＝容器寬度（扣掉邊距）；高度＝它在這個寬度下自己回報的偏好高度
///         （<see cref="Control.GetPreferredSize"/>）。說明文字因此自動換行、卡片自動長高，
///         沒有任何寫死的高度或百分比可以把內容切掉。</item>
///   <item>以 <see cref="AddFill"/> 加入的子控制項（表格、記錄框）分到剩下的高度，
///         但至少有它宣告的最小高度；容器再矮就由外層 <see cref="ScrollPage"/> 出捲軸。</item>
///   <item>以 <see cref="AddFixed"/> 加入的子控制項有固定高度，單位是<b>邏輯像素</b>（96 DPI），
///         在版面計算時才換算成實際像素，所以不會被 WinForms 的自動縮放重複放大。</item>
/// </list>
///
/// 以前的版面用 <c>RowStyle(Percent)</c> 分配高度：比例是照開發機上的字型高度抓的，
/// 換到字比較大的螢幕上，內容比分到的高度高，就被群組框切掉一截（ISSUE-099 的截圖）。
/// 這裡不再有比例，高度永遠是量出來的。
/// </summary>
public class StackPanel : Panel
{
    private static readonly StackLayoutEngine Engine = new();
    private readonly Dictionary<Control, ChildSpec> _specs = new();

    public StackPanel()
    {
        DoubleBuffered = true;
        // AutoSize 本身不讓它自己調整大小（大小一律由父層決定），而是讓 WinForms 知道
        // 這個容器的偏好尺寸會隨內容改變：子控制項改字時，版面異動才會一路往上傳。
        AutoSize = true;
        SetStyle(ControlStyles.ResizeRedraw, true);
    }

    public override LayoutEngine LayoutEngine => Engine;

    /// <summary>一般子控制項：撐滿寬度、高度取偏好高度。</summary>
    public T Add<T>(T control) where T : Control => AddCore(control, new ChildSpec(SizeKind.Natural, 0, true, 1));

    /// <summary>保持自然寬度、靠左（例如單獨一顆按鈕）。</summary>
    public T AddNatural<T>(T control) where T : Control => AddCore(control, new ChildSpec(SizeKind.Natural, 0, false, 1));

    /// <summary>固定高度（邏輯像素）。</summary>
    public T AddFixed<T>(T control, int logicalHeight) where T : Control =>
        AddCore(control, new ChildSpec(SizeKind.Fixed, logicalHeight, true, 1));

    /// <summary>
    /// 分到剩餘高度，但至少 <paramref name="minimumLogicalHeight"/> 邏輯像素。
    /// 有多個 Fill 時依 <paramref name="weight"/> 比例分配剩餘高度。
    /// </summary>
    public T AddFill<T>(T control, int minimumLogicalHeight, int weight = 1) where T : Control =>
        AddCore(control, new ChildSpec(SizeKind.Fill, minimumLogicalHeight, true, Math.Max(1, weight)));

    /// <summary>
    /// 跟 <see cref="AddFill"/> 一樣分到剩餘高度，但最小高度是它自己量出來的偏好高度
    /// （內含堆疊的分頁控制項用這個）。
    /// </summary>
    public T AddGrow<T>(T control) where T : Control =>
        AddCore(control, new ChildSpec(SizeKind.Fill, -1, true, 1));

    /// <summary>
    /// 固定高度改成「幾行文字」：依該控制項自己的字型換算。記錄框之類的高度用這個，
    /// 字型或 DPI 一變，行數不變。
    /// </summary>
    public T AddLines<T>(T control, int lines) where T : Control =>
        AddCore(control, new ChildSpec(SizeKind.Lines, lines, true, 1));

    private T AddCore<T>(T control, ChildSpec spec) where T : Control
    {
        _specs[control] = spec;
        Controls.Add(control);
        return control;
    }

    protected override void OnControlRemoved(ControlEventArgs e)
    {
        if (e.Control is not null) _specs.Remove(e.Control);
        base.OnControlRemoved(e);
    }

    /// <summary>子控制項實際可用的矩形。<see cref="Card"/> 會再扣掉標題列。</summary>
    internal virtual Rectangle GetContentRectangle(Size outer) =>
        new(Padding.Left, Padding.Top,
            Math.Max(0, outer.Width - Padding.Horizontal),
            Math.Max(0, outer.Height - Padding.Vertical));

    /// <summary>容器本身在內容之外額外需要的高度（Padding、卡片標題列）。</summary>
    internal virtual int GetChromeHeight(int outerWidth) => Padding.Vertical;

    internal bool HasFillChild()
    {
        foreach (Control child in Controls)
            if (child.Visible && SpecOf(child).Kind == SizeKind.Fill) return true;
        return false;
    }

    private ChildSpec SpecOf(Control child) =>
        _specs.TryGetValue(child, out ChildSpec spec) ? spec : new ChildSpec(SizeKind.Natural, 0, true, 1);

    /// <summary>
    /// 不換行就放不下的最窄寬度：核取方塊、按鈕、下拉選單這些不會換行的控制項裡最寬的那個。
    /// 視窗比這個窄時由 <see cref="ScrollPage"/> 出橫向捲軸，而不是把文字切掉。
    /// </summary>
    internal int GetMinimumContentWidth()
    {
        int widest = 0;
        foreach (Control child in Controls)
        {
            if (!child.Visible) continue;
            widest = Math.Max(widest, MinimumWidthOf(child) + child.Margin.Horizontal);
        }
        return widest + Padding.Horizontal;
    }

    internal static int MinimumWidthOf(Control control)
    {
        switch (control)
        {
            case StackPanel stack:
                return stack.GetMinimumContentWidth();
            case Label:
                // 說明文字會換行，給它幾個字的寬度就夠了。
                return control.Font.Height * 8;
            case TableLayoutPanel table:
                return MinimumWidthOf(table);
            case FlowLayoutPanel flow:
                // 換行面板最窄就是「最寬的那一格」；不換行的面板是全部加總。
                int sum = 0, widest = 0;
                foreach (Control child in flow.Controls)
                {
                    if (!child.Visible) continue;
                    int w = MinimumWidthOf(child) + child.Margin.Horizontal;
                    sum += w;
                    widest = Math.Max(widest, w);
                }
                return (flow.WrapContents ? widest : sum) + flow.Padding.Horizontal;
            case ComboBox when control.Anchor.HasFlag(AnchorStyles.Right):
                // 被表格撐滿的下拉選單／輸入框可以縮窄，它的最小寬度不是內容長度。
                return Math.Max(control.MinimumSize.Width, control.Font.Height * 6);
            case DataGridView or TextBoxBase or TabControl or PictureBox or ListBox or ListView:
                return control.MinimumSize.Width > 0 ? control.MinimumSize.Width : control.Font.Height * 12;
            default:
                return Math.Max(control.MinimumSize.Width, control.GetPreferredSize(Size.Empty).Width);
        }
    }

    /// <summary>
    /// 表格的最小寬度：每一欄取該欄各格最小寬度的最大值再加總（跨欄的格子不計）。
    /// 直接問 TableLayoutPanel 的偏好寬度會拿到「文字框裡那串路徑的全長」，不是最小寬度。
    /// </summary>
    private static int MinimumWidthOf(TableLayoutPanel table)
    {
        var columns = new int[Math.Max(1, table.ColumnCount)];
        foreach (Control child in table.Controls)
        {
            if (!child.Visible || table.GetColumnSpan(child) > 1) continue;
            int column = Math.Clamp(table.GetColumn(child), 0, columns.Length - 1);
            columns[column] = Math.Max(columns[column], MinimumWidthOf(child) + child.Margin.Horizontal);
        }
        return columns.Sum() + table.Padding.Horizontal;
    }

    /// <summary>某個子控制項在指定寬度下需要的高度（不含 Fill 的額外分配）。</summary>
    internal int MeasureChild(Control child, int width)
    {
        ChildSpec spec = SpecOf(child);
        int height = spec.Kind switch
        {
            SizeKind.Fixed => child.LogicalToDeviceUnits(spec.Value),
            SizeKind.Fill when spec.Value < 0 => MeasureNatural(child, width),
            // 版面容器（例如窄螢幕上改成上下排的 ColumnsPanel）自己量得出最小需求，
            // 取它與宣告的最小高度中較大者；表格、文字框之類的偏好高度沒有意義，只看宣告值。
            SizeKind.Fill when IsLayoutContainer(child) =>
                Math.Max(child.LogicalToDeviceUnits(spec.Value), MeasureNatural(child, width)),
            SizeKind.Fill => child.LogicalToDeviceUnits(spec.Value),
            SizeKind.Lines => spec.Value * child.Font.Height + child.LogicalToDeviceUnits(8),
            _ => MeasureNatural(child, width)
        };
        if (child.MinimumSize.Height > 0) height = Math.Max(height, child.MinimumSize.Height);
        if (child.MaximumSize.Height > 0) height = Math.Min(height, child.MaximumSize.Height);
        return height;
    }

    private static bool IsLayoutContainer(Control child) =>
        child is StackPanel or ColumnsPanel or UniformGrid or ContentTabControl;

    private static int MeasureNatural(Control child, int width)
    {
        // 寬度給定、高度不限：Label、TableLayoutPanel、FlowLayoutPanel、StackPanel 都會依寬度換行。
        Size preferred = child.GetPreferredSize(new Size(Math.Max(1, width), 0));
        return preferred.Height;
    }

    internal int ChildWidth(Control child, int available)
    {
        int width = Math.Max(0, available - child.Margin.Horizontal);
        if (!SpecOf(child).Stretch)
            width = Math.Min(width, Math.Max(child.MinimumSize.Width, child.GetPreferredSize(Size.Empty).Width));
        return width;
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        int outerWidth = proposedSize.Width is > 0 and < int.MaxValue / 2
            ? proposedSize.Width
            : GetMinimumContentWidth();
        Rectangle content = GetContentRectangle(new Size(outerWidth, 0));
        int height = 0;
        foreach (Control child in Controls)
        {
            if (!child.Visible) continue;
            height += MeasureChild(child, ChildWidth(child, content.Width)) + child.Margin.Vertical;
        }
        return new Size(outerWidth, height + GetChromeHeight(outerWidth));
    }

    private enum SizeKind { Natural, Fixed, Fill, Lines }

    private readonly record struct ChildSpec(SizeKind Kind, int Value, bool Stretch, int Weight);

    private sealed class StackLayoutEngine : LayoutEngine
    {
        private static int fillCountTotal(StackPanel stack) => stack.HasFillChild() ? 1 : 0;

        public override bool Layout(object container, LayoutEventArgs layoutEventArgs)
        {
            var stack = (StackPanel)container;
            Rectangle content = stack.GetContentRectangle(stack.ClientSize);

            var visible = new List<Control>();
            foreach (Control child in stack.Controls)
                if (child.Visible) visible.Add(child);

            // 先量每個子控制項需要的高度，剩下的高度平均分給 Fill。
            var heights = new int[visible.Count];
            int used = 0, fillWeight = 0;
            for (int i = 0; i < visible.Count; i++)
            {
                Control child = visible[i];
                heights[i] = stack.MeasureChild(child, stack.ChildWidth(child, content.Width));
                used += heights[i] + child.Margin.Vertical;
                ChildSpec childSpec = stack.SpecOf(child);
                if (childSpec.Kind == SizeKind.Fill) fillWeight += childSpec.Weight;
            }

            int spare = Math.Max(0, content.Height - used);
            int y = content.Top;
            for (int i = 0; i < visible.Count; i++)
            {
                Control child = visible[i];
                int height = heights[i];
                ChildSpec spec = stack.SpecOf(child);
                if (fillWeight > 0 && spec.Kind == SizeKind.Fill)
                {
                    int share = spare * spec.Weight / fillWeight;
                    height += share;
                    spare -= share;
                    fillWeight -= spec.Weight;
                }
                int width = stack.ChildWidth(child, content.Width);
                y += child.Margin.Top;
                var bounds = new Rectangle(content.Left + child.Margin.Left, y, width, height);
                if (child.Bounds != bounds) child.Bounds = bounds;
                y += height + child.Margin.Bottom;
            }

            // 回傳 true 會讓父層重排。內容需要的高度跟目前不同（說明文字換了語言、
            // 卡片多了一行）就要往上通報，外層的捲動範圍才會跟著更新。
            int needed = used + stack.GetChromeHeight(stack.Width);
            return fillCountTotal(stack) == 0 ? needed != stack.Height : needed > stack.Height;
        }
    }
}
