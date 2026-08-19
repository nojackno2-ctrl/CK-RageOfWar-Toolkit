using System.Globalization;
using CKToolkit.Core.Common;
using CKToolkit.Core.Trainer;
using CKToolkit.I18n;

namespace CKToolkit.Gui;

public sealed class TrainerPage : UserControl
{
    private readonly CheckBox _enabled = new();
    private readonly CheckBox _numpad = new();
    private readonly CheckBox _keepVanilla = new();
    private readonly Label _playerModeLabel = new();
    private readonly ComboBox _playerMode = new();
    private readonly Label _fixedPlayerLabel = new();
    private readonly NumericUpDown _fixedPlayer = new();
    private readonly TabControl _subTabs = new();
    private readonly TabPage _cheatsTab = new();
    private readonly TabPage _tweaksTab = new();
    private readonly DataGridView _cheats = new();
    private readonly DataGridView _tweaks = new();
    private readonly Button _resetCheats = new();
    private readonly Button _resetTweaks = new();
    private readonly Label _hint = new();
    private bool _loading;

    public TrainerPage()
    {
        BackColor = Color.White;
        Padding = new Padding(12);
        BuildUi();
        PopulateDefinitions();
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        var settings = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, AutoSize = true, WrapContents = true,
            Padding = new Padding(6), BackColor = Color.FromArgb(248, 250, 252)
        };
        _enabled.AutoSize = true;
        _enabled.Font = new Font(Font, FontStyle.Bold);
        _enabled.CheckedChanged += (_, _) => RefreshEnabledState();
        _numpad.AutoSize = true;
        _numpad.CheckedChanged += (_, _) => NumpadModeChanged();
        _keepVanilla.AutoSize = true;
        _playerModeLabel.AutoSize = true;
        _playerModeLabel.Margin = new Padding(18, 8, 4, 0);
        _playerMode.DropDownStyle = ComboBoxStyle.DropDownList;
        _playerMode.Width = 140;
        _playerMode.Items.AddRange(["auto", "fixed"]);
        _playerMode.SelectedIndexChanged += (_, _) => RefreshEnabledState();
        _fixedPlayerLabel.AutoSize = true;
        _fixedPlayerLabel.Margin = new Padding(12, 8, 4, 0);
        _fixedPlayer.Minimum = 1;
        _fixedPlayer.Maximum = 16;
        _fixedPlayer.Width = 64;
        settings.Controls.AddRange([_enabled, _numpad, _keepVanilla, _playerModeLabel, _playerMode, _fixedPlayerLabel, _fixedPlayer]);
        root.Controls.Add(settings, 0, 0);

        _hint.AutoSize = true;
        _hint.MaximumSize = new Size(1000, 0);
        _hint.ForeColor = Color.FromArgb(71, 85, 105);
        _hint.Padding = new Padding(8, 6, 8, 8);
        root.Controls.Add(_hint, 0, 1);

