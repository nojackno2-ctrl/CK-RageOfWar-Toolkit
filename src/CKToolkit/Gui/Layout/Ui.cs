namespace CKToolkit.Gui.Layout;

/// <summary>
/// 跨電腦一致的版面規則集中在這裡（ISSUE-081）。
///
/// 「換一台電腦就亂掉」的根因有三個，這裡逐一封死：
/// <list type="number">
///   <item><b>DPI 自動縮放從來沒有生效。</b>表單只設了 <c>AutoScaleMode = Dpi</c> 卻沒有
///         <c>AutoScaleDimensions</c>，WinForms 算出的縮放倍率永遠是 1，所有寫死的像素（邊距、按鈕最小尺寸、
///         視窗大小）在 150%／200% 的螢幕上都沒換算，字卻變大了。<see cref="BeginForm"/>／<see cref="EndForm"/>
///         以設計工具產生程式碼的順序設定，所有數字都以 96 DPI 的邏輯像素撰寫。</item>
///   <item><b>高度是猜的。</b>百分比列高、寫死的 <c>MinimumSize</c>，在字型比較大的螢幕上一定不夠。
///         現在高度一律由 <see cref="StackPanel"/> 量出來。</item>
///   <item><b>字型不一定存在。</b>英文版 Windows 可能沒有「Microsoft JhengHei UI」，GDI+ 會悄悄換成
///         字距完全不同的字型。<see cref="UiFontFamily"/> 依序找可用的字型。</item>
/// </list>
/// </summary>
internal static class Ui
{
    public static readonly Color TextPrimary = Color.FromArgb(15, 23, 42);
    public static readonly Color TextSecondary = Color.FromArgb(71, 85, 105);
    public static readonly Color TextMuted = Color.FromArgb(100, 116, 139);
    public static readonly Color Warning = Color.FromArgb(180, 83, 9);
    public static readonly Color Accent = Color.FromArgb(37, 99, 235);
    public static readonly Color Danger = Color.FromArgb(220, 38, 38);
    public static readonly Color Surface = Color.FromArgb(248, 250, 252);
    public static readonly Color Border = Color.FromArgb(203, 213, 225);

    private static string? _family;

    /// <summary>介面字型：依序挑第一個這台電腦上真的有的字型。</summary>
    public static string UiFontFamily => _family ??= PickFamily();

    private static string PickFamily()
    {
        string[] candidates = ["Microsoft JhengHei UI", "Microsoft JhengHei", "Microsoft YaHei UI", "Segoe UI"];
        using var installed = new System.Drawing.Text.InstalledFontCollection();
        var names = new HashSet<string>(installed.Families.Select(f => f.Name), StringComparer.OrdinalIgnoreCase);
        foreach (string candidate in candidates)
            if (names.Contains(candidate)) return candidate;
        return SystemFonts.MessageBoxFont?.FontFamily.Name ?? FontFamily.GenericSansSerif.Name;
    }

    /// <summary>
    /// 只給 SelfTest 的版面稽核用：把所有介面字型放大，模擬「字比開發機大」的電腦
    /// （高 DPI、系統字型不同、英文字較寬）。邊距不跟著放大，所以比真實情況更嚴苛。
    /// </summary>
    internal static float TextScaleForTesting { get; set; } = 1f;

    /// <summary>以點數建立介面字型（點數會由系統依 DPI 換算，不需要自己乘倍率）。</summary>
    public static Font UiFont(float points = 9f, FontStyle style = FontStyle.Regular) =>
        new(UiFontFamily, points * TextScaleForTesting, style);

    /// <summary>等寬字型：記錄區、報告框用。</summary>
    public static Font MonoFont(float points = 8.5f)
    {
        points *= TextScaleForTesting;
        foreach (string family in new[] { "Cascadia Mono", "Consolas", "Courier New" })
        {
            using var probe = new Font(family, points);
            if (string.Equals(probe.Name, family, StringComparison.OrdinalIgnoreCase)) return new Font(family, points);
        }
        return new Font(FontFamily.GenericMonospace, points);
    }

    /// <summary>
    /// 表單建構的開頭呼叫。之後設定的所有尺寸都是 96 DPI 的邏輯像素。
    /// </summary>
    public static void BeginForm(Form form)
    {
        form.SuspendLayout();
        form.Font = UiFont();
    }

