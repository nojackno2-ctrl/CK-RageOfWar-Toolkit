namespace CKToolkit.Gui.Layout;

/// <summary>
/// 回報「真實高度」的下拉選單（ISSUE-081）。
///
/// WinForms 的 <see cref="ComboBox"/> 對外回報的偏好高度是用字型算出來的估計值，但 Windows 實際給它的
/// 高度（尤其是 DropDownList 加上視覺樣式時）在字型較大時會多出幾個像素。FlowLayoutPanel／TableLayoutPanel
/// 依估計值配置列高，下拉選單的下緣就被切掉。這裡在 handle 建立後改回報實際高度，版面配置才會正確。
/// </summary>
public class UiComboBox : ComboBox
{
    public override Size GetPreferredSize(Size proposedSize)
    {
        Size preferred = base.GetPreferredSize(proposedSize);
        if (IsHandleCreated && Height > preferred.Height) preferred.Height = Height;
        return preferred;
    }

    private bool _syncing;

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        if (_syncing) return;
        // TableLayoutPanel／FlowLayoutPanel 對非 AutoSize 的控制項用的是「最後一次指定的大小」，
        // 不是實際大小。Windows 依字型自行把下拉選單撐高時不會更新那個值，所以這裡把實際高度
        // 重新指定一次，容器才會配置正確的列高。
        _syncing = true;
        try { SetBounds(Left, Top, Width, Height, BoundsSpecified.Height); }
        finally { _syncing = false; }
        // 高度是 Windows 依字型決定的；變了就請每一層祖先重新配置。TableLayoutPanel／FlowLayoutPanel
        // 會快取偏好尺寸，只通知直接父層的話，外層仍拿舊的列高。
        Control child = this;
        for (Control? parent = Parent; parent is not null && parent is not Form; parent = parent.Parent)
        {
            parent.PerformLayout(child, "Bounds");
            child = parent;
        }
    }
}
