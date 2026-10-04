using System.Windows.Forms.Layout;

namespace CKToolkit.Gui.Layout;

/// <summary>
/// 等寬多欄的勾選清單（ISSUE-101）。
///
/// 取代「TableLayoutPanel + 固定 30px 列高」：列高改成每次排版時依該列內容在目前欄寬下的
/// 偏好高度計算，字型、DPI、語言怎麼變都不會把勾選框的字切掉。子控制項依加入順序由左而右、
/// 由上而下排列；不需要自己算欄列索引。
/// </summary>
public sealed class UniformGrid : Panel
{
    private static readonly GridEngine Engine = new();

    public UniformGrid()
    {
        AutoSize = true;
        DoubleBuffered = true;
    }

    /// <summary>欄數。</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int Columns { get; set; } = 3;

    public override LayoutEngine LayoutEngine => Engine;

    private List<Control> VisibleItems()
    {
        var list = new List<Control>();
        foreach (Control c in Controls) if (c.Visible) list.Add(c);
        return list;
    }

    private int ColumnWidth(int width) => Math.Max(1, (width - Padding.Horizontal) / Math.Max(1, Columns));

    private List<int> RowHeights(List<Control> items, int columnWidth)
    {
        var rows = new List<int>();
        for (int i = 0; i < items.Count; i++)
        {
            Control c = items[i];
            int h = c.GetPreferredSize(new Size(Math.Max(1, columnWidth - c.Margin.Horizontal), 0)).Height + c.Margin.Vertical;
            int row = i / Math.Max(1, Columns);
            if (row == rows.Count) rows.Add(h);
            else rows[row] = Math.Max(rows[row], h);
        }
        return rows;
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        int width = proposedSize.Width is > 0 and < int.MaxValue / 2 ? proposedSize.Width : Width;
        List<Control> items = VisibleItems();
        return new Size(width, RowHeights(items, ColumnWidth(width)).Sum() + Padding.Vertical);
    }

    private sealed class GridEngine : LayoutEngine
    {
        public override bool Layout(object container, LayoutEventArgs layoutEventArgs)
        {
            var grid = (UniformGrid)container;
            List<Control> items = grid.VisibleItems();
            int columnWidth = grid.ColumnWidth(grid.ClientSize.Width);
            List<int> rows = grid.RowHeights(items, columnWidth);
            int y = grid.Padding.Top;
            int columns = Math.Max(1, grid.Columns);
            for (int i = 0; i < items.Count; i++)
            {
                int row = i / columns, column = i % columns;
                if (column == 0 && row > 0) y += rows[row - 1];
                Control c = items[i];
                var bounds = new Rectangle(
                    grid.Padding.Left + column * columnWidth + c.Margin.Left,
                    y + c.Margin.Top,
                    columnWidth - c.Margin.Horizontal,
                    rows[row] - c.Margin.Vertical);
                if (c.Bounds != bounds) c.Bounds = bounds;
            }
            return rows.Sum() + grid.Padding.Vertical != grid.Height;
        }
    }
}
