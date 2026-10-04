using System.Diagnostics;
using System.Reflection;
using CKToolkit.Core.Common;
using CKToolkit.Core.Saves;
using CKToolkit.Core.Trainer;
using CKToolkit.Gui;
using CKToolkit.Gui.Layout;
using CKToolkit.I18n;

namespace CKToolkit.SelfTest;

/// <summary>
/// 第 49 組：GUI 版面稽核（ISSUE-101）。
///
/// 「換一台電腦介面就亂掉」不能再靠人眼在開發機上看過就算數。這裡把每一個分頁與對話框，
/// 在三種介面語言、多種視窗大小、多種字型放大倍率下實際排版，然後機械式地檢查四件事：
/// <list type="number">
///   <item><b>不裁切</b>：每個可見控制項都完整落在父容器的工作區內；捲動容器內的內容必須捲得到。</item>
///   <item><b>文字完整</b>：標籤在目前寬度下需要的高度不超過實際高度；核取方塊、單選鈕、按鈕的文字放得下。</item>
///   <item><b>不重疊</b>：同一個版面容器裡的兄弟控制項互不重疊。</item>
///   <item><b>表格可讀</b>：DataGridView 的列高、標題列高至少容得下一行字。</item>
/// </list>
///
/// DPI 只能在建立第一個視窗之前設定，所以稽核在子行程中執行：一次以 96 DPI（DpiUnaware），
/// 一次以這台電腦實際的 DPI（PerMonitorV2）。字型放大倍率另外模擬 125%～200% 的「字變大」情境，
/// 而且邊距不跟著放大，比真實情況更嚴苛。
/// </summary>
internal static class LayoutAudit
{
    public const string ChildSwitch = "--layout-audit";

    private static readonly string[] Languages = ["zh-TW", "zh-CN", "en"];
    private static readonly float[] TextScales = [1f, 1.25f, 1.5f, 2f];

