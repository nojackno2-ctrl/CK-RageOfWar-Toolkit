using System.Windows.Forms.Layout;

namespace CKToolkit.Gui.Layout;

/// <summary>
/// 寬螢幕上把幾張卡片並排、窄螢幕上自動改成上下排的容器（ISSUE-081）。
///
/// 欄寬依 <see cref="Add"/> 給的權重分配；任何一欄窄於 <see cref="MinimumColumnLogicalWidth"/>
/// （或窄於它不換行就放不下的寬度）時整組改成上下堆疊。並排時所有欄位取最高那欄的高度，
/// 若容器被父層給了更多高度（<see cref="StackPanel.AddGrow"/>），各欄一起延展到底。
/// 上下排時多出來的高度給最後一欄。
/// </summary>
public sealed class ColumnsPanel : Panel
{
    private static readonly ColumnsEngine Engine = new();
    private readonly Dictionary<Control, int> _weights = new();
    private readonly Dictionary<Control, int> _minimums = new();

    public ColumnsPanel()
    {
        AutoSize = true;
        BackColor = Color.Transparent;
        Margin = Padding.Empty;
    }

    /// <summary>每欄至少要有幾個邏輯像素寬，不到就改成上下排。</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int MinimumColumnLogicalWidth { get; set; } = 360;

    /// <summary>欄與欄之間的間距（邏輯像素）。</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int GapLogical { get; set; } = 12;

    public override LayoutEngine LayoutEngine => Engine;

    /// <summary>
    /// 加入一欄。<paramref name="weight"/> 是並排時的相對寬度；
    /// <paramref name="minimumLogicalWidth"/> 是這一欄並排時至少要有的寬度（預設 <see cref="MinimumColumnLogicalWidth"/>）。
    /// </summary>
    public T Add<T>(T control, int weight = 1, int? minimumLogicalWidth = null) where T : Control
    {
        _weights[control] = Math.Max(1, weight);
        if (minimumLogicalWidth is int min) _minimums[control] = min;
        Controls.Add(control);
        return control;
    }

    protected override void OnControlRemoved(ControlEventArgs e)
    {
        if (e.Control is not null)
        {
            _weights.Remove(e.Control);
            _minimums.Remove(e.Control);
        }
        base.OnControlRemoved(e);
    }

    private List<Control> VisibleChildren()
    {
        var list = new List<Control>();
        foreach (Control child in Controls) if (child.Visible) list.Add(child);
        return list;
    }

    private int WeightOf(Control child) => _weights.TryGetValue(child, out int w) ? w : 1;

    /// <summary>並排時各欄寬度；任何一欄放不下就回傳 null（改成上下排）。</summary>
    private int[]? ColumnWidths(int width, List<Control> children)
    {
        if (children.Count < 2) return null;
        int gap = LogicalToDeviceUnits(GapLogical);
        int available = width - gap * (children.Count - 1);
        int total = children.Sum(WeightOf);
        var widths = new int[children.Count];
        int remaining = available;
        for (int i = 0; i < children.Count; i++)
        {
            Control child = children[i];
            widths[i] = i == children.Count - 1 ? remaining : available * WeightOf(child) / total;
            remaining -= widths[i];
            int required = Math.Max(LogicalToDeviceUnits(MinimumOf(child)),
                                    StackPanel.MinimumWidthOf(child) + child.Margin.Horizontal);
            if (widths[i] < required) return null;
        }
        return widths;
    }

    private int MinimumOf(Control child) =>
        _minimums.TryGetValue(child, out int min) ? min : MinimumColumnLogicalWidth;

    private static int ChildHeight(Control child, int width) =>
        child.GetPreferredSize(new Size(Math.Max(1, width - child.Margin.Horizontal), 0)).Height + child.Margin.Vertical;

    public override Size GetPreferredSize(Size proposedSize)
    {
        List<Control> children = VisibleChildren();
        if (proposedSize.Width is <= 0 or >= int.MaxValue / 2)
        {
            int min = 0;
            foreach (Control child in children) min = Math.Max(min, StackPanel.MinimumWidthOf(child) + child.Margin.Horizontal);
            proposedSize.Width = min;
        }
        int width = proposedSize.Width;
        int height = 0;
        int[]? widths = ColumnWidths(width, children);
        if (widths is not null)
        {
            for (int i = 0; i < children.Count; i++) height = Math.Max(height, ChildHeight(children[i], widths[i]));
        }
        else
        {
            foreach (Control child in children) height += ChildHeight(child, width);
        }
        return new Size(width, height);
    }

    private sealed class ColumnsEngine : LayoutEngine
    {
        public override bool Layout(object container, LayoutEventArgs layoutEventArgs)
        {
            var panel = (ColumnsPanel)container;
            List<Control> children = panel.VisibleChildren();
            int width = panel.ClientSize.Width;
            int needed;
            int[]? widths = panel.ColumnWidths(width, children);
            if (widths is not null)
            {
                int gap = panel.LogicalToDeviceUnits(panel.GapLogical);
                int height = 0;
                for (int i = 0; i < children.Count; i++) height = Math.Max(height, ChildHeight(children[i], widths[i]));
                needed = height;
                height = Math.Max(height, panel.ClientSize.Height);
                int x = 0;
                for (int i = 0; i < children.Count; i++)
                {
                    Control child = children[i];
                    var bounds = new Rectangle(x + child.Margin.Left, child.Margin.Top,
                        widths[i] - child.Margin.Horizontal, height - child.Margin.Vertical);
                    if (child.Bounds != bounds) child.Bounds = bounds;
                    x += widths[i] + gap;
                }
            }
            else
            {
                var heights = children.Select(c => ChildHeight(c, width)).ToArray();
                needed = heights.Sum();
                if (heights.Length > 0) heights[^1] += Math.Max(0, panel.ClientSize.Height - needed);
                int y = 0;
                for (int i = 0; i < children.Count; i++)
                {
                    Control child = children[i];
                    var bounds = new Rectangle(child.Margin.Left, y + child.Margin.Top,
                        width - child.Margin.Horizontal, heights[i] - child.Margin.Vertical);
                    if (child.Bounds != bounds) child.Bounds = bounds;
                    y += heights[i];
                }
            }
            return needed > panel.Height;
        }
    }
}
