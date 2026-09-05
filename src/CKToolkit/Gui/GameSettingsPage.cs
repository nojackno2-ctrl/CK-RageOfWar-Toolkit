using CKToolkit.Core.Common;
using CKToolkit.I18n;

namespace CKToolkit.Gui;

/// <summary>
/// 遊戲設定與規則調整分頁 (Game Settings Page)。
/// 用於自訂遊戲的核心機制、兵種特性與編隊規則。
/// </summary>
public sealed class GameSettingsPage : UserControl
{
    private readonly Label _title = new();
    private readonly Label _subtitle = new();
    private readonly Label _groupHeroArmy = new();
    private readonly CheckBox _allowVikingLordHeroArmy = new();
    private readonly Label _vikingDesc = new();
    private readonly CheckBox _allowLiberatiHeroArmy = new();
    private readonly Label _liberatiDesc = new();
    private readonly CheckBox _allowMuleHeroArmy = new();
    private readonly Label _muleDesc = new();

    private readonly Label _groupLogistics = new();
    private readonly CheckBox _wagonCapacity10k = new();
    private readonly Label _wagonCapacityDesc = new();

    private readonly Button _resetBtn = new();
    private bool _loading;

    public event Action? SettingsChanged;

    public GameSettingsPage()
    {
        AutoScroll = true;
        BackColor = Color.White;
        Padding = new Padding(24);
        BuildUi();
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 1,
            RowCount = 4,
            BackColor = Color.White
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        // 1. 頁面頂部標題與說明
        var header = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Margin = new Padding(0, 0, 0, 16)
        };
        _title.AutoSize = true;
        _title.Font = new Font(Font.FontFamily, 13F, FontStyle.Bold);
        _title.ForeColor = Color.FromArgb(15, 23, 42);
        _subtitle.AutoSize = true;
        _subtitle.Font = new Font(Font.FontFamily, 9F);
        _subtitle.ForeColor = Color.FromArgb(71, 85, 105);
        _subtitle.Margin = new Padding(0, 4, 0, 0);
        header.Controls.Add(_title);
        header.Controls.Add(_subtitle);
        root.Controls.Add(header, 0, 0);

