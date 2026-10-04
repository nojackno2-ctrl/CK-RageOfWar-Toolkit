using System.Runtime.CompilerServices;

namespace CKToolkit.Gui.Layout;

/// <summary>
/// DataGridView 的欄寬與列高 DPI 換算（ISSUE-081）。
///
/// WinForms 的表單自動縮放<b>不會</b>換算 DataGridView 的欄寬與列高（實測：150% 螢幕上
/// 100px 的欄寬縮放後仍是 100px），寫死的 <c>RowTemplate.Height = 30</c> 在 200% 螢幕上
/// 連一行字都放不下。這裡把呼叫當下的欄寬記成邏輯像素，每次 handle 建立、DPI 或字型改變時
/// 以「指定」而非「乘上倍率」的方式重新換算，重複觸發也不會越放越大。
/// 列高則直接由字型高度推出。
/// </summary>
internal static class GridMetrics
{
    private sealed class Logical
    {
        public required Dictionary<DataGridViewColumn, (int Width, int MinimumWidth)> Columns { get; init; }
    }

    private static readonly ConditionalWeakTable<DataGridView, Logical> Registered = new();

    /// <summary>欄位全部加入之後呼叫一次；之後新增的欄位不受管理。</summary>
    public static void Apply(DataGridView grid)
    {
        var logical = new Logical { Columns = new() };
        foreach (DataGridViewColumn column in grid.Columns)
            logical.Columns[column] = (column.Width, column.MinimumWidth);
        Registered.AddOrUpdate(grid, logical);

        grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
        grid.AllowUserToResizeRows = false;
        grid.HandleCreated += (_, _) => Sync(grid);
        grid.FontChanged += (_, _) => Sync(grid);
        grid.DpiChangedAfterParent += (_, _) => Sync(grid);
        grid.RowsAdded += (_, e) => SyncRows(grid, e.RowIndex, e.RowCount);
        Sync(grid);
    }

    private static int RowHeight(DataGridView grid)
    {
        int text = grid.Font.Height;
        foreach (DataGridViewColumn column in grid.Columns)
            if (column.DefaultCellStyle.Font is Font font) text = Math.Max(text, font.Height);
        return text + grid.LogicalToDeviceUnits(12);
    }

    private static void Sync(DataGridView grid)
    {
        if (!Registered.TryGetValue(grid, out Logical? logical)) return;
        foreach ((DataGridViewColumn column, (int width, int minimum)) in logical.Columns)
        {
            if (column.DataGridView != grid) continue;
            int min = Math.Max(2, grid.LogicalToDeviceUnits(Math.Max(2, minimum)));
            if (column.MinimumWidth != min) column.MinimumWidth = min;
            if (column.AutoSizeMode != DataGridViewAutoSizeColumnMode.Fill)
            {
                int scaled = Math.Max(min, grid.LogicalToDeviceUnits(width));
                if (column.Width != scaled) column.Width = scaled;
            }
        }
        grid.RowTemplate.Height = RowHeight(grid);
        SyncRows(grid, 0, grid.Rows.Count);
    }

    private static void SyncRows(DataGridView grid, int start, int count)
    {
        int height = RowHeight(grid);
        for (int i = start; i < start + count && i < grid.Rows.Count; i++)
        {
            DataGridViewRow row = grid.Rows[i];
            if (!row.IsNewRow && row.Height != height) row.Height = height;
        }
    }
}