    /// <summary>
    /// 表單建構的結尾呼叫：控制項全部加進去之後才設定縮放基準，WinForms 才會在第一次排版時
    /// 把整棵控制項樹（邊距、最小尺寸、視窗大小）一起換算到實際 DPI。
    /// 先設定再加控制項的話，縮放會在空表單上就做完，後加的控制項永遠停留在 96 DPI 的數字。
    /// </summary>
    public static void EndForm(Form form)
    {
        form.AutoScaleDimensions = new SizeF(96F, 96F);
        form.AutoScaleMode = AutoScaleMode.Dpi;
        form.ResumeLayout(false);
        form.PerformLayout();
    }

    /// <summary>
    /// 讓視窗不超出它所在螢幕的工作區（小筆電、高縮放比例）。
    /// 在 <see cref="Form.Load"/> 裡呼叫，此時尺寸已經換算成實際像素。
    /// 置中的視窗重新置中，其餘只往內推到螢幕範圍內。
    /// </summary>
    public static void FitToScreen(Form form)
    {
        Screen screen = form.Owner is not null ? Screen.FromControl(form.Owner) : Screen.FromControl(form);
        Rectangle area = screen.WorkingArea;
        if (form.MinimumSize.Width > area.Width || form.MinimumSize.Height > area.Height)
            form.MinimumSize = new Size(Math.Min(form.MinimumSize.Width, area.Width), Math.Min(form.MinimumSize.Height, area.Height));
        int width = Math.Min(form.Width, area.Width);
        int height = Math.Min(form.Height, area.Height);
        int x, y;
        if (form.StartPosition == FormStartPosition.CenterScreen)
        {
            x = area.Left + (area.Width - width) / 2;
            y = area.Top + (area.Height - height) / 2;
        }
        else if (form.StartPosition == FormStartPosition.CenterParent && form.Owner is not null)
        {
            Rectangle owner = form.Owner.Bounds;
            x = owner.Left + (owner.Width - width) / 2;
            y = owner.Top + (owner.Height - height) / 2;
        }
        else
        {
            x = form.Left;
            y = form.Top;
        }
        x = Math.Clamp(x, area.Left, area.Right - width);
        y = Math.Clamp(y, area.Top, area.Bottom - height);
        form.Bounds = new Rectangle(x, y, width, height);
    }

    /// <summary>會依容器寬度自動換行的文字。</summary>
    public static Label Text(Label? label = null, Color? color = null, float? points = null, FontStyle style = FontStyle.Regular)
    {
        label ??= new Label();
        label.AutoSize = true;
        label.UseMnemonic = false;
        label.ForeColor = color ?? TextSecondary;
        if (points is not null || style != FontStyle.Regular) label.Font = UiFont(points ?? 9f, style);
        label.Margin = new Padding(0, 0, 0, 6);
        return label;
    }

    /// <summary>有底色的提示橫幅。</summary>
    public static Label Banner(Label label, Color back, Color fore, bool bold = false)
    {
        Text(label, fore, style: bold ? FontStyle.Bold : FontStyle.Regular);
        label.BackColor = back;
        label.Padding = new Padding(10, 8, 10, 8);
        label.Margin = new Padding(0, 0, 0, 10);
        return label;
    }

    /// <summary>勾選框／單選鈕底下那行縮排說明。</summary>
    public static Label Description(Label label, Color? color = null)
    {
        Text(label, color ?? TextMuted);
        label.Margin = new Padding(24, 0, 0, 10);
        return label;
    }

    public static T Option<T>(T box, bool bold = false) where T : ButtonBase
    {
        box.AutoSize = true;
        box.UseMnemonic = false;
        if (bold) box.Font = UiFont(9f, FontStyle.Bold);
        box.Margin = new Padding(0, 4, 0, 2);
        return box;
    }

