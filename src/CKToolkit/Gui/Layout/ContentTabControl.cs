namespace CKToolkit.Gui.Layout;

/// <summary>
/// 會回報「內容實際需要多高」的分頁控制項（ISSUE-101）。
///
/// 一般的 <see cref="TabControl"/> 對外回報的偏好尺寸跟分頁內容無關，外層只能猜一個最小高度。
/// 這裡把每一頁的根 <see cref="StackPanel"/> 在目前寬度下量一次，取最高的那頁再加上頁籤列，
/// 外層的 <see cref="StackPanel.AddGrow"/> 就能保證任何一頁都不會被切掉。
/// </summary>
public sealed class ContentTabControl : TabControl
{
    private int _lastPreferredHeight = -1;

    protected override void OnControlAdded(ControlEventArgs e)
    {
        base.OnControlAdded(e);
        // 分頁內容改變高度（換語言、提示文字變長）時，通知外層重新分配高度。
        // TabPage 本身不是 AutoSize，WinForms 不會自己把這個變化往上傳。
        if (e.Control is TabPage page)
            page.Layout += (_, _) =>
            {
                if (Parent is null || Width <= 0) return;
                int height = GetPreferredSize(new Size(Width, 0)).Height;
                if (height == _lastPreferredHeight) return;
                _lastPreferredHeight = height;
                Parent.BeginInvokeIfReady(() => Parent?.PerformLayout());
            };
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        int width = proposedSize.Width is > 0 and < int.MaxValue / 2 ? proposedSize.Width : Width;
        // 頁籤列與邊框佔掉的部分：控制項大小與 DisplayRectangle 的差。
        Rectangle display = DisplayRectangle;
        int chromeWidth = Math.Max(0, Width - display.Width);
        int chromeHeight = Math.Max(0, Height - display.Height);
        if (!IsHandleCreated || display.Height <= 0)
        {
            chromeWidth = LogicalToDeviceUnits(8);
            chromeHeight = Font.Height + LogicalToDeviceUnits(16);
        }

        int contentWidth = Math.Max(1, width - chromeWidth);
        int tallest = 0;
        foreach (TabPage page in TabPages)
        {
            foreach (Control child in page.Controls)
            {
                if (child is StackPanel stack)
                    tallest = Math.Max(tallest,
                        stack.GetPreferredSize(new Size(contentWidth - page.Padding.Horizontal, 0)).Height + page.Padding.Vertical);
            }
        }
        return new Size(width, tallest + chromeHeight);
    }
}

internal static class ControlExtensions
{
    /// <summary>handle 建好了就排到訊息佇列（避免在排版途中重入），否則直接執行。</summary>
    public static void BeginInvokeIfReady(this Control control, Action action)
    {
        if (control.IsHandleCreated && !control.IsDisposed) control.BeginInvoke(action);
        else action();
    }
}