    /// <summary>父行程：啟動兩個子行程並彙整結果。</summary>
    public static IReadOnlyList<(string Mode, int ExitCode, string Output)> RunChildren()
    {
        var results = new List<(string, int, string)>();
        string exe = Environment.ProcessPath ?? throw new InvalidOperationException("ProcessPath unavailable");
        foreach (string mode in new[] { "unaware", "permonitor" })
        foreach (float scale in TextScales)
        {
            var info = new ProcessStartInfo(exe)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
                StandardErrorEncoding = System.Text.Encoding.UTF8
            };
            info.ArgumentList.Add(ChildSwitch);
            info.ArgumentList.Add(mode);
            info.ArgumentList.Add(scale.ToString(System.Globalization.CultureInfo.InvariantCulture));
            using var process = Process.Start(info)!;
            Task<string> stdout = process.StandardOutput.ReadToEndAsync();
            Task<string> stderr = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(600_000))
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                results.Add(($"{mode} x{scale}", -1, "timeout"));
                continue;
            }
            results.Add(($"{mode} x{scale}", process.ExitCode, stdout.Result + stderr.Result));
        }
        return results;
    }

    /// <summary>子行程進入點。回傳違規數量（0 = 全部通過）。</summary>
    public static int RunChild(string mode, float scale)
    {
        // 字型倍率必須在建立任何控制項之前決定（含應用程式預設字型），
        // 才是在模擬「這台電腦的字比較大」，而不是「字型在執行中途被換掉」。
        Ui.TextScaleForTesting = scale;
        Application.SetHighDpiMode(mode == "unaware" ? HighDpiMode.DpiUnaware : HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetDefaultFont(Ui.UiFont());

        var violations = new List<string>();
        int checkedControls = 0;
        int dpi = 96;

        foreach (string language in Languages)
        {
            Strings.Language = language;
            AuditMainForm(language, scale, violations, ref checkedControls, ref dpi);
            AuditDialogs(language, scale, violations, ref checkedControls);
        }

        Console.WriteLine($"dpi={dpi} textScale={scale} controls={checkedControls} violations={violations.Count}");
        if (Environment.GetEnvironmentVariable("CK_LAYOUT_AUDIT_LOG") is { Length: > 0 } log)
            File.WriteAllLines(log, violations.Distinct());
        foreach (string violation in violations.Distinct().Take(60))
            Console.WriteLine("  ! " + violation);
        return Math.Min(violations.Count, 100);
    }

    private static void AuditMainForm(string language, float scale, List<string> violations, ref int count, ref int dpi)
    {
        using var form = new MainForm { ShowInTaskbar = false, Opacity = 0, StartPosition = FormStartPosition.Manual };
        typeof(MainForm).GetMethod("ApplyLanguage", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(form, null);
        form.Show();
        dpi = form.DeviceDpi;
        var tabs = (TabControl)typeof(MainForm).GetField("_tabs", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(form)!;

        Rectangle area = Screen.FromControl(form).WorkingArea;
        var sizes = new List<Size>
        {
            form.MinimumSize,                                            // 最小視窗
            new(Math.Min(area.Width, form.LogicalToDeviceUnits(1100)),   // 預設大小
                Math.Min(area.Height, form.LogicalToDeviceUnits(800))),
            area.Size                                                    // 最大化
        };

        foreach (Size size in sizes)
        {
            form.Size = size;
            for (int t = 0; t < tabs.TabCount; t++)
            {
                tabs.SelectedIndex = t;
                Settle(form);
                string context = $"[{language} x{scale} {form.Width}x{form.Height} tab{t}:{tabs.TabPages[t].Text}]";
                Audit(form, context, violations, ref count);

                // 修改器的子分頁也要逐頁檢查。
                foreach (TabControl inner in Descendants(tabs.TabPages[t]).OfType<TabControl>())
                {
                    for (int i = 0; i < inner.TabCount; i++)
                    {
                        inner.SelectedIndex = i;
                        Settle(form);
                        Audit(form, $"{context} sub{i}", violations, ref count);
                    }
                    inner.SelectedIndex = 0;
                }
            }
        }
        form.Hide();
    }

    private static void AuditDialogs(string language, float scale, List<string> violations, ref int count)
    {
        var dialogs = new List<(string Name, Func<Form> Create)>
        {
            ("stats", () => new PlayerStatisticsDialog(new PlayerStatisticsSummary(
                "p", 3, 2, 1, 50, 1, 0, 0, 3_600_000, 1, 120, 0, 60, "Mule",
                1000, 2000, 30, 10, 0, "Mule", 5, 120, "player.ini"))),
            ("panel", () => new InGamePanelForm(new TrainerConfig { Enabled = true }))
        };
        foreach (Cheat cheat in Cheats.All.Where(c => c.Parameters.Any(p => !p.Hidden)))
            dialogs.Add(($"cheat:{cheat.Id}", () => new CheatParamsDialog(cheat, null)));

        foreach ((string name, Func<Form> create) in dialogs)
        {
            using Form dialog = create();
            dialog.ShowInTaskbar = false;
            dialog.Opacity = 0;
            dialog.StartPosition = FormStartPosition.Manual;
            dialog.Show();
            foreach (Size size in new[] { dialog.MinimumSize, dialog.Size })
            {
                if (size.Width <= 0 || size.Height <= 0) continue;
                dialog.Size = size;
                Settle(dialog);
                Audit(dialog, $"[{language} x{scale} {name} {dialog.Width}x{dialog.Height}]", violations, ref count);
            }
            dialog.Hide();
        }
    }

    private static void Settle(Control root)
    {
        // 版面異動有些是 BeginInvoke 排進佇列的（分頁內容高度改變後通知外層），處理掉再量。
        for (int i = 0; i < 3; i++)
        {
            foreach (Control c in Descendants(root).Prepend(root)) c.PerformLayout();
            Application.DoEvents();
        }
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            if (IsLeaf(child)) continue;
            foreach (Control d in Descendants(child)) yield return d;
        }
    }

    /// <summary>這些控制項內部的子控制項（捲軸、編輯框）由它們自己管理，不往下檢查。</summary>
    private static bool IsLeaf(Control c) =>
        c is DataGridView or NumericUpDown or ComboBox or TextBoxBase or ScrollBar or PictureBox or ListBox;

    private static void Audit(Control root, string context, List<string> violations, ref int count)
    {
        foreach (Control c in Descendants(root))
        {
            if (!c.Visible || c.Parent is null) continue;
            count++;
            string who = $"{context} {Describe(c)}";
            Control parent = c.Parent;

            // 1. 不裁切。
            if (parent is ScrollableControl { AutoScroll: true } scroller && parent is not TabPage)
            {
                Size extent = new(Math.Max(scroller.ClientSize.Width, scroller.AutoScrollMinSize.Width),
                                  Math.Max(scroller.ClientSize.Height, scroller.AutoScrollMinSize.Height));
                Point origin = scroller.AutoScrollPosition;
                var reachable = new Rectangle(origin, extent);
                if (!Contains(reachable, c.Bounds))
                    violations.Add($"{who} 捲動範圍 {reachable} 不足以顯示 {c.Bounds}");
            }
            else if (parent is not TabControl && !Contains(parent.ClientRectangle, c.Bounds))
            {
                violations.Add($"{who} 超出父容器 {Describe(parent)} 工作區 {parent.ClientRectangle}：{c.Bounds}（偏好 {c.GetPreferredSize(Size.Empty)}，handle={c.IsHandleCreated}）");
            }

            // 2. 文字完整。
            switch (c)
            {
                case Label label when label.AutoSize && label.Text.Length > 0 && label.Width > 0:
                {
                    int need = label.GetPreferredSize(new Size(label.Width, 0)).Height;
                    if (need > label.Height + 1)
                        violations.Add($"{who} 文字需要 {need}px 高，只有 {label.Height}px");
                    break;
                }
                case ButtonBase { AutoEllipsis: true } ellipsis when ellipsis.Text.Length > 0:
                {
                    // 刻意以省略號收尾（完整文字在提示裡），只要求高度放得下一行字。
                    int textHeight = TextRenderer.MeasureText("Ag", ellipsis.Font).Height;
                    if (ellipsis.Height < textHeight)
                        violations.Add($"{who} 高 {ellipsis.Height}px，放不下 {textHeight}px 的文字");
                    break;
                }
                case CheckBox or RadioButton when c.Text.Length > 0:
                {
                    Size need = c.GetPreferredSize(Size.Empty);
                    if (need.Width > c.Width + 1 || need.Height > c.Height + 1)
                        violations.Add($"{who} 選項文字需要 {need}，只有 {c.Size}");
                    break;
                }
                case Button button when button.Text.Length > 0:
                {
                    int textHeight = TextRenderer.MeasureText(button.Text, button.Font).Height;
                    if (button.Height < textHeight)
                        violations.Add($"{who} 按鈕高 {button.Height}px，放不下 {textHeight}px 的文字");
                    if (button.AutoSize && button.GetPreferredSize(Size.Empty).Width > button.Width + 1)
                        violations.Add($"{who} 按鈕文字需要 {button.GetPreferredSize(Size.Empty).Width}px 寬，只有 {button.Width}px");
                    break;
                }
                case DataGridView grid:
                {
                    int text = grid.Font.Height;
                    if (grid.ColumnHeadersVisible && grid.ColumnHeadersHeight < text)
                        violations.Add($"{who} 標題列高 {grid.ColumnHeadersHeight}px < 字高 {text}px");
                    if (grid.Rows.Count > 0 && grid.Rows[0].Height < text + 2)
                        violations.Add($"{who} 列高 {grid.Rows[0].Height}px < 字高 {text}px");
                    break;
                }
            }
        }

        // 3. 不重疊（版面容器裡的兄弟控制項）。
        foreach (Control container in Descendants(root).Prepend(root))
        {
            if (container is TabControl || IsLeaf(container)) continue;
            var visible = container.Controls.Cast<Control>().Where(x => x.Visible && x.Width > 0 && x.Height > 0).ToList();
            for (int i = 0; i < visible.Count; i++)
                for (int j = i + 1; j < visible.Count; j++)
                {
                    Rectangle overlap = Rectangle.Intersect(visible[i].Bounds, visible[j].Bounds);
                    if (overlap.Width > 1 && overlap.Height > 1)
                        violations.Add($"{context} {Describe(visible[i])} 與 {Describe(visible[j])} 重疊 {overlap}");
                }
        }
    }

    private static bool Contains(Rectangle outer, Rectangle inner) =>
        inner.Left >= outer.Left - 1 && inner.Top >= outer.Top - 1 &&
        inner.Right <= outer.Right + 1 && inner.Bottom <= outer.Bottom + 1;

    private static string Describe(Control c)
    {
        string text = c.Text.Replace('\n', ' ').Replace('\r', ' ');
        if (text.Length > 24) text = text[..24] + "…";
        return text.Length > 0 ? $"{c.GetType().Name}「{text}」" : c.GetType().Name;
    }
}
