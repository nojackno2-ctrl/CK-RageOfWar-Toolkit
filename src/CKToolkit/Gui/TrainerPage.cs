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
    private readonly TabPage _scopedTweaksTab = new();
    private readonly KeyCaptureGrid _cheats = new();
    private readonly DataGridView _tweaks = new();
    private readonly DataGridView _scopedSimple = new();
    private readonly DataGridView _scopedSettlement = new();
    private readonly Button _resetCheats = new();
    private readonly Button _resetTweaks = new();
    private readonly Button _resetScopedTweaks = new();
    private readonly Button _launchGame = new();
    private readonly Label _launchHint = new();
    private readonly Label _hint = new();
    private readonly Label _tweaksWarning = new();
    private readonly Label _scopedWarning = new();
    private readonly Label _scopedSimpleLabel = new();
    private readonly Label _scopedSettlementLabel = new();
    private readonly Label _riskBanner = new();
    private bool _loading;
    private int _capturingRow = -1;

    private static readonly HashSet<Keys> CaptureIgnoredKeys =
    [
        Keys.ControlKey, Keys.LControlKey, Keys.RControlKey,
        Keys.ShiftKey, Keys.LShiftKey, Keys.RShiftKey,
        Keys.Menu, Keys.LMenu, Keys.RMenu,
        Keys.LWin, Keys.RWin,
    ];

    /// <summary>使用者按下修改器頁裡的「啟動遊戲」。MainForm 收到後會先套用目前設定、再帶診斷層啟動。</summary>
    public event Action? LaunchGameRequested;

    public TrainerPage()
    {
        AutoScroll = true;
        BackColor = Color.White;
        Padding = new Padding(12);
        BuildUi();
        PopulateDefinitions();
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, RowCount = 5 };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var settings = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, AutoSize = true, WrapContents = true,
            Padding = new Padding(6), BackColor = Color.FromArgb(248, 250, 252)
        };
        _enabled.AutoSize = true;
        _enabled.Font = new Font(Font, FontStyle.Bold);
        _enabled.CheckedChanged += (_, _) => { RefreshEnabledState(); UpdateRiskBanner(); };
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

        _riskBanner.AutoSize = true;
        _riskBanner.Dock = DockStyle.Fill;
        _riskBanner.MaximumSize = new Size(1050, 0);
        _riskBanner.Font = new Font(Font, FontStyle.Bold);
        _riskBanner.Padding = new Padding(12, 9, 12, 9);
        _riskBanner.Margin = new Padding(6, 6, 6, 4);
        root.Controls.Add(_riskBanner, 0, 1);

        // 「啟動遊戲」放在作弊／數值設定的下方：先完成調整，再一鍵套用現在的設定並
        // 帶診斷層啟動，操作順序由上而下。
        var launchRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, AutoSize = true, WrapContents = false,
            Padding = new Padding(6, 6, 6, 2)
        };
        _launchGame.AutoSize = true;
        _launchGame.MinimumSize = new Size(140, 34);
        _launchGame.FlatStyle = FlatStyle.Flat;
        _launchGame.BackColor = Color.FromArgb(37, 99, 235);
        _launchGame.ForeColor = Color.White;
        _launchGame.FlatAppearance.BorderColor = Color.FromArgb(37, 99, 235);
        _launchGame.Font = new Font(Font, FontStyle.Bold);
        _launchGame.Margin = new Padding(0, 0, 12, 0);
        _launchGame.Click += (_, _) => LaunchGameRequested?.Invoke();
        _launchHint.AutoSize = true;
        _launchHint.Anchor = AnchorStyles.Left;
        _launchHint.ForeColor = Color.FromArgb(100, 116, 139);
        _launchHint.Margin = new Padding(0, 10, 0, 0);
        launchRow.Controls.AddRange([_launchGame, _launchHint]);

        _hint.AutoSize = true;
        _hint.MaximumSize = new Size(1000, 0);
        _hint.ForeColor = Color.FromArgb(71, 85, 105);
        _hint.Padding = new Padding(8, 6, 8, 8);
        root.Controls.Add(_hint, 0, 2);

        _subTabs.Dock = DockStyle.Top;
        _subTabs.Height = 440;
        _subTabs.MinimumSize = new Size(0, 360);
        _subTabs.Controls.AddRange([_cheatsTab, _tweaksTab, _scopedTweaksTab]);
        BuildCheatsTab();
        BuildTweaksTab();
        BuildScopedTweaksTab();
        root.Controls.Add(_subTabs, 0, 3);
        root.Controls.Add(launchRow, 0, 4);
        Controls.Add(root);
    }

    private void BuildCheatsTab()
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        ConfigureGrid(_cheats);
        _cheats.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Enabled", Width = 72 });
        _cheats.Columns.Add(new DataGridViewTextBoxColumn { Name = "Name", ReadOnly = true, Width = 230 });
        _cheats.Columns.Add(new DataGridViewTextBoxColumn { Name = "Key", Width = 130, ReadOnly = true });
        _cheats.Columns.Add(new DataGridViewButtonColumn
        {
            Name = "ClearKey", Text = "×", UseColumnTextForButtonValue = true,
            Width = 36, MinimumWidth = 36, FlatStyle = FlatStyle.Popup,
        });
        _cheats.Columns.Add(new DataGridViewTextBoxColumn { Name = "Parameters", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, ReadOnly = true });
        _cheats.Columns.Add(new DataGridViewButtonColumn
        {
            Name = "ConfigParams", Width = 85, MinimumWidth = 85, FlatStyle = FlatStyle.Popup,
        });
        _cheats.CellToolTipTextNeeded += CheatsCellToolTipTextNeeded;
        _cheats.CellClick += CheatsCellClick;
        _cheats.CellValueChanged += (_, _) => { if (!_loading) UpdateRiskBanner(); };
        _cheats.KeyCaptured += OnKeyCaptured;
        _cheats.Leave += (_, _) => CancelCapture();
        _resetCheats.AutoSize = true;
        _resetCheats.Margin = new Padding(6);
        _resetCheats.Click += (_, _) => ResetCheatsToDefaults();
        panel.Controls.Add(_cheats, 0, 0);
        panel.Controls.Add(_resetCheats, 0, 1);
        _cheatsTab.Controls.Add(panel);
    }

    private void BuildTweaksTab()
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _tweaksWarning.AutoSize = true;
        _tweaksWarning.MaximumSize = new Size(1000, 0);
        _tweaksWarning.ForeColor = Color.FromArgb(180, 83, 9);
        _tweaksWarning.Font = new Font(Font, FontStyle.Bold);
        _tweaksWarning.Padding = new Padding(8, 6, 8, 6);
        panel.Controls.Add(_tweaksWarning, 0, 0);

        ConfigureGrid(_tweaks);
        _tweaks.Columns.Add(new DataGridViewTextBoxColumn { Name = "Group", ReadOnly = true, Width = 120 });
        _tweaks.Columns.Add(new DataGridViewTextBoxColumn { Name = "Name", ReadOnly = true, Width = 260 });
        _tweaks.Columns.Add(new DataGridViewTextBoxColumn { Name = "Value", Width = 130 });
        _tweaks.Columns.Add(new DataGridViewTextBoxColumn { Name = "Default", ReadOnly = true, Width = 100 });
        _tweaks.Columns.Add(new DataGridViewTextBoxColumn { Name = "Range", ReadOnly = true, AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        _tweaks.CellToolTipTextNeeded += TweaksCellToolTipTextNeeded;
        _tweaks.CellValueChanged += (_, _) => { if (!_loading) UpdateRiskBanner(); };
        _tweaks.CellEndEdit += (_, _) => UpdateRiskBanner();
        _resetTweaks.AutoSize = true;
        _resetTweaks.Margin = new Padding(6);
        _resetTweaks.Click += (_, _) => ResetTweaksToDefaults();
        panel.Controls.Add(_tweaks, 0, 1);
        panel.Controls.Add(_resetTweaks, 0, 2);
        _tweaksTab.Controls.Add(panel);
    }

    private void BuildScopedTweaksTab()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 6,
            Padding = new Padding(4)
        };
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 42F));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 58F));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _scopedWarning.AutoSize = true;
        _scopedWarning.Dock = DockStyle.Fill;
        _scopedWarning.MaximumSize = new Size(1000, 0);
        _scopedWarning.Padding = new Padding(10, 8, 10, 8);
        _scopedWarning.Margin = new Padding(2, 2, 2, 6);
        _scopedWarning.BackColor = Color.FromArgb(239, 246, 255);
        _scopedWarning.ForeColor = Color.FromArgb(30, 64, 175);
        panel.Controls.Add(_scopedWarning, 0, 0);

        ConfigureSectionLabel(_scopedSimpleLabel);
        panel.Controls.Add(_scopedSimpleLabel, 0, 1);
        ConfigureGrid(_scopedSimple);
        AddScopedIdentityColumns(_scopedSimple, nameWidth: 220);
        AddScopedValueColumn(_scopedSimple, "Self", 112);
        AddScopedValueColumn(_scopedSimple, "Enemy", 112);
        AddScopedTailColumns(_scopedSimple);
        ConfigureScopedGrid(_scopedSimple);
        panel.Controls.Add(_scopedSimple, 0, 2);

        ConfigureSectionLabel(_scopedSettlementLabel);
        _scopedSettlementLabel.Margin = new Padding(2, 8, 2, 3);
        panel.Controls.Add(_scopedSettlementLabel, 0, 3);
        ConfigureGrid(_scopedSettlement);
        AddScopedIdentityColumns(_scopedSettlement, nameWidth: 190);
        AddScopedValueColumn(_scopedSettlement, "SelfTownhall", 108);
        AddScopedValueColumn(_scopedSettlement, "SelfVillage", 108);
        AddScopedValueColumn(_scopedSettlement, "EnemyTownhall", 108);
        AddScopedValueColumn(_scopedSettlement, "EnemyVillage", 108);
        AddScopedTailColumns(_scopedSettlement);
        ConfigureScopedGrid(_scopedSettlement);
        panel.Controls.Add(_scopedSettlement, 0, 4);

        _resetScopedTweaks.AutoSize = true;
        _resetScopedTweaks.Margin = new Padding(2, 7, 2, 2);
        _resetScopedTweaks.Click += (_, _) => ResetAllScopedTweaks();
        panel.Controls.Add(_resetScopedTweaks, 0, 5);
        _scopedTweaksTab.Controls.Add(panel);
    }

    private static void ConfigureSectionLabel(Label label)
    {
        label.AutoSize = true;
        label.Dock = DockStyle.Fill;
        label.Font = new Font(label.Font, FontStyle.Bold);
        label.ForeColor = Color.FromArgb(51, 65, 85);
        label.Padding = new Padding(2, 3, 2, 3);
        label.Margin = new Padding(2, 2, 2, 3);
    }

    private static void AddScopedIdentityColumns(DataGridView grid, int nameWidth)
    {
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Group", ReadOnly = true, Width = 105 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Name", ReadOnly = true, Width = nameWidth });
    }

    private static void AddScopedValueColumn(DataGridView grid, string name, int width)
    {
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = name,
            Width = width,
            DefaultCellStyle = new DataGridViewCellStyle
            {
                Alignment = DataGridViewContentAlignment.MiddleRight,
                Font = new Font("Consolas", 9F)
            }
        });
    }

    private static void AddScopedTailColumns(DataGridView grid)
    {
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Original",
            ReadOnly = true,
            Width = 116,
            DefaultCellStyle = new DataGridViewCellStyle
            {
                Alignment = DataGridViewContentAlignment.MiddleRight,
                ForeColor = Color.FromArgb(100, 116, 139),
                Font = new Font("Consolas", 9F)
            }
        });
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Range",
            ReadOnly = true,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            MinimumWidth = 120
        });
        grid.Columns.Add(new DataGridViewButtonColumn
        {
            Name = "ResetRow",
            Width = 82,
            MinimumWidth = 82,
            FlatStyle = FlatStyle.Flat,
            UseColumnTextForButtonValue = true
        });
    }

    private void ConfigureScopedGrid(DataGridView grid)
    {
        grid.CellToolTipTextNeeded += ScopedCellToolTipTextNeeded;
        grid.CellClick += ScopedGridCellClick;
        grid.CellValueChanged += (_, _) => { if (!_loading) UpdateRiskBanner(); };
        grid.CellEndEdit += (_, _) => UpdateRiskBanner();
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
        grid.EnableHeadersVisualStyles = false;
        grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(241, 245, 249);
        grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(51, 65, 85);
        grid.ColumnHeadersDefaultCellStyle.Font = new Font(grid.Font, FontStyle.Bold);
        grid.ColumnHeadersHeight = 34;
        grid.RowTemplate.Height = 30;
        grid.GridColor = Color.FromArgb(226, 232, 240);
        grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(219, 234, 254);
        grid.DefaultCellStyle.SelectionForeColor = Color.FromArgb(15, 23, 42);
        grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(248, 250, 252);
    }

    private void PopulateDefinitions()
    {
        _loading = true;
        _cheats.Rows.Clear();
        foreach (Cheat cheat in Cheats.All)
        {
            int rowIndex = _cheats.Rows.Add(false, DisplayCheatName(cheat), string.Empty, string.Empty, string.Empty);
            var row = _cheats.Rows[rowIndex];
            row.Tag = cheat;
            SetKeyCell(row, cheat.DefaultKey);
            var defaultParams = cheat.Defaults().ToDictionary(
                kvp => kvp.Key,
                kvp => Convert.ToString(kvp.Value, CultureInfo.InvariantCulture) ?? string.Empty,
                StringComparer.Ordinal);
            SetParameterCell(row, defaultParams);
        }
        _tweaks.Rows.Clear();
        foreach (Tweak tweak in Tweaks.All)
        {
            int rowIndex = _tweaks.Rows.Add(DisplayGroup(tweak.Group), DisplayTweakName(tweak),
                FormatDecimal(tweak.Default), FormatDecimal(tweak.Default),
                $"{FormatDecimal(tweak.Minimum)} – {FormatDecimal(tweak.Maximum)}");
            _tweaks.Rows[rowIndex].Tag = tweak;
        }

        _scopedSimple.Rows.Clear();
        _scopedSettlement.Rows.Clear();
        var vanillaTrainer = new TrainerConfig();
        foreach (Tweak tweak in Tweaks.All)
        {
            IReadOnlyList<string> scopes = ScopedTweakPatch.GetSupportedScopes(tweak.Id);
            if (scopes.Count == 2)
            {
                string original = string.Join(" / ", scopes.Select(scope =>
                    FormatDecimal(ScopedTweakPatch.GetScopedFallbackValue(vanillaTrainer, tweak.Id, scope))));
                int rowIndex = _scopedSimple.Rows.Add(
                    DisplayGroup(tweak.Group), DisplayTweakName(tweak), string.Empty, string.Empty,
                    original, $"{FormatDecimal(tweak.Minimum)} – {FormatDecimal(tweak.Maximum)}");
                _scopedSimple.Rows[rowIndex].Tag = tweak;
            }
            else if (scopes.Count == 4)
            {
                string original = string.Join(" / ", scopes.Select(scope =>
                    FormatDecimal(ScopedTweakPatch.GetScopedFallbackValue(vanillaTrainer, tweak.Id, scope))));
                int rowIndex = _scopedSettlement.Rows.Add(
                    DisplayGroup(tweak.Group), DisplayTweakName(tweak),
                    string.Empty, string.Empty, string.Empty, string.Empty,
                    original, $"{FormatDecimal(tweak.Minimum)} – {FormatDecimal(tweak.Maximum)}");
                _scopedSettlement.Rows[rowIndex].Tag = tweak;
            }
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
                SetKeyCell(row, string.IsNullOrWhiteSpace(selected.Key) ? cheat.DefaultKeyFor(config.NumpadKeys) : selected.Key);
                var dict = new Dictionary<string, string>(selected.Parameters, StringComparer.Ordinal);
                foreach (var p in cheat.Parameters.Where(p => !p.Hidden))
                {
                    if (!dict.ContainsKey(p.Name))
                        dict[p.Name] = Convert.ToString(p.Default, CultureInfo.InvariantCulture) ?? string.Empty;
                }
                SetParameterCell(row, dict);
            }
            else
            {
                row.Cells["Enabled"].Value = cheat.DefaultEnabledFor(config.NumpadKeys);
                SetKeyCell(row, cheat.DefaultKeyFor(config.NumpadKeys));
                var dict = cheat.Defaults().ToDictionary(
                    kvp => kvp.Key,
                    kvp => Convert.ToString(kvp.Value, CultureInfo.InvariantCulture) ?? string.Empty,
                    StringComparer.Ordinal);
                SetParameterCell(row, dict);
            }
        }

        foreach (DataGridViewRow row in _tweaks.Rows)
        {
            var tweak = (Tweak)row.Tag!;
            decimal value = config.Tweaks.TryGetValue(tweak.Id, out decimal configuredValue) ? configuredValue : tweak.Default;
            row.Cells["Value"].Value = FormatDecimal(value);
        }
        LoadScopedGrid(_scopedSimple, config);
        LoadScopedGrid(_scopedSettlement, config);
        _loading = false;
        RefreshEnabledState();
        ApplyLanguage();
        UpdateRiskBanner();
    }

    public void SaveConfig(TrainerConfig config)
    {
        CancelCapture();
        _cheats.EndEdit();
        _tweaks.EndEdit();
        _scopedSimple.EndEdit();
        _scopedSettlement.EndEdit();
        config.Enabled = _enabled.Checked;
        config.NumpadKeys = _numpad.Checked;
        config.KeepVanilla = _keepVanilla.Checked;
        config.PlayerMode = _playerMode.SelectedIndex == 1 ? "fixed" : "auto";
        config.FixedPlayer = (int)_fixedPlayer.Value;
        config.Cheats = [];

        foreach (DataGridViewRow row in _cheats.Rows)
        {
            var cheat = (Cheat)row.Tag!;
            bool rowEnabled = Convert.ToBoolean(row.Cells["Enabled"].Value ?? false, CultureInfo.InvariantCulture);
            string? key = row.Cells["Key"].Tag as string;
            if (key is null)
            {
                // 按了「清除」但沒有重新指定按鍵：已啟用的作弊一定要有按鍵才能觸發，
                // 停用的作弊按鍵欄本來就不會寫進 scdebug.xml（見 TrainerInstaller），空著沒差。
                if (rowEnabled)
                    throw new InvalidOperationException(Strings.Get("Gui_Trainer_KeyRequired", DisplayCheatName(cheat)));
                key = string.Empty;
            }
            else if (!Cheats.Keys.Contains(key, StringComparer.Ordinal))
            {
                throw new InvalidOperationException(Strings.Get("Gui_Trainer_InvalidKey", cheat.Id, key));
            }

            var paramDict = row.Cells["Parameters"].Tag as Dictionary<string, string>
                ?? cheat.Defaults().ToDictionary(
                    kvp => kvp.Key,
                    kvp => Convert.ToString(kvp.Value, CultureInfo.InvariantCulture) ?? string.Empty,
                    StringComparer.Ordinal);

            config.Cheats.Add(new CheatConfig
            {
                Id = cheat.Id,
                Enabled = rowEnabled,
                Key = key,
                Parameters = new Dictionary<string, string>(paramDict, StringComparer.Ordinal)
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

        config.ScopedTweaks = new Dictionary<string, Dictionary<string, decimal>>(StringComparer.Ordinal);
        SaveScopedGrid(_scopedSimple, config);
        SaveScopedGrid(_scopedSettlement, config);

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
        _scopedTweaksTab.Text = Strings.Get("Gui_Trainer_ScopedTweaks");
        _resetCheats.Text = Strings.Get("Gui_Trainer_ResetCheats");
        _resetTweaks.Text = Strings.Get("Gui_Trainer_ResetTweaks");
        _resetScopedTweaks.Text = Strings.Get("Gui_Trainer_ResetScopedTweaks");
        _launchGame.Text = Strings.Get("Gui_Trainer_Launch");
        _launchHint.Text = Strings.Get("Gui_Trainer_LaunchHint");
        _hint.Text = Strings.Get("Gui_Trainer_Hint");
        _tweaksWarning.Text = Strings.Get("Gui_Trainer_TweaksWarning");
        _scopedWarning.Text = Strings.Get("Gui_Trainer_ScopedWarning");
        _scopedSimpleLabel.Text = Strings.Get("Gui_Trainer_ScopedSimple");
        _scopedSettlementLabel.Text = Strings.Get("Gui_Trainer_ScopedSettlement");
        UpdateRiskBanner();
        _cheats.Columns["Enabled"]!.HeaderText = Strings.Get("Gui_Enabled");
        _cheats.Columns["Name"]!.HeaderText = Strings.Get("Gui_Name");
        _cheats.Columns["Key"]!.HeaderText = Strings.Get("Gui_Key");
        _cheats.Columns["ClearKey"]!.HeaderText = Strings.Get("Gui_Trainer_ClearKey");
        _cheats.Columns["Parameters"]!.HeaderText = Strings.Get("Gui_Parameters");
        _cheats.Columns["ConfigParams"]!.HeaderText = string.Empty;
        _tweaks.Columns["Group"]!.HeaderText = Strings.Get("Gui_Group");
        _tweaks.Columns["Name"]!.HeaderText = Strings.Get("Gui_Name");
        _tweaks.Columns["Value"]!.HeaderText = Strings.Get("Gui_Value");
        _tweaks.Columns["Default"]!.HeaderText = Strings.Get("Gui_Default");
        _tweaks.Columns["Range"]!.HeaderText = Strings.Get("Gui_Range");
        ApplyScopedGridLanguage(_scopedSimple, settlement: false);
        ApplyScopedGridLanguage(_scopedSettlement, settlement: true);

        foreach (DataGridViewRow row in _cheats.Rows)
        {
            if (row.Tag is Cheat cheat)
            {
                row.Cells["Name"].Value = DisplayCheatName(cheat);
                if (row.Cells["Parameters"].Tag is Dictionary<string, string> dict)
                    SetParameterCell(row, dict);
            }
        }
        foreach (DataGridViewRow row in _tweaks.Rows)
            if (row.Tag is Tweak tweak)
            {
                row.Cells["Group"].Value = DisplayGroup(tweak.Group);
                row.Cells["Name"].Value = DisplayTweakName(tweak);
            }
        foreach (DataGridView grid in new[] { _scopedSimple, _scopedSettlement })
        {
            foreach (DataGridViewRow row in grid.Rows)
            {
                if (row.Tag is not Tweak tweak) continue;
                row.Cells["Group"].Value = DisplayGroup(tweak.Group);
                row.Cells["Name"].Value = DisplayTweakName(tweak);
            }
        }
        RefreshPlayerModeItems();
    }

    private static void ApplyScopedGridLanguage(DataGridView grid, bool settlement)
    {
        grid.Columns["Group"]!.HeaderText = Strings.Get("Gui_Group");
        grid.Columns["Name"]!.HeaderText = Strings.Get("Gui_Name");
        if (settlement)
        {
            grid.Columns["SelfTownhall"]!.HeaderText = Strings.Get("Gui_Trainer_SelfTownhall");
            grid.Columns["SelfVillage"]!.HeaderText = Strings.Get("Gui_Trainer_SelfVillage");
            grid.Columns["EnemyTownhall"]!.HeaderText = Strings.Get("Gui_Trainer_EnemyTownhall");
            grid.Columns["EnemyVillage"]!.HeaderText = Strings.Get("Gui_Trainer_EnemyVillage");
        }
        else
        {
            grid.Columns["Self"]!.HeaderText = Strings.Get("Gui_Trainer_Self");
            grid.Columns["Enemy"]!.HeaderText = Strings.Get("Gui_Trainer_Enemy");
        }
        grid.Columns["Original"]!.HeaderText = Strings.Get("Gui_Trainer_OriginalScopes");
        grid.Columns["Range"]!.HeaderText = Strings.Get("Gui_Range");
        var resetColumn = (DataGridViewButtonColumn)grid.Columns["ResetRow"]!;
        resetColumn.HeaderText = string.Empty;
        resetColumn.Text = Strings.Get("Gui_Trainer_ResetRow");
    }

    private static IReadOnlyList<(string Scope, string Column)> ScopeBindings(DataGridView grid) =>
        grid.Columns.Contains("Self")
            ? [("self", "Self"), ("enemy", "Enemy")]
            :
            [
                ("selfTownhall", "SelfTownhall"),
                ("selfVillage", "SelfVillage"),
                ("enemyTownhall", "EnemyTownhall"),
                ("enemyVillage", "EnemyVillage")
            ];

    private static void LoadScopedGrid(DataGridView grid, TrainerConfig config)
    {
        foreach (DataGridViewRow row in grid.Rows)
        {
            if (row.Tag is not Tweak tweak) continue;
            foreach ((string scope, string column) in ScopeBindings(grid))
            {
                row.Cells[column].Value = FormatDecimal(
                    ScopedTweakPatch.GetEffectiveScopedValue(config, tweak.Id, scope));
            }
        }
    }

    private static void SaveScopedGrid(DataGridView grid, TrainerConfig config)
    {
        foreach (DataGridViewRow row in grid.Rows)
        {
            if (row.Tag is not Tweak tweak) continue;
            foreach ((string scope, string column) in ScopeBindings(grid))
            {
                string raw = Convert.ToString(row.Cells[column].Value, CultureInfo.InvariantCulture) ?? string.Empty;
                if (!TryParseDecimal(raw, out decimal value) ||
                    value < tweak.Minimum || value > tweak.Maximum)
                {
                    throw new InvalidOperationException(Strings.Get(
                        "Gui_Trainer_InvalidScopedTweak",
                        DisplayTweakName(tweak),
                        Strings.Get(ScopeLabelKey(scope)),
                        raw,
                        FormatDecimal(tweak.Minimum),
                        FormatDecimal(tweak.Maximum)));
                }

                decimal fallback = ScopedTweakPatch.GetScopedFallbackValue(config, tweak.Id, scope);
                if (value == fallback) continue;

                if (!config.ScopedTweaks.TryGetValue(tweak.Id, out Dictionary<string, decimal>? values))
                {
                    values = new Dictionary<string, decimal>(StringComparer.Ordinal);
                    config.ScopedTweaks[tweak.Id] = values;
                }
                values[scope] = value;
            }
        }
    }

    private static string ScopeLabelKey(string scope) => scope switch
    {
        "self" => "Gui_Trainer_Self",
        "enemy" => "Gui_Trainer_Enemy",
        "selfTownhall" => "Gui_Trainer_SelfTownhall",
        "selfVillage" => "Gui_Trainer_SelfVillage",
        "enemyTownhall" => "Gui_Trainer_EnemyTownhall",
        "enemyVillage" => "Gui_Trainer_EnemyVillage",
        _ => throw new ArgumentOutOfRangeException(nameof(scope), scope, null)
    };

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

    private void UpdateRiskBanner()
    {
        TrainerRiskLevel risk;
        try
        {
            risk = TrainerRisk.Assess(BuildRiskSnapshot());
        }
        catch
        {
            risk = TrainerRiskLevel.Extreme;
        }

        string statusKey = risk switch
        {
            TrainerRiskLevel.Extreme => "Gui_Trainer_RiskExtreme",
            TrainerRiskLevel.Elevated => "Gui_Trainer_RiskElevated",
            _ => "Gui_Trainer_RiskNormal",
        };
        _riskBanner.Text = Strings.Get("Gui_Trainer_RiskBanner", Strings.Get(statusKey));
        _riskBanner.ForeColor = risk switch
        {
            TrainerRiskLevel.Extreme => Color.FromArgb(153, 27, 27),
            TrainerRiskLevel.Elevated => Color.FromArgb(146, 64, 14),
            _ => Color.FromArgb(55, 65, 81),
        };
        _riskBanner.BackColor = risk switch
        {
            TrainerRiskLevel.Extreme => Color.FromArgb(254, 226, 226),
            TrainerRiskLevel.Elevated => Color.FromArgb(255, 237, 213),
            _ => Color.FromArgb(241, 245, 249),
        };
    }

    private TrainerConfig BuildRiskSnapshot()
    {
        var config = new TrainerConfig { Enabled = _enabled.Checked };
        foreach (DataGridViewRow row in _tweaks.Rows)
        {
            if (row.Tag is not Tweak tweak) continue;
            string raw = Convert.ToString(row.Cells["Value"].Value, CultureInfo.InvariantCulture) ?? string.Empty;
            if (TryParseDecimal(raw, out decimal value)) config.Tweaks[tweak.Id] = value;
        }
        SaveScopedGrid(_scopedSimple, config);
        SaveScopedGrid(_scopedSettlement, config);
        foreach (DataGridViewRow row in _cheats.Rows)
        {
            if (row.Tag is not Cheat cheat) continue;
            config.Cheats.Add(new CheatConfig
            {
                Id = cheat.Id,
                Enabled = Convert.ToBoolean(row.Cells["Enabled"].Value ?? false, CultureInfo.InvariantCulture),
                Parameters = row.Cells["Parameters"].Tag is Dictionary<string, string> parameters
                    ? new Dictionary<string, string>(parameters, StringComparer.Ordinal)
                    : new Dictionary<string, string>(StringComparer.Ordinal),
            });
        }
        return config;
    }

    private void NumpadModeChanged()
    {
        CancelCapture();
        if (_loading) return;
        foreach (DataGridViewRow row in _cheats.Rows)
        {
            var cheat = (Cheat)row.Tag!;
            SetKeyCell(row, cheat.DefaultKeyFor(_numpad.Checked));
            row.Cells["Enabled"].Value = cheat.DefaultEnabledFor(_numpad.Checked);
        }
    }

    private void ResetCheatsToDefaults()
    {
        CancelCapture();
        foreach (DataGridViewRow row in _cheats.Rows)
        {
            var cheat = (Cheat)row.Tag!;
            row.Cells["Enabled"].Value = cheat.DefaultEnabledFor(_numpad.Checked);
            SetKeyCell(row, cheat.DefaultKeyFor(_numpad.Checked));
            var defaultParams = cheat.Defaults().ToDictionary(
                kvp => kvp.Key,
                kvp => Convert.ToString(kvp.Value, CultureInfo.InvariantCulture) ?? string.Empty,
                StringComparer.Ordinal);
            SetParameterCell(row, defaultParams);
        }
    }

    /// <summary>設定按鍵儲存格：Tag 放 scdebug id（權威值），Value 放目前模式下要顯示的鍵名。</summary>
    private void SetKeyCell(DataGridViewRow row, string id)
    {
        row.Cells["Key"].Tag = id;
        row.Cells["Key"].Value = KeyMap.Display(id, _numpad.Checked);
    }

    /// <summary>設定參數儲存格：Tag 放參數字典，Value 放人類易讀的摘要字串，設定按鈕放按鈕文字。</summary>
    private void SetParameterCell(DataGridViewRow row, Dictionary<string, string> parameters)
    {
        var cheat = (Cheat)row.Tag!;
        row.Cells["Parameters"].Tag = parameters;
        row.Cells["Parameters"].Value = FormatSummary(cheat, parameters);
        bool hasVisibleParams = cheat.Parameters.Any(p => !p.Hidden);
        row.Cells["ConfigParams"].Value = hasVisibleParams ? Strings.Get("Gui_Trainer_Configure") : string.Empty;
    }

    /// <summary>
    /// 按鍵欄改成「點一下、直接按鍵盤設定」；參數欄與設定按鈕點一下開啟圖形對話框。
    /// </summary>
    private void CheatsCellClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0) return;
        if (e.ColumnIndex == _cheats.Columns["Key"]!.Index)
        {
            BeginCapture(e.RowIndex);
        }
        else if (e.ColumnIndex == _cheats.Columns["ClearKey"]!.Index)
        {
            ClearKey(e.RowIndex);
        }
        else if (e.ColumnIndex == _cheats.Columns["ConfigParams"]!.Index || e.ColumnIndex == _cheats.Columns["Parameters"]!.Index)
        {
            CancelCapture();
            OpenParameterDialog(e.RowIndex);
        }
        else
        {
            CancelCapture();
        }
    }

    private void OpenParameterDialog(int rowIndex)
    {
        DataGridViewRow row = _cheats.Rows[rowIndex];
        var cheat = (Cheat)row.Tag!;
        if (!cheat.Parameters.Any(p => !p.Hidden)) return;

        var currentParams = row.Cells["Parameters"].Tag as Dictionary<string, string>;
        using var dialog = new CheatParamsDialog(cheat, currentParams);
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            SetParameterCell(row, new Dictionary<string, string>(dialog.ResultParameters, StringComparer.Ordinal));
            UpdateRiskBanner();
        }
    }

    /// <summary>清掉這一列指定的按鍵。停用的作弊留空沒差；已啟用的作弊留空會在套用時擋下來，要求先指定按鍵或取消啟用。</summary>
    private void ClearKey(int rowIndex)
    {
        CancelCapture();
        DataGridViewRow row = _cheats.Rows[rowIndex];
        row.Cells["Key"].Tag = null;
        row.Cells["Key"].Value = Strings.Get("Gui_Trainer_KeyUnset");
    }

    private void BeginCapture(int rowIndex)
    {
        if (_capturingRow == rowIndex) return;
        CancelCapture();
        _capturingRow = rowIndex;
        _cheats.Rows[rowIndex].Cells["Key"].Value = Strings.Get("Gui_Trainer_KeyCapturePrompt");
        _cheats.CurrentCell = _cheats.Rows[rowIndex].Cells["Key"];
        _cheats.IsCapturing = true;
        _cheats.Focus();
    }

    private void CancelCapture()
    {
        if (_capturingRow < 0) return;
        DataGridViewRow row = _cheats.Rows[_capturingRow];
        string? id = row.Cells["Key"].Tag as string;
        row.Cells["Key"].Value = id is null ? Strings.Get("Gui_Trainer_KeyUnset") : KeyMap.Display(id, _numpad.Checked);
        _capturingRow = -1;
        _cheats.IsCapturing = false;
    }

    private void OnKeyCaptured(Keys keyData)
    {
        if (_capturingRow < 0) return;
        Keys code = keyData & Keys.KeyCode;
        if (code == Keys.Escape) { CancelCapture(); return; }
        if (CaptureIgnoredKeys.Contains(code)) return; // 只是按下 Ctrl/Shift/Alt 本身，繼續等真正的按鍵

        DataGridViewRow row = _cheats.Rows[_capturingRow];
        string? id = KeyMap.IdFromVirtualKey((int)code, _numpad.Checked);
        if (id is null)
        {
            row.Cells["Key"].Value = Strings.Get("Gui_Trainer_KeyCaptureUnsupported", code.ToString());
            return;
        }

        DataGridViewRow? occupant = _cheats.Rows.Cast<DataGridViewRow>()
            .FirstOrDefault(r => r.Index != _capturingRow && (string?)r.Cells["Key"].Tag == id);
        if (occupant is not null)
        {
            row.Cells["Key"].Value = Strings.Get("Gui_Trainer_KeyCaptureConflict",
                KeyMap.Display(id, _numpad.Checked), DisplayCheatName((Cheat)occupant.Tag!));
            return;
        }

        SetKeyCell(row, id);
        _capturingRow = -1;
        _cheats.IsCapturing = false;
    }

    private void ResetTweaksToDefaults()
    {
        foreach (DataGridViewRow row in _tweaks.Rows)
            if (row.Tag is Tweak tweak) row.Cells["Value"].Value = FormatDecimal(tweak.Default);
        UpdateRiskBanner();
    }

    private TrainerConfig BuildCurrentLegacyTweakConfig()
    {
        var config = new TrainerConfig { Enabled = _enabled.Checked };
        foreach (DataGridViewRow row in _tweaks.Rows)
        {
            if (row.Tag is not Tweak tweak) continue;
            string raw = Convert.ToString(row.Cells["Value"].Value, CultureInfo.InvariantCulture) ?? string.Empty;
            config.Tweaks[tweak.Id] = TryParseDecimal(raw, out decimal value) ? value : tweak.Default;
        }
        return config;
    }

    private void ResetScopedRow(DataGridView grid, int rowIndex)
    {
        if (rowIndex < 0 || grid.Rows[rowIndex].Tag is not Tweak tweak) return;
        TrainerConfig legacy = BuildCurrentLegacyTweakConfig();
        foreach ((string scope, string column) in ScopeBindings(grid))
        {
            grid.Rows[rowIndex].Cells[column].Value = FormatDecimal(
                ScopedTweakPatch.GetScopedFallbackValue(legacy, tweak.Id, scope));
        }
        UpdateRiskBanner();
    }

    private void ResetAllScopedTweaks()
    {
        _loading = true;
        TrainerConfig legacy = BuildCurrentLegacyTweakConfig();
        foreach (DataGridView grid in new[] { _scopedSimple, _scopedSettlement })
        {
            foreach (DataGridViewRow row in grid.Rows)
            {
                if (row.Tag is not Tweak tweak) continue;
                foreach ((string scope, string column) in ScopeBindings(grid))
                {
                    row.Cells[column].Value = FormatDecimal(
                        ScopedTweakPatch.GetScopedFallbackValue(legacy, tweak.Id, scope));
                }
            }
        }
        _loading = false;
        UpdateRiskBanner();
    }

    private void ScopedGridCellClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (sender is not DataGridView grid || e.RowIndex < 0) return;
        if (e.ColumnIndex == grid.Columns["ResetRow"]!.Index)
            ResetScopedRow(grid, e.RowIndex);
    }

    private void CheatsCellToolTipTextNeeded(object? sender, DataGridViewCellToolTipTextNeededEventArgs e)
    {
        if (e.RowIndex >= 0 && _cheats.Rows[e.RowIndex].Tag is Cheat cheat)
            e.ToolTipText = Strings.IsChinese ? cheat.Description : cheat.Id;
    }

    private void TweaksCellToolTipTextNeeded(object? sender, DataGridViewCellToolTipTextNeededEventArgs e)
    {
        if (e.RowIndex >= 0 && _tweaks.Rows[e.RowIndex].Tag is Tweak tweak)
            e.ToolTipText = Strings.IsChinese ? tweak.Description : tweak.Id;
    }

    private void ScopedCellToolTipTextNeeded(object? sender, DataGridViewCellToolTipTextNeededEventArgs e)
    {
        if (sender is DataGridView grid && e.RowIndex >= 0 && grid.Rows[e.RowIndex].Tag is Tweak tweak)
            e.ToolTipText = Strings.IsChinese ? tweak.Description : tweak.Id;
    }

    private static string FormatSummary(Cheat cheat, IReadOnlyDictionary<string, string> parameters)
    {
        bool isZh = Strings.IsChinese;
        var visibleParams = cheat.Parameters.Where(p => !p.Hidden).ToList();
        if (visibleParams.Count == 0)
            return Strings.Get("Gui_Trainer_NoParams");

        long GetNum(string name, long def)
        {
            if (parameters.TryGetValue(name, out string? s) && long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out long v))
                return v;
            return def;
        }

        switch (cheat.Id)
        {
            case "population_boost":
                return Strings.Get("Gui_Trainer_Summary_Pop", GetNum("amount", 500).ToString("N0", CultureInfo.CurrentCulture));

            case "production_boost":
                return Strings.Get("Gui_Trainer_Summary_Prod", GetNum("rate", 200).ToString("N0", CultureInfo.CurrentCulture));

            case "heal_army":
                return Strings.Get("Gui_Trainer_Summary_Heal", GetNum("amount", 100000).ToString("N0", CultureInfo.CurrentCulture));

            case "buff_army":
                return Strings.Get("Gui_Trainer_Summary_Buff",
                    GetNum("attack", 50).ToString("N0", CultureInfo.CurrentCulture),
                    GetNum("defense", 50).ToString("N0", CultureInfo.CurrentCulture),
                    GetNum("health", 500).ToString("N0", CultureInfo.CurrentCulture));

            case "heal_buildings":
                return Strings.Get("Gui_Trainer_Summary_Repair", GetNum("amount", 100000).ToString("N0", CultureInfo.CurrentCulture));

            case "smite_enemies":
                return Strings.Get("Gui_Trainer_Summary_Damage", GetNum("damage", 9999).ToString("N0", CultureInfo.CurrentCulture));

            case Cheats.SetSelectedLevelId:
                return Strings.Get("Gui_Trainer_Summary_Level", GetNum("level", 100));

            case Cheats.SpawnUnitId:
                string rawUnits = parameters.TryGetValue("units", out string? u) ? u : Cheats.DefaultUnitList;
                var units = Cheats.ParseUnitList(rawUnits);
                long count = GetNum("count", 5);
                long level = GetNum("level", 1);
                string rawItems = parameters.TryGetValue("items", out string? it) ? it : string.Empty;
                var items = Cheats.ParseItemList(rawItems, Cheats.MaxItemListLength);

                string lvlStr = level > 1 ? $" · Lv.{level}" : "";
                string itemStr = items.Count > 0 ? (isZh ? $" · 攜帶 {items.Count} 件物品" : $" · {items.Count} items") : "";

                if (units.Count > 0)
                {
                    string sample = string.Join("、", units.Take(2).Select(unit => Cheats.GetUnitLabel(unit, !isZh))) + (units.Count > 2 ? "..." : "");
                    return Strings.Get("Gui_Trainer_Summary_Spawn", units.Count, sample, count) + lvlStr + itemStr;
                }
                return Strings.Get("Gui_Trainer_Summary_SpawnSimple", units.Count, count) + lvlStr + itemStr;

            case Cheats.SpawnItemId:
                string rawSpawnItems = parameters.TryGetValue("items", out string? sit) ? sit : Cheats.DefaultItemList;
                var spawnItems = Cheats.ParseItemList(rawSpawnItems, Cheats.MaxSwitchableItemListLength);
                long spawnItemCount = GetNum("count", 1);
                if (spawnItems.Count > 0)
                {
                    string sample = string.Join("、", spawnItems.Take(2).Select(item => Cheats.GetItemLabel(item, !isZh))) + (spawnItems.Count > 2 ? "..." : "");
                    return Strings.Get("Gui_Trainer_Summary_SpawnItem", spawnItems.Count, sample, spawnItemCount);
                }
                return Strings.Get("Gui_Trainer_Summary_SpawnItemSimple", spawnItems.Count, spawnItemCount);

            default:
                return string.Join("; ", visibleParams.Select(p =>
                {
                    string label = p.DisplayLabel(!isZh);
                    string val = parameters.TryGetValue(p.Name, out string? v) ? v : Convert.ToString(p.Default, CultureInfo.InvariantCulture) ?? "";
                    return $"{label}: {val}";
                }));
        }
    }

    private static bool TryParseDecimal(string text, out decimal value) =>
        decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out value) ||
        decimal.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out value);

    private static string FormatDecimal(decimal value) => value.ToString(CultureInfo.InvariantCulture);

    private static string DisplayCheatName(Cheat cheat) =>
        Strings.IsChinese ? cheat.Name : Humanize(cheat.Id);

    private static string DisplayTweakName(Tweak tweak) =>
        Strings.IsChinese ? tweak.Label : Humanize(tweak.Id);

    private static string DisplayGroup(string group) => Strings.IsChinese ? group : group switch
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

/// <summary>
/// 「按鍵擷取」用的 DataGridView：<see cref="IsCapturing"/> 開著的時候，任何按鍵
/// （包含方向鍵、Tab、F1、Esc 這些平常會被 DataGridView 自己吃掉去換格/翻頁的鍵）
/// 都會被攔下來，改成觸發 <see cref="KeyCaptured"/>，而不會真的移動選取格或編輯儲存格。
///
/// 攔截點選在 ProcessCmdKey：這是整個訊息鏈最早看到 WM_KEYDOWN／WM_SYSKEYDOWN 的地方，
/// 比 DataGridView 自己的方向鍵／編輯邏輯還早，是 WinForms 做「錄製快捷鍵」控制項的標準寫法。
/// </summary>
public sealed class KeyCaptureGrid : DataGridView
{
    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool IsCapturing { get; set; }

    public event Action<Keys>? KeyCaptured;

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (IsCapturing)
        {
            KeyCaptured?.Invoke(keyData);
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }
}
