using CKToolkit.Core.Common;
using CKToolkit.Core.Lang;
using CKToolkit.I18n;

namespace CKToolkit.Gui;

public sealed class LanguagePage : UserControl
{
    private readonly CheckBox _enabled = new();
    private readonly Label _packLabel = new();
    private readonly ComboBox _pack = new();
    private readonly Label _fontLabel = new();
    private readonly ComboBox _font = new();
    private readonly Label _details = new();
    private readonly Label _extensionHint = new();
    private Dictionary<string, LanguagePack> _packs = new(StringComparer.OrdinalIgnoreCase);

    public LanguagePage()
    {
        BackColor = Color.White;
        Padding = new Padding(24);
        BuildUi();
        ReloadPacks();
    }

    private void BuildUi()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2,
            Padding = new Padding(16), BackColor = Color.FromArgb(248, 250, 252)
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

        _enabled.AutoSize = true;
        _enabled.Font = new Font(Font, FontStyle.Bold);
        _enabled.CheckedChanged += (_, _) => RefreshEnabledState();
        panel.Controls.Add(_enabled, 0, 0);
        panel.SetColumnSpan(_enabled, 2);

        _packLabel.AutoSize = true;
        _packLabel.Anchor = AnchorStyles.Left;
        _packLabel.Margin = new Padding(0, 14, 12, 0);
        panel.Controls.Add(_packLabel, 0, 1);
        _pack.DropDownStyle = ComboBoxStyle.DropDownList;
        _pack.Width = 360;
        _pack.Margin = new Padding(0, 10, 0, 0);
        _pack.SelectedIndexChanged += (_, _) => ShowPackDetails();
        panel.Controls.Add(_pack, 1, 1);

        _fontLabel.AutoSize = true;
        _fontLabel.Anchor = AnchorStyles.Left;
        _fontLabel.Margin = new Padding(0, 14, 12, 0);
        panel.Controls.Add(_fontLabel, 0, 2);
        _font.DropDownStyle = ComboBoxStyle.DropDown;
        _font.Items.AddRange(["微軟正黑體", "Microsoft JhengHei", "Noto Sans CJK TC", "Arial Unicode MS"]);
        _font.Width = 360;
        _font.Margin = new Padding(0, 10, 0, 0);
        panel.Controls.Add(_font, 1, 2);

        _details.AutoSize = true;
        _details.MaximumSize = new Size(800, 0);
        _details.ForeColor = Color.FromArgb(51, 65, 85);
        _details.Margin = new Padding(0, 18, 0, 0);
        panel.Controls.Add(_details, 0, 3);
        panel.SetColumnSpan(_details, 2);

        _extensionHint.AutoSize = true;
        _extensionHint.MaximumSize = new Size(800, 0);
        _extensionHint.ForeColor = Color.FromArgb(71, 85, 105);
        _extensionHint.Margin = new Padding(0, 16, 0, 0);
        panel.Controls.Add(_extensionHint, 0, 4);
        panel.SetColumnSpan(_extensionHint, 2);
        Controls.Add(panel);
    }

    private void ReloadPacks()
    {
        string? selected = (_pack.SelectedItem as PackChoice)?.Id;
        _packs = PackLoader.DiscoverAll();
        _pack.Items.Clear();
        foreach (LanguagePack languagePack in _packs.Values.OrderBy(p => p.Meta.NativeName))
        {
            string name = string.IsNullOrWhiteSpace(languagePack.Meta.NativeName)
                ? languagePack.Meta.Name : languagePack.Meta.NativeName;
            _pack.Items.Add(new PackChoice(languagePack.Meta.Id, $"{name} ({languagePack.Meta.Id})"));
        }
        if (_pack.Items.Count > 0)
        {
            int index = Enumerable.Range(0, _pack.Items.Count)
                .FirstOrDefault(i => (_pack.Items[i] as PackChoice)?.Id.Equals(selected, StringComparison.OrdinalIgnoreCase) == true, -1);
            _pack.SelectedIndex = index >= 0 ? index : 0;
        }
    }

    public void LoadConfig(LangConfig config)
    {
        ReloadPacks();
        _enabled.Checked = !string.IsNullOrWhiteSpace(config.Pack);
        if (_enabled.Checked)
        {
            int found = Enumerable.Range(0, _pack.Items.Count)
                .FirstOrDefault(i => (_pack.Items[i] as PackChoice)?.Id.Equals(config.Pack, StringComparison.OrdinalIgnoreCase) == true, -1);
            if (found >= 0) _pack.SelectedIndex = found;
            else
            {
                _pack.Items.Add(new PackChoice(config.Pack, config.Pack));
                _pack.SelectedIndex = _pack.Items.Count - 1;
            }
        }
        _font.Text = string.IsNullOrWhiteSpace(config.FontFace) ? "微軟正黑體" : config.FontFace;
        RefreshEnabledState();
        ShowPackDetails();
    }

    public void SaveConfig(LangConfig config)
    {
        if (!_enabled.Checked)
        {
            config.Pack = string.Empty;
            return;
        }
        if (_pack.SelectedItem is not PackChoice choice)
            throw new InvalidOperationException(Strings.Get("Gui_Lang_NoPack"));
        if (!_packs.ContainsKey(choice.Id))
            throw new InvalidOperationException(Strings.Get("Error_LangPackNotFound", choice.Id));
        config.Pack = choice.Id;
        config.FontFace = _font.Text.Trim();
        if (string.IsNullOrWhiteSpace(config.FontFace))
            throw new InvalidOperationException(Strings.Get("Gui_Lang_NoFont"));
    }

    public void ApplyLanguage()
    {
        _enabled.Text = Strings.Get("Gui_Lang_Enable");
        _packLabel.Text = Strings.Get("Gui_Lang_Pack");
        _fontLabel.Text = Strings.Get("Gui_Lang_Font");
        _extensionHint.Text = Strings.Get("Gui_Lang_ExtensionHint", Path.Combine(AppContext.BaseDirectory, "langpacks", "<id>"));
        ShowPackDetails();
    }

    private void RefreshEnabledState()
    {
        _pack.Enabled = _enabled.Checked;
        _font.Enabled = _enabled.Checked;
    }

    private void ShowPackDetails()
    {
        if (_pack.SelectedItem is PackChoice choice && _packs.TryGetValue(choice.Id, out LanguagePack? languagePack))
        {
            string authors = languagePack.Meta.Authors.Count > 0 ? string.Join(", ", languagePack.Meta.Authors) : "-";
            _details.Text = Strings.Get("Gui_Lang_Details", languagePack.Meta.Version, authors,
                languagePack.IsBuiltIn ? Strings.Get("Gui_Lang_BuiltIn") : languagePack.SourcePath ?? "-");
        }
        else _details.Text = Strings.Get("Gui_Lang_NoPack");
    }

    private sealed record PackChoice(string Id, string Label)
    {
        public override string ToString() => Label;
    }
}

internal static class GuiEnumerableExtensions
{
    public static int FirstOrDefault(this IEnumerable<int> source, Func<int, bool> predicate, int defaultValue)
    {
        foreach (int value in source) if (predicate(value)) return value;
        return defaultValue;
    }
}
