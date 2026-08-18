using System.Drawing;
using System.Windows.Forms;
using CKToolkit.I18n;

namespace CKToolkit.Gui;

/// <summary>
/// Phase 1 骨架視窗。完整的五分頁 GUI 介面將於 Phase 5 實作。
/// </summary>
public sealed class MainForm : Form
{
    public MainForm()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        Text = Strings.Get("Gui_Placeholder_Title");
        Size = new Size(640, 420);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Microsoft JhengHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(24)
        };
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        var lblTitle = new Label
        {
            Text = Strings.Get("AppTitle"),
            Font = new Font("Microsoft JhengHei UI", 16F, FontStyle.Bold, GraphicsUnit.Point),
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 12)
        };

        var lblDesc = new Label
        {
            Text = Strings.Get("AppDescription"),
            Font = new Font("Microsoft JhengHei UI", 10F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = Color.DimGray,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 20)
        };

        var lblMsg = new Label
        {
            Text = Strings.Get("Gui_Placeholder_Message"),
            Font = new Font("Microsoft JhengHei UI", 10F, FontStyle.Regular, GraphicsUnit.Point),
            Dock = DockStyle.Fill,
            AutoSize = true
        };

        table.Controls.Add(lblTitle, 0, 0);
        table.Controls.Add(lblDesc, 0, 1);
        table.Controls.Add(lblMsg, 0, 2);

        Controls.Add(table);
    }
}