    /// <summary>
    /// 標籤＋輸入框的欄位表：第 0 欄標籤取自然寬度，最後一欄吃掉剩餘寬度。
    /// 中間欄（若有）取自然寬度。
    /// </summary>
    public static TableLayoutPanel FieldTable(int columns = 2)
    {
        var table = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = columns,
            Margin = new Padding(0, 0, 0, 6),
            Padding = Padding.Empty,
            BackColor = Color.Transparent
        };
        for (int i = 0; i < columns; i++)
            table.ColumnStyles.Add(i == columns - 1
                ? new ColumnStyle(SizeType.Percent, 100F)
                : new ColumnStyle(SizeType.AutoSize));
        return table;
    }

    /// <summary>在欄位表加一列，回傳列索引。</summary>
    public static int AddRow(TableLayoutPanel table, params Control?[] cells)
    {
        int row = table.RowCount;
        table.RowCount = row + 1;
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        int column = 0;
        foreach (Control? cell in cells)
        {
            if (cell is not null)
            {
                if (cell is Label label)
                {
                    label.AutoSize = true;
                    label.Anchor = AnchorStyles.Left | AnchorStyles.Right;
                    if (label.Margin == new Padding(3) || label.Margin == Padding.Empty)
                        label.Margin = new Padding(0, 3, 10, 3);
                }
                else if (cell is ComboBox or TextBox)
                {
                    cell.Anchor = AnchorStyles.Left | AnchorStyles.Right;
                }
                else if (cell is NumericUpDown)
                {
                    cell.Anchor = AnchorStyles.Left;
                }
                table.Controls.Add(cell, column, row);
            }
            column++;
        }
        return row;
    }

    /// <summary>一排會自動換行的按鈕。</summary>
    public static FlowLayoutPanel ButtonRow(params Control[] controls)
    {
        var row = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = true,
            FlowDirection = FlowDirection.LeftToRight,
            Margin = new Padding(0, 4, 0, 6),
            Padding = Padding.Empty,
            BackColor = Color.Transparent
        };
        row.Controls.AddRange(controls);
        return row;
    }

    /// <summary>扁平按鈕。最小尺寸以邏輯像素給定，交由表單縮放一起換算。</summary>
    public static Button Button(Button button, Color back, Color fore, bool bold = false, int minWidth = 96)
    {
        button.AutoSize = true;
        button.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        button.UseMnemonic = false;
        button.MinimumSize = new Size(minWidth, 32);
        button.Padding = new Padding(8, 2, 8, 2);
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderColor = fore == Color.White ? back : Border;
        button.BackColor = back;
        button.ForeColor = fore;
        if (bold) button.Font = UiFont(9f, FontStyle.Bold);
        button.Margin = new Padding(0, 0, 8, 6);
        button.Cursor = Cursors.Hand;
        return button;
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ComboBox, object> FittedCombos = new();

    /// <summary>
    /// 下拉選單的寬度改由「最長的選項」決定：任何語言、任何 DPI 都剛好放得下，不會被截斷。
    /// 寬度在每次 handle 建立、字型／DPI 改變、選項切換時重新指定（而不是乘倍率），
    /// 所以表單的自動縮放也不會把它放大兩次。
    /// 只用在自然寬度的下拉選單上；被表格撐滿的下拉選單不需要。
    /// </summary>
    public static void FitComboToItems(ComboBox combo, int maximumLogicalWidth = 420)
    {
        if (FittedCombos.TryGetValue(combo, out _)) { FitCombo(combo, maximumLogicalWidth); return; }
        FittedCombos.Add(combo, new object());
        EventHandler refit = (_, _) => FitCombo(combo, maximumLogicalWidth);
        combo.HandleCreated += refit;
        combo.FontChanged += refit;
        combo.DpiChangedAfterParent += refit;
        combo.SelectedIndexChanged += refit;
        combo.SizeChanged += refit;
        FitCombo(combo, maximumLogicalWidth);
    }

    private static void FitCombo(ComboBox combo, int maximumLogicalWidth)
    {
        int widest = 0;
        foreach (object? item in combo.Items)
        {
            string text = combo.GetItemText(item) ?? string.Empty;
            widest = Math.Max(widest, TextRenderer.MeasureText(text, combo.Font, Size.Empty, TextFormatFlags.NoPrefix).Width);
        }
        if (!string.IsNullOrEmpty(combo.Text))
            widest = Math.Max(widest, TextRenderer.MeasureText(combo.Text, combo.Font, Size.Empty, TextFormatFlags.NoPrefix).Width);
        int arrow = SystemInformation.GetVerticalScrollBarWidthForDpi(combo.DeviceDpi);
        int desired = widest + arrow + combo.LogicalToDeviceUnits(14);
        desired = Math.Max(desired, combo.LogicalToDeviceUnits(60));
        desired = Math.Min(desired, combo.LogicalToDeviceUnits(maximumLogicalWidth));
        if (combo.Width != desired) combo.Width = desired;
        int dropDown = Math.Max(desired, widest + arrow + combo.LogicalToDeviceUnits(14));
        if (combo.DropDownWidth != dropDown) combo.DropDownWidth = dropDown;
    }
}