        // 2. 英雄編隊規則卡片
        var card = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 1,
            RowCount = 7,
            Padding = new Padding(18),
            BackColor = Color.FromArgb(248, 250, 252),
            Margin = new Padding(0, 0, 0, 16)
        };
        card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        card.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _groupHeroArmy.AutoSize = true;
        _groupHeroArmy.Font = new Font(Font.FontFamily, 11F, FontStyle.Bold);
        _groupHeroArmy.ForeColor = Color.FromArgb(30, 41, 59);
        _groupHeroArmy.Margin = new Padding(0, 0, 0, 12);
        card.Controls.Add(_groupHeroArmy, 0, 0);

        // 維京領主
        _allowVikingLordHeroArmy.AutoSize = true;
        _allowVikingLordHeroArmy.Font = new Font(Font.FontFamily, 9.5F, FontStyle.Bold);
        _allowVikingLordHeroArmy.ForeColor = Color.FromArgb(15, 23, 42);
        _allowVikingLordHeroArmy.Margin = new Padding(0, 4, 0, 2);
        _allowVikingLordHeroArmy.CheckedChanged += (_, _) => OnSettingChanged();
        card.Controls.Add(_allowVikingLordHeroArmy, 0, 1);

        _vikingDesc.AutoSize = true;
        _vikingDesc.Font = new Font(Font.FontFamily, 8.5F);
        _vikingDesc.ForeColor = Color.FromArgb(100, 116, 139);
        _vikingDesc.Margin = new Padding(22, 0, 0, 14);
        card.Controls.Add(_vikingDesc, 0, 2);

        // 自由鬥士
        _allowLiberatiHeroArmy.AutoSize = true;
        _allowLiberatiHeroArmy.Font = new Font(Font.FontFamily, 9.5F, FontStyle.Bold);
        _allowLiberatiHeroArmy.ForeColor = Color.FromArgb(15, 23, 42);
        _allowLiberatiHeroArmy.Margin = new Padding(0, 4, 0, 2);
        _allowLiberatiHeroArmy.CheckedChanged += (_, _) => OnSettingChanged();
        card.Controls.Add(_allowLiberatiHeroArmy, 0, 3);

        _liberatiDesc.AutoSize = true;
        _liberatiDesc.Font = new Font(Font.FontFamily, 8.5F);
        _liberatiDesc.ForeColor = Color.FromArgb(100, 116, 139);
        _liberatiDesc.Margin = new Padding(22, 0, 0, 14);
        card.Controls.Add(_liberatiDesc, 0, 4);

        // 運糧馬／騾子
        _allowMuleHeroArmy.AutoSize = true;
        _allowMuleHeroArmy.Font = new Font(Font.FontFamily, 9.5F, FontStyle.Bold);
        _allowMuleHeroArmy.ForeColor = Color.FromArgb(15, 23, 42);
        _allowMuleHeroArmy.Margin = new Padding(0, 4, 0, 2);
        _allowMuleHeroArmy.CheckedChanged += (_, _) => OnSettingChanged();
        card.Controls.Add(_allowMuleHeroArmy, 0, 5);

        _muleDesc.AutoSize = true;
        _muleDesc.Font = new Font(Font.FontFamily, 8.5F);
        _muleDesc.ForeColor = Color.FromArgb(100, 116, 139);
        _muleDesc.Margin = new Padding(22, 0, 0, 8);
        card.Controls.Add(_muleDesc, 0, 6);

        root.Controls.Add(card, 0, 1);

        // 3. 經濟與運輸規則卡片
        var cardLogistics = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(18),
            BackColor = Color.FromArgb(248, 250, 252),
            Margin = new Padding(0, 0, 0, 16)
        };
        cardLogistics.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        cardLogistics.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        cardLogistics.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _groupLogistics.AutoSize = true;
        _groupLogistics.Font = new Font(Font.FontFamily, 11F, FontStyle.Bold);
        _groupLogistics.ForeColor = Color.FromArgb(30, 41, 59);
        _groupLogistics.Margin = new Padding(0, 0, 0, 12);
        cardLogistics.Controls.Add(_groupLogistics, 0, 0);

        _wagonCapacity10k.AutoSize = true;
        _wagonCapacity10k.Font = new Font(Font.FontFamily, 9.5F, FontStyle.Bold);
        _wagonCapacity10k.ForeColor = Color.FromArgb(15, 23, 42);
        _wagonCapacity10k.Margin = new Padding(0, 4, 0, 2);
        _wagonCapacity10k.CheckedChanged += (_, _) => OnSettingChanged();
        cardLogistics.Controls.Add(_wagonCapacity10k, 0, 1);

        _wagonCapacityDesc.AutoSize = true;
        _wagonCapacityDesc.Font = new Font(Font.FontFamily, 8.5F);
        _wagonCapacityDesc.ForeColor = Color.FromArgb(100, 116, 139);
        _wagonCapacityDesc.Margin = new Padding(22, 0, 0, 8);
        cardLogistics.Controls.Add(_wagonCapacityDesc, 0, 2);

        root.Controls.Add(cardLogistics, 0, 2);

        // 4. 底部動作列（還原預設值）
        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0, 4, 0, 0)
        };
        _resetBtn.AutoSize = true;
        _resetBtn.Padding = new Padding(10, 5, 10, 5);
        _resetBtn.BackColor = Color.White;
        _resetBtn.ForeColor = Color.FromArgb(71, 85, 105);
        _resetBtn.FlatStyle = FlatStyle.Flat;
        _resetBtn.FlatAppearance.BorderColor = Color.FromArgb(203, 213, 225);
        _resetBtn.Click += (_, _) => ResetToDefaults();
        actions.Controls.Add(_resetBtn);
        root.Controls.Add(actions, 0, 3);

        Controls.Add(root);
    }

    private void OnSettingChanged()
    {
        if (_loading) return;
        SettingsChanged?.Invoke();
    }

    private void ResetToDefaults()
    {
        _loading = true;
        _allowVikingLordHeroArmy.Checked = false;
        _allowLiberatiHeroArmy.Checked = false;
        _allowMuleHeroArmy.Checked = false;
        _wagonCapacity10k.Checked = false;
        _loading = false;
        SettingsChanged?.Invoke();
    }

    public void LoadConfig(GameSettingsConfig config)
    {
        _loading = true;
        _allowVikingLordHeroArmy.Checked = config.AllowVikingLordHeroArmy;
        _allowLiberatiHeroArmy.Checked = config.AllowLiberatiHeroArmy;
        _allowMuleHeroArmy.Checked = config.AllowMuleHeroArmy;
        _wagonCapacity10k.Checked = config.WagonCapacity10k;
        _loading = false;
    }

    public void SaveConfig(GameSettingsConfig config)
    {
        config.AllowVikingLordHeroArmy = _allowVikingLordHeroArmy.Checked;
        config.AllowLiberatiHeroArmy = _allowLiberatiHeroArmy.Checked;
        config.AllowMuleHeroArmy = _allowMuleHeroArmy.Checked;
        config.WagonCapacity10k = _wagonCapacity10k.Checked;
    }

    public void ApplyLanguage()
    {
        _title.Text = Strings.Get("GameSettings_Title");
        _subtitle.Text = Strings.Get("GameSettings_Subtitle");
        _groupHeroArmy.Text = Strings.Get("GameSettings_Group_HeroArmy");
        _allowVikingLordHeroArmy.Text = Strings.Get("GameSettings_AllowVikingLordHeroArmy_Label");
        _vikingDesc.Text = Strings.Get("GameSettings_AllowVikingLordHeroArmy_Desc");
        _allowLiberatiHeroArmy.Text = Strings.Get("GameSettings_AllowLiberatiHeroArmy_Label");
        _liberatiDesc.Text = Strings.Get("GameSettings_AllowLiberatiHeroArmy_Desc");
        _allowMuleHeroArmy.Text = Strings.Get("GameSettings_AllowMuleHeroArmy_Label");
        _muleDesc.Text = Strings.Get("GameSettings_AllowMuleHeroArmy_Desc");
        _groupLogistics.Text = Strings.Get("GameSettings_Group_Logistics");
        _wagonCapacity10k.Text = Strings.Get("GameSettings_WagonCapacity10k_Label");
        _wagonCapacityDesc.Text = Strings.Get("GameSettings_WagonCapacity10k_Desc");
        _resetBtn.Text = Strings.Get("Gui_ResetDefaults");
    }
}
