using CKToolkit.Core.Common;
using CKToolkit.Gui.Layout;
using CKToolkit.I18n;

namespace CKToolkit.Gui;

/// <summary>
/// 遊戲設定與規則調整分頁 (Game Settings Page)。
/// 用於自訂遊戲的核心機制、兵種特性與編隊規則。
/// </summary>
public sealed class GameSettingsPage : ScrollPage
{
    private readonly Label _title = new();
    private readonly Label _subtitle = new();
    private readonly Card _heroArmyCard = new();
    private readonly CheckBox _allowVikingLordHeroArmy = new();
    private readonly Label _vikingDesc = new();
    private readonly CheckBox _allowLiberatiHeroArmy = new();
    private readonly Label _liberatiDesc = new();
    private readonly CheckBox _allowMuleHeroArmy = new();
    private readonly Label _muleDesc = new();
    private readonly CheckBox _instantHeroAttach = new();
    private readonly Label _instantHeroAttachDesc = new();

    private readonly Card _logisticsCard = new();
    private readonly CheckBox _wagonCapacity10k = new();
    private readonly Label _wagonCapacityDesc = new();

    private readonly Button _resetBtn = new();
    private bool _loading;

    public event Action? SettingsChanged;

    public GameSettingsPage()
    {
        BuildUi();
    }

    /// <summary>版面（ISSUE-101）：標題、兩張卡片、還原按鈕由上而下，高度全部由內容決定。</summary>
    private void BuildUi()
    {
        _title.AutoSize = true;
        _title.UseMnemonic = false;
        _title.Font = Ui.UiFont(13F, FontStyle.Bold);
        _title.ForeColor = Ui.TextPrimary;
        Content.Add(_title);
        Ui.Text(_subtitle).Margin = new Padding(0, 4, 0, 14);
        Content.Add(_subtitle);

        AddOption(_heroArmyCard, _allowVikingLordHeroArmy, _vikingDesc);
        AddOption(_heroArmyCard, _allowLiberatiHeroArmy, _liberatiDesc);
        AddOption(_heroArmyCard, _allowMuleHeroArmy, _muleDesc);
        AddOption(_heroArmyCard, _instantHeroAttach, _instantHeroAttachDesc);
        Content.Add(_heroArmyCard);

        AddOption(_logisticsCard, _wagonCapacity10k, _wagonCapacityDesc);
        Content.Add(_logisticsCard);

        Ui.Button(_resetBtn, Color.White, Ui.TextSecondary);
        _resetBtn.Click += (_, _) => ResetToDefaults();
        Content.AddNatural(_resetBtn);
    }

    private void AddOption(Card card, CheckBox option, Label description)
    {
        card.Add(Ui.Option(option, bold: true));
        option.ForeColor = Ui.TextPrimary;
        option.CheckedChanged += (_, _) => OnSettingChanged();
        card.Add(Ui.Description(description));
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
        _instantHeroAttach.Checked = false;
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
        _instantHeroAttach.Checked = config.InstantHeroAttach;
        _loading = false;
    }

    public void SaveConfig(GameSettingsConfig config)
    {
        config.AllowVikingLordHeroArmy = _allowVikingLordHeroArmy.Checked;
        config.AllowLiberatiHeroArmy = _allowLiberatiHeroArmy.Checked;
        config.AllowMuleHeroArmy = _allowMuleHeroArmy.Checked;
        config.WagonCapacity10k = _wagonCapacity10k.Checked;
        config.InstantHeroAttach = _instantHeroAttach.Checked;
    }

    public void ApplyLanguage()
    {
        _title.Text = Strings.Get("GameSettings_Title");
        _subtitle.Text = Strings.Get("GameSettings_Subtitle");
        _heroArmyCard.Title = Strings.Get("GameSettings_Group_HeroArmy");
        _allowVikingLordHeroArmy.Text = Strings.Get("GameSettings_AllowVikingLordHeroArmy_Label");
        _vikingDesc.Text = Strings.Get("GameSettings_AllowVikingLordHeroArmy_Desc");
        _allowLiberatiHeroArmy.Text = Strings.Get("GameSettings_AllowLiberatiHeroArmy_Label");
        _liberatiDesc.Text = Strings.Get("GameSettings_AllowLiberatiHeroArmy_Desc");
        _allowMuleHeroArmy.Text = Strings.Get("GameSettings_AllowMuleHeroArmy_Label");
        _muleDesc.Text = Strings.Get("GameSettings_AllowMuleHeroArmy_Desc");
        _instantHeroAttach.Text = Strings.Get("GameSettings_InstantHeroAttach_Label");
        _instantHeroAttachDesc.Text = Strings.Get("GameSettings_InstantHeroAttach_Desc");
        _logisticsCard.Title = Strings.Get("GameSettings_Group_Logistics");
        _wagonCapacity10k.Text = Strings.Get("GameSettings_WagonCapacity10k_Label");
        _wagonCapacityDesc.Text = Strings.Get("GameSettings_WagonCapacity10k_Desc");
        // 這裡原本查的是不存在的鍵，按鈕上直接印出 "Gui_ResetDefaults"（ISSUE-099 順手修掉）。
        _resetBtn.Text = Strings.Get("GameSettings_ResetDefaults");
    }
}