        _subTabs.Dock = DockStyle.Fill;
        _subTabs.Controls.AddRange([_cheatsTab, _tweaksTab]);
        BuildCheatsTab();
        BuildTweaksTab();
        root.Controls.Add(_subTabs, 0, 2);
        Controls.Add(root);
    }

    private void BuildCheatsTab()
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        ConfigureGrid(_cheats);
        _cheats.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Enabled", Width = 72 });
        _cheats.Columns.Add(new DataGridViewTextBoxColumn { Name = "Name", ReadOnly = true, Width = 250 });
        _cheats.Columns.Add(new DataGridViewComboBoxColumn
        {
            Name = "Key", Width = 115, FlatStyle = FlatStyle.Flat,
            DataSource = Cheats.Keys.ToList()
        });
        _cheats.Columns.Add(new DataGridViewTextBoxColumn { Name = "Parameters", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        _cheats.CellToolTipTextNeeded += CheatsCellToolTipTextNeeded;
        _resetCheats.AutoSize = true;
        _resetCheats.Margin = new Padding(6);
        _resetCheats.Click += (_, _) => ResetCheatsToDefaults();
        panel.Controls.Add(_cheats, 0, 0);
        panel.Controls.Add(_resetCheats, 0, 1);
        _cheatsTab.Controls.Add(panel);
    }

    private void BuildTweaksTab()
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        ConfigureGrid(_tweaks);
        _tweaks.Columns.Add(new DataGridViewTextBoxColumn { Name = "Group", ReadOnly = true, Width = 120 });
        _tweaks.Columns.Add(new DataGridViewTextBoxColumn { Name = "Name", ReadOnly = true, Width = 260 });
        _tweaks.Columns.Add(new DataGridViewTextBoxColumn { Name = "Value", Width = 130 });
        _tweaks.Columns.Add(new DataGridViewTextBoxColumn { Name = "Default", ReadOnly = true, Width = 100 });
        _tweaks.Columns.Add(new DataGridViewTextBoxColumn { Name = "Range", ReadOnly = true, AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        _tweaks.CellToolTipTextNeeded += TweaksCellToolTipTextNeeded;
        _resetTweaks.AutoSize = true;
        _resetTweaks.Margin = new Padding(6);
        _resetTweaks.Click += (_, _) => ResetTweaksToDefaults();
        panel.Controls.Add(_tweaks, 0, 0);
        panel.Controls.Add(_resetTweaks, 0, 1);
        _tweaksTab.Controls.Add(panel);
    }

    private static void ConfigureGrid(DataGridView grid)
    {
        grid.Dock = DockStyle.Fill;
        grid.AllowUserToAddRows = false;
        grid.AllowUserToDeleteRows = false;
        grid.AllowUserToResizeRows = false;
        grid.RowHeadersVisible = false;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        grid.MultiSelect = false;
        grid.AutoGenerateColumns = false;
        grid.BackgroundColor = Color.White;
        grid.BorderStyle = BorderStyle.FixedSingle;
        grid.EditMode = DataGridViewEditMode.EditOnEnter;
    }

    private void PopulateDefinitions()
    {
        _loading = true;
        _cheats.Rows.Clear();
        foreach (Cheat cheat in Cheats.All)
        {
            int rowIndex = _cheats.Rows.Add(false, DisplayCheatName(cheat), cheat.DefaultKey, FormatParameters(cheat, null));
            _cheats.Rows[rowIndex].Tag = cheat;
        }
        _tweaks.Rows.Clear();
        foreach (Tweak tweak in Tweaks.All)
        {
            int rowIndex = _tweaks.Rows.Add(DisplayGroup(tweak.Group), DisplayTweakName(tweak),
                FormatDecimal(tweak.Default), FormatDecimal(tweak.Default),
                $"{FormatDecimal(tweak.Minimum)} – {FormatDecimal(tweak.Maximum)}");
            _tweaks.Rows[rowIndex].Tag = tweak;
        }
        _loading = false;
    }

    public void LoadConfig(TrainerConfig config)
    {
        _loading = true;
        _enabled.Checked = config.Enabled;
        _numpad.Checked = config.NumpadKeys;
        _keepVanilla.Checked = config.KeepVanilla;
        _playerMode.SelectedIndex = string.Equals(config.PlayerMode, "fixed", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        _fixedPlayer.Value = Math.Clamp(config.FixedPlayer, 1, 16);

        var configured = config.Cheats.ToDictionary(c => c.Id, StringComparer.Ordinal);
        foreach (DataGridViewRow row in _cheats.Rows)
        {
            var cheat = (Cheat)row.Tag!;
            if (configured.TryGetValue(cheat.Id, out CheatConfig? selected))
            {
                row.Cells["Enabled"].Value = selected.Enabled;
                row.Cells["Key"].Value = string.IsNullOrWhiteSpace(selected.Key) ? cheat.DefaultKeyFor(config.NumpadKeys) : selected.Key;
                row.Cells["Parameters"].Value = FormatParameters(cheat, selected.Parameters);
            }
            else
            {
                row.Cells["Enabled"].Value = cheat.DefaultEnabledFor(config.NumpadKeys);
                row.Cells["Key"].Value = cheat.DefaultKeyFor(config.NumpadKeys);
                row.Cells["Parameters"].Value = FormatParameters(cheat, null);
            }
        }

        foreach (DataGridViewRow row in _tweaks.Rows)
        {
            var tweak = (Tweak)row.Tag!;
            decimal value = config.Tweaks.TryGetValue(tweak.Id, out decimal configuredValue) ? configuredValue : tweak.Default;
            row.Cells["Value"].Value = FormatDecimal(value);
        }
        _loading = false;
        RefreshEnabledState();
        ApplyLanguage();
    }

    public void SaveConfig(TrainerConfig config)
    {
        _cheats.EndEdit();
        _tweaks.EndEdit();
        config.Enabled = _enabled.Checked;
        config.NumpadKeys = _numpad.Checked;
        config.KeepVanilla = _keepVanilla.Checked;
        config.PlayerMode = _playerMode.SelectedIndex == 1 ? "fixed" : "auto";
        config.FixedPlayer = (int)_fixedPlayer.Value;
        config.Cheats = [];

        foreach (DataGridViewRow row in _cheats.Rows)
        {
            var cheat = (Cheat)row.Tag!;
            string key = Convert.ToString(row.Cells["Key"].Value, CultureInfo.InvariantCulture) ?? cheat.DefaultKeyFor(config.NumpadKeys);
            if (!Cheats.Keys.Contains(key, StringComparer.Ordinal))
                throw new InvalidOperationException(Strings.Get("Gui_Trainer_InvalidKey", cheat.Id, key));
            config.Cheats.Add(new CheatConfig
            {
                Id = cheat.Id,
                Enabled = Convert.ToBoolean(row.Cells["Enabled"].Value ?? false, CultureInfo.InvariantCulture),
                Key = key,
                Parameters = ParseParameters(cheat, Convert.ToString(row.Cells["Parameters"].Value, CultureInfo.InvariantCulture) ?? string.Empty)
            });
        }

        config.Tweaks = new Dictionary<string, decimal>(StringComparer.Ordinal);
        foreach (DataGridViewRow row in _tweaks.Rows)
        {
            var tweak = (Tweak)row.Tag!;
            string raw = Convert.ToString(row.Cells["Value"].Value, CultureInfo.InvariantCulture) ?? string.Empty;
            if (!TryParseDecimal(raw, out decimal value) || value < tweak.Minimum || value > tweak.Maximum)
                throw new InvalidOperationException(Strings.Get("Gui_Trainer_InvalidTweak", DisplayTweakName(tweak), raw,
                    FormatDecimal(tweak.Minimum), FormatDecimal(tweak.Maximum)));
            config.Tweaks[tweak.Id] = value;
        }

        // 使用核心產生器做最後驗證：重複按鍵、未知參數或不合法值都在寫檔前被擋下。
        if (config.Enabled && config.Cheats.Any(c => c.Enabled))
        {
            var selections = config.Cheats.Where(c => c.Enabled).Select(c => new CheatSelection
            {
                Id = c.Id, Key = c.Key,
                Parameters = c.Parameters.ToDictionary(p => p.Key, p => (object)p.Value, StringComparer.Ordinal)
            });
            _ = Cheats.BuildScDebug(selections, config.PlayerMode, config.FixedPlayer, config.KeepVanilla);
        }
    }

    public void ApplyLanguage()
    {
        _enabled.Text = Strings.Get("Gui_Trainer_Enable");
        _numpad.Text = Strings.Get("Gui_Trainer_Numpad");
        _keepVanilla.Text = Strings.Get("Gui_Trainer_KeepVanilla");
        _playerModeLabel.Text = Strings.Get("Gui_Trainer_PlayerMode");
        _fixedPlayerLabel.Text = Strings.Get("Gui_Trainer_FixedPlayer");
        _cheatsTab.Text = Strings.Get("Gui_Trainer_Cheats");
        _tweaksTab.Text = Strings.Get("Gui_Trainer_Tweaks");
        _resetCheats.Text = Strings.Get("Gui_Trainer_ResetCheats");
        _resetTweaks.Text = Strings.Get("Gui_Trainer_ResetTweaks");
        _hint.Text = Strings.Get("Gui_Trainer_Hint");
        _cheats.Columns["Enabled"]!.HeaderText = Strings.Get("Gui_Enabled");
        _cheats.Columns["Name"]!.HeaderText = Strings.Get("Gui_Name");
        _cheats.Columns["Key"]!.HeaderText = Strings.Get("Gui_Key");
        _cheats.Columns["Parameters"]!.HeaderText = Strings.Get("Gui_Parameters");
        _tweaks.Columns["Group"]!.HeaderText = Strings.Get("Gui_Group");
        _tweaks.Columns["Name"]!.HeaderText = Strings.Get("Gui_Name");
        _tweaks.Columns["Value"]!.HeaderText = Strings.Get("Gui_Value");
        _tweaks.Columns["Default"]!.HeaderText = Strings.Get("Gui_Default");
        _tweaks.Columns["Range"]!.HeaderText = Strings.Get("Gui_Range");

        foreach (DataGridViewRow row in _cheats.Rows)
            if (row.Tag is Cheat cheat) row.Cells["Name"].Value = DisplayCheatName(cheat);
        foreach (DataGridViewRow row in _tweaks.Rows)
            if (row.Tag is Tweak tweak)
            {
                row.Cells["Group"].Value = DisplayGroup(tweak.Group);
                row.Cells["Name"].Value = DisplayTweakName(tweak);
            }
        RefreshPlayerModeItems();
    }

    private void RefreshPlayerModeItems()
    {
        int selected = _playerMode.SelectedIndex < 0 ? 0 : _playerMode.SelectedIndex;
        _playerMode.Items.Clear();
        _playerMode.Items.Add(Strings.Get("Gui_Trainer_PlayerAuto"));
        _playerMode.Items.Add(Strings.Get("Gui_Trainer_PlayerFixed"));
        _playerMode.SelectedIndex = selected;
    }

    private void RefreshEnabledState()
    {
        bool enabled = _enabled.Checked;
        _numpad.Enabled = enabled;
        _keepVanilla.Enabled = enabled;
        _playerMode.Enabled = enabled;
        _fixedPlayer.Enabled = enabled && _playerMode.SelectedIndex == 1;
        _subTabs.Enabled = enabled;
    }

    private void NumpadModeChanged()
    {
        if (_loading) return;
        foreach (DataGridViewRow row in _cheats.Rows)
        {
            var cheat = (Cheat)row.Tag!;
            row.Cells["Key"].Value = cheat.DefaultKeyFor(_numpad.Checked);
            row.Cells["Enabled"].Value = cheat.DefaultEnabledFor(_numpad.Checked);
        }
    }

    private void ResetCheatsToDefaults()
    {
        foreach (DataGridViewRow row in _cheats.Rows)
        {
            var cheat = (Cheat)row.Tag!;
            row.Cells["Enabled"].Value = cheat.DefaultEnabledFor(_numpad.Checked);
            row.Cells["Key"].Value = cheat.DefaultKeyFor(_numpad.Checked);
            row.Cells["Parameters"].Value = FormatParameters(cheat, null);
        }
    }

    private void ResetTweaksToDefaults()
    {
        foreach (DataGridViewRow row in _tweaks.Rows)
            if (row.Tag is Tweak tweak) row.Cells["Value"].Value = FormatDecimal(tweak.Default);
    }

    private void CheatsCellToolTipTextNeeded(object? sender, DataGridViewCellToolTipTextNeededEventArgs e)
    {
        if (e.RowIndex >= 0 && _cheats.Rows[e.RowIndex].Tag is Cheat cheat)
            e.ToolTipText = Strings.EffectiveLanguage == "zh-TW" ? cheat.Description : cheat.Id;
    }

    private void TweaksCellToolTipTextNeeded(object? sender, DataGridViewCellToolTipTextNeededEventArgs e)
    {
        if (e.RowIndex >= 0 && _tweaks.Rows[e.RowIndex].Tag is Tweak tweak)
            e.ToolTipText = Strings.EffectiveLanguage == "zh-TW" ? tweak.Description : tweak.Id;
    }

    private static string FormatParameters(Cheat cheat, IReadOnlyDictionary<string, string>? configured)
    {
        var values = new List<string>();
        foreach (CheatParam parameter in cheat.Parameters.Where(p => !p.Hidden))
        {
            string value = configured is not null && configured.TryGetValue(parameter.Name, out string? selected)
                ? selected : Convert.ToString(parameter.Default, CultureInfo.InvariantCulture) ?? string.Empty;
            values.Add($"{parameter.Name}={value}");
        }
        return string.Join("; ", values);
    }

    private static Dictionary<string, string> ParseParameters(Cheat cheat, string text)
    {
        var result = cheat.Parameters.ToDictionary(p => p.Name,
            p => Convert.ToString(p.Default, CultureInfo.InvariantCulture) ?? string.Empty, StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(text)) return result;
        foreach (string part in text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int equals = part.IndexOf('=');
            if (equals <= 0) throw new InvalidOperationException(Strings.Get("Gui_Trainer_InvalidParameter", part));
            string name = part[..equals].Trim();
            string value = part[(equals + 1)..].Trim();
            CheatParam? definition = cheat.Parameters.FirstOrDefault(p => p.Name == name);
            if (definition is null) throw new InvalidOperationException(Strings.Get("Gui_Trainer_InvalidParameter", name));
            if (!definition.IsText && (!TryParseDecimal(value, out decimal numeric) || numeric < definition.Minimum || numeric > definition.Maximum))
                throw new InvalidOperationException(Strings.Get("Gui_Trainer_InvalidParameter", part));
            result[name] = value;
        }
        return result;
    }

    private static bool TryParseDecimal(string text, out decimal value) =>
        decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out value) ||
        decimal.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out value);

    private static string FormatDecimal(decimal value) => value.ToString(CultureInfo.InvariantCulture);

    private static string DisplayCheatName(Cheat cheat) =>
        Strings.EffectiveLanguage == "zh-TW" ? cheat.Name : Humanize(cheat.Id);

    private static string DisplayTweakName(Tweak tweak) =>
        Strings.EffectiveLanguage == "zh-TW" ? tweak.Label : Humanize(tweak.Id);

    private static string DisplayGroup(string group) => Strings.EffectiveLanguage == "zh-TW" ? group : group switch
    {
        Tweaks.GroupHero => "Hero",
        Tweaks.GroupTown => "Settlements",
        Tweaks.GroupEconomy => "Economy",
        Tweaks.GroupProduction => "Production & Research",
        Tweaks.GroupUnits => "Unit Stats",
        _ => group
    };

    private static string Humanize(string id) => CultureInfo.InvariantCulture.TextInfo
        .ToTitleCase(id.Replace('_', ' '));
}
