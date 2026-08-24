using CKToolkit.Core.Common;
using CKToolkit.Core.Saves;
using CKToolkit.I18n;

namespace CKToolkit.Gui;

public sealed class SavePage : UserControl
{
    private static readonly Color Accent = Color.FromArgb(37, 99, 235);
    private static readonly Color Danger = Color.FromArgb(220, 38, 38);

    private readonly Label _hint = new();
    private readonly GroupBox _playerGroup = new();
    private readonly Label _profileLabel = new();
    private readonly ComboBox _profile = new();
    private readonly Label _playerNameLabel = new();
    private readonly TextBox _playerName = new();
    private readonly Label _colorLabel = new();
    private readonly ComboBox _color = new();
    private readonly Label _raceLabel = new();
    private readonly ComboBox _race = new();
    private readonly Label _games = new();
    private readonly Button _savePlayer = new();
    private readonly DataGridView _grid = new();
    private readonly DataGridViewTextBoxColumn _nameColumn = new();
    private readonly DataGridViewTextBoxColumn _modifiedColumn = new();
    private readonly DataGridViewTextBoxColumn _sizeColumn = new();
    private readonly DataGridViewTextBoxColumn _previewColumn = new();
    private readonly Button _refresh = new();
    private readonly Button _export = new();
    private readonly Button _import = new();
    private readonly Button _delete = new();
    private readonly Button _editStats = new();
    private readonly GroupBox _previewGroup = new();
    private readonly PictureBox _preview = new();
    private readonly Label _details = new();
    private readonly Label _status = new();

    private SaveCatalog? _catalog;
    private bool _refreshing;
    private bool _busy;
    private bool _gameRunning;

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Func<string?>? GameDirProvider { get; set; }

    public event Action<bool>? BusyChanged;
    public event Action<string>? LogMessage;

    public SavePage()
    {
        BackColor = Color.White;
        Padding = new Padding(18);
        BuildUi();
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            BackColor = Color.White
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _hint.AutoSize = true;
        _hint.MaximumSize = new Size(1000, 0);
        _hint.ForeColor = Color.FromArgb(71, 85, 105);
        _hint.Margin = new Padding(0, 0, 0, 10);
        root.Controls.Add(_hint, 0, 0);

        BuildPlayerGroup();
        root.Controls.Add(_playerGroup, 0, 1);

        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0, 10, 0, 8)
        };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 68F));
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32F));
        content.Controls.Add(BuildSaveList(), 0, 0);
        BuildPreviewGroup();
        _previewGroup.Margin = new Padding(10, 0, 0, 0);
        content.Controls.Add(_previewGroup, 1, 0);
        root.Controls.Add(content, 0, 2);

        _status.AutoSize = true;
        _status.ForeColor = Color.FromArgb(71, 85, 105);
        _status.Margin = new Padding(2, 2, 0, 0);
        root.Controls.Add(_status, 0, 3);
        Controls.Add(root);
    }

    private void BuildPlayerGroup()
    {
        _playerGroup.Dock = DockStyle.Top;
        _playerGroup.AutoSize = true;
        _playerGroup.Padding = new Padding(12, 8, 12, 12);
        _playerGroup.Margin = new Padding(0);

        var row = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 10,
            RowCount = 1
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        ConfigureFieldLabel(_profileLabel);
        ConfigureFieldLabel(_playerNameLabel);
        ConfigureFieldLabel(_colorLabel);
        ConfigureFieldLabel(_raceLabel);
        _profile.Dock = DockStyle.Fill;
        _profile.DropDownStyle = ComboBoxStyle.DropDownList;
        _profile.SelectedIndexChanged += (_, _) => { if (!_refreshing) LoadSelectedProfile(); };
        _playerName.Dock = DockStyle.Fill;
        _playerName.MaxLength = 32;
        _color.Dock = DockStyle.Fill;
        _color.DropDownStyle = ComboBoxStyle.DropDownList;
        _race.Dock = DockStyle.Fill;
        _race.DropDownStyle = ComboBoxStyle.DropDownList;
        _games.AutoSize = true;
        _games.Anchor = AnchorStyles.Left;
        _games.Margin = new Padding(10, 6, 10, 0);
        ConfigureToolbarButton(_savePlayer, Accent, Color.White);
        _savePlayer.Click += async (_, _) => await SavePlayerAsync();

        row.Controls.Add(_profileLabel, 0, 0);
        row.Controls.Add(_profile, 1, 0);
        row.Controls.Add(_playerNameLabel, 2, 0);
        row.Controls.Add(_playerName, 3, 0);
        row.Controls.Add(_colorLabel, 4, 0);
        row.Controls.Add(_color, 5, 0);
        row.Controls.Add(_raceLabel, 6, 0);
        row.Controls.Add(_race, 7, 0);
        row.Controls.Add(_games, 8, 0);
        row.Controls.Add(_savePlayer, 9, 0);
        _playerGroup.Controls.Add(row);
    }

    private Control BuildSaveList()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Color.White
        };
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _grid.Dock = DockStyle.Fill;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AllowUserToResizeRows = false;
        _grid.AutoGenerateColumns = false;
        _grid.BackgroundColor = Color.White;
        _grid.BorderStyle = BorderStyle.FixedSingle;
        _grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        _grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
        _grid.MultiSelect = false;
        _grid.ReadOnly = true;
        _grid.RowHeadersVisible = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.SelectionChanged += (_, _) => ShowSelectedSave();

        _nameColumn.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        _nameColumn.FillWeight = 42;
        _modifiedColumn.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        _modifiedColumn.FillWeight = 38;
        _sizeColumn.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        _sizeColumn.FillWeight = 20;
        _previewColumn.AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells;
        _grid.Columns.AddRange(_nameColumn, _modifiedColumn, _sizeColumn, _previewColumn);
        panel.Controls.Add(_grid, 0, 0);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            Padding = new Padding(0, 8, 0, 0)
        };
        ConfigureToolbarButton(_refresh, Color.White, Color.FromArgb(71, 85, 105));
        ConfigureToolbarButton(_export, Color.White, Accent);
        ConfigureToolbarButton(_import, Accent, Color.White);
        ConfigureToolbarButton(_delete, Color.White, Danger);
        ConfigureToolbarButton(_editStats, Color.White, Color.FromArgb(124, 58, 237));
        _refresh.Click += (_, _) => RefreshCatalog();
        _export.Click += async (_, _) => await ExportAsync();
        _import.Click += async (_, _) => await ImportAsync();
        _delete.Click += async (_, _) => await DeleteAsync();
        _editStats.Click += async (_, _) => await EditStatisticsAsync();
        actions.Controls.AddRange([_refresh, _export, _import, _delete, _editStats]);
        panel.Controls.Add(actions, 0, 1);
        return panel;
    }

    private void BuildPreviewGroup()
    {
        _previewGroup.Dock = DockStyle.Fill;
        _previewGroup.Padding = new Padding(10);
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2
        };
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _preview.Dock = DockStyle.Fill;
        _preview.BackColor = Color.FromArgb(15, 23, 42);
        _preview.SizeMode = PictureBoxSizeMode.Zoom;
        _details.AutoSize = true;
        _details.MaximumSize = new Size(320, 0);
        _details.ForeColor = Color.FromArgb(71, 85, 105);
        _details.Margin = new Padding(0, 8, 0, 0);
        panel.Controls.Add(_preview, 0, 0);
        panel.Controls.Add(_details, 0, 1);
        _previewGroup.Controls.Add(panel);
    }

    public void ApplyLanguage()
    {
        _hint.Text = Strings.Get("Gui_Save_Hint");
        _playerGroup.Text = Strings.Get("Gui_Save_PlayerGroup");
        _profileLabel.Text = Strings.Get("Gui_Save_Profile");
        _playerNameLabel.Text = Strings.Get("Gui_Save_PlayerName");
        _colorLabel.Text = Strings.Get("Gui_Save_Color");
        _raceLabel.Text = Strings.Get("Gui_Save_Race");
        _savePlayer.Text = Strings.Get("Gui_Save_SavePlayer");
        _previewGroup.Text = Strings.Get("Gui_Save_Preview");
        _nameColumn.HeaderText = Strings.Get("Gui_Save_ColumnName");
        _modifiedColumn.HeaderText = Strings.Get("Gui_Save_ColumnModified");
        _sizeColumn.HeaderText = Strings.Get("Gui_Save_ColumnSize");
        _previewColumn.HeaderText = Strings.Get("Gui_Save_ColumnPreview");
        _refresh.Text = Strings.Get("Gui_Save_Refresh");
        _export.Text = Strings.Get("Gui_Save_Export");
        _import.Text = Strings.Get("Gui_Save_Import");
        _delete.Text = Strings.Get("Gui_Save_Delete");
        _editStats.Text = Strings.Get("Gui_Save_EditStatistics");

        int colorId = (_color.SelectedItem as OptionChoice)?.Id ?? 0;
        _color.Items.Clear();
        for (int i = 0; i < 8; i++)
            _color.Items.Add(new OptionChoice(i, Strings.Get("Gui_Save_Color" + i)));
        SelectOption(_color, colorId);

        int raceId = (_race.SelectedItem as OptionChoice)?.Id ?? 0;
        _race.Items.Clear();
        _race.Items.Add(new OptionChoice(0, Strings.Get("Gui_Save_RaceGaul")));
        _race.Items.Add(new OptionChoice(1, Strings.Get("Gui_Save_RaceRoman")));
        _race.Items.Add(new OptionChoice(2, Strings.Get("Gui_Save_RaceRandom")));
        SelectOption(_race, raceId);

        if (_catalog is null) _status.Text = Strings.Get("Gui_Save_NotLoaded");
        else SetStatus(_gameRunning
            ? Strings.Get("Gui_Save_GameRunningReadOnly", _catalog.Profiles.Count, _catalog.SaveCount)
            : Strings.Get("Gui_Save_Count", _catalog.Profiles.Count, _catalog.SaveCount));
        ShowSelectedSave();
    }

    public void RefreshCatalog()
    {
        if (_busy) return;
        string? gameDir = GameDirProvider?.Invoke()?.Trim();
        if (string.IsNullOrWhiteSpace(gameDir) || !GamePaths.IsGameDir(gameDir))
        {
            ClearCatalog();
            SetStatus(Strings.Get("Error_GameNotFound"), isError: true);
            return;
        }

        string? previousProfile = (_profile.SelectedItem as ProfileChoice)?.Name;
        Result<SaveCatalog> result = SaveManager.Inspect(gameDir);
        if (!result.Success || result.Value is null)
        {
            ClearCatalog();
            SetStatus(result.ErrorMessage ?? Strings.Get("Save_Error_Inspect", Strings.Get("Save_Error_UnknownDetail")), isError: true);
            return;
        }

        _catalog = result.Value;
        _gameRunning = GamePaths.IsGameRunning(gameDir);
        _refreshing = true;
        try
        {
            _profile.Items.Clear();
            foreach (SaveProfileInfo profile in _catalog.Profiles)
                _profile.Items.Add(new ProfileChoice(profile.Name, profile.IsDefault));

            string? wanted = previousProfile ?? _catalog.DefaultProfile;
            int index = Enumerable.Range(0, _profile.Items.Count)
                .FirstOrDefault(i => (_profile.Items[i] as ProfileChoice)?.Name.Equals(wanted, StringComparison.OrdinalIgnoreCase) == true, -1);
            if (_profile.Items.Count > 0) _profile.SelectedIndex = index >= 0 ? index : 0;
        }
        finally
        {
            _refreshing = false;
        }
        LoadSelectedProfile();
        SetStatus(_gameRunning
            ? Strings.Get("Gui_Save_GameRunningReadOnly", _catalog.Profiles.Count, _catalog.SaveCount)
            : Strings.Get("Gui_Save_Count", _catalog.Profiles.Count, _catalog.SaveCount));
    }

    private void LoadSelectedProfile()
    {
        ClearPreview();
        _grid.Rows.Clear();
        if (_catalog is null || _profile.SelectedItem is not ProfileChoice choice)
        {
            SetPlayerControls(null);
            RefreshButtons();
            return;
        }

        SaveProfileInfo? profile = _catalog.Profiles.FirstOrDefault(p => p.Name.Equals(choice.Name, StringComparison.OrdinalIgnoreCase));
        if (profile is null)
        {
            SetPlayerControls(null);
            RefreshButtons();
            return;
        }

        foreach (GameSaveInfo save in profile.Saves)
        {
            int rowIndex = _grid.Rows.Add(
                Path.GetFileNameWithoutExtension(save.FileName),
                save.LastWriteTimeUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
                FormatBytes(save.SizeBytes),
                save.HasScreenshot ? Strings.Get("Gui_Save_Yes") : Strings.Get("Gui_Save_No"));
            _grid.Rows[rowIndex].Tag = save;
        }
        if (_grid.Rows.Count > 0) _grid.Rows[0].Selected = true;

        string? gameDir = GameDirProvider?.Invoke()?.Trim();
        if (!string.IsNullOrWhiteSpace(gameDir))
        {
            Result<PlayerProfileData> player = SaveManager.GetPlayerProfile(gameDir, profile.Name);
            SetPlayerControls(player.Success ? player.Value : null);
            if (!player.Success && player.ErrorMessage is not null) LogMessage?.Invoke(player.ErrorMessage);
        }
        RefreshButtons();
    }

    private void SetPlayerControls(PlayerProfileData? player)
    {
        _playerName.Text = player?.DisplayName ?? string.Empty;
        SelectOption(_color, player?.Color ?? 0);
        SelectOption(_race, player?.Race ?? 0);
        _games.Text = player is null ? string.Empty : Strings.Get("Gui_Save_Games", player.Games);
        _playerName.Enabled = player is not null && !_busy && !_gameRunning;
        _color.Enabled = player is not null && !_busy && !_gameRunning;
        _race.Enabled = player is not null && !_busy && !_gameRunning;
        _savePlayer.Enabled = player is not null && !_busy && !_gameRunning;
    }

    private void ShowSelectedSave()
    {
        GameSaveInfo? save = SelectedSave;
        ClearPreview();
        if (save is null)
        {
            _details.Text = Strings.Get("Gui_Save_NoSelection");
            RefreshButtons();
            return;
        }

        _details.Text = Strings.Get(
            "Gui_Save_Details",
            save.Profile,
            save.FileName,
            save.LastWriteTimeUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"),
            FormatBytes(save.SizeBytes));

        if (save.ScreenshotPath is not null)
        {
            try
            {
                using var stream = new FileStream(save.ScreenshotPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using Image source = Image.FromStream(stream);
                _preview.Image = new Bitmap(source);
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(Strings.Get("Save_Error_Preview", ex.Message));
            }
        }
        RefreshButtons();
    }

    private async Task SavePlayerAsync()
    {
        if (_busy || _profile.SelectedItem is not ProfileChoice profile ||
            _color.SelectedItem is not OptionChoice color || _race.SelectedItem is not OptionChoice race)
            return;
        string? gameDir = GameDirProvider?.Invoke()?.Trim();
        if (string.IsNullOrWhiteSpace(gameDir)) return;
        string playerName = _playerName.Text;

        SetBusy(true);
        try
        {
            Result<PlayerProfileData> result = await Task.Run(() => SaveManager.UpdatePlayerProfile(
                gameDir,
                profile.Name,
                new PlayerProfileUpdate(playerName, color.Id, race.Id)));
            if (!result.Success || result.Value is null)
            {
                ShowError(result.ErrorMessage ?? Strings.Get("Save_Error_PlayerWrite", Strings.Get("Save_Error_UnknownDetail")));
                return;
            }
            SetPlayerControls(result.Value);
            SetStatus(Strings.Get("Gui_Save_PlayerUpdated", result.Value.DisplayName));
            LogMessage?.Invoke(Strings.Get("Gui_Save_PlayerUpdated", result.Value.DisplayName));
        }
        finally { SetBusy(false); }
    }

    private async Task ExportAsync()
    {
        GameSaveInfo? save = SelectedSave;
        if (_busy || save is null) return;
        string? gameDir = GameDirProvider?.Invoke()?.Trim();
        if (string.IsNullOrWhiteSpace(gameDir)) return;

        using var dialog = new SaveFileDialog
        {
            Title = Strings.Get("Gui_Save_ExportTitle"),
            Filter = Strings.Get("Gui_Save_ArchiveFilter"),
            DefaultExt = "cksave",
            AddExtension = true,
            OverwritePrompt = true,
            FileName = $"{save.Profile}-{Path.GetFileNameWithoutExtension(save.FileName)}-{DateTime.Now:yyyyMMdd-HHmmss}.cksave"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        SetBusy(true);
        try
        {
            Result<SaveExportResult> result = await Task.Run(() => SaveManager.ExportSave(
                gameDir, save.Profile, save.FileName, dialog.FileName, overwrite: true));
            if (!result.Success || result.Value is null)
            {
                ShowError(result.ErrorMessage ?? Strings.Get("Save_Error_Export", Strings.Get("Save_Error_UnknownDetail")));
                return;
            }
            SetStatus(Strings.Get("Gui_Save_Exported", result.Value.ArchivePath));
            LogMessage?.Invoke(Strings.Get("Gui_Save_Exported", result.Value.ArchivePath));
        }
        finally { SetBusy(false); }
    }

    private async Task ImportAsync()
    {
        if (_busy || _profile.SelectedItem is not ProfileChoice profile) return;
        string? gameDir = GameDirProvider?.Invoke()?.Trim();
        if (string.IsNullOrWhiteSpace(gameDir)) return;
        using var dialog = new OpenFileDialog
        {
            Title = Strings.Get("Gui_Save_ImportTitle"),
            Filter = Strings.Get("Gui_Save_ArchiveFilter"),
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        SetBusy(true);
        try
        {
            Result<SaveImportResult> result = await Task.Run(() => SaveManager.ImportSave(gameDir, profile.Name, dialog.FileName));
            if (!result.Success || result.Value is null)
            {
                ShowError(result.ErrorMessage ?? Strings.Get("Save_Error_Import", Strings.Get("Save_Error_UnknownDetail")));
                return;
            }
            LogMessage?.Invoke(Strings.Get("Gui_Save_Imported", result.Value.SaveFileName, result.Value.Profile));
        }
        finally { SetBusy(false); }
        RefreshCatalog();
    }

    private async Task DeleteAsync()
    {
        GameSaveInfo? save = SelectedSave;
        if (_busy || save is null) return;
        if (MessageBox.Show(
                this,
                Strings.Get("Gui_Save_DeleteConfirm", save.FileName),
                Strings.Get("Gui_Save_Delete"),
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return;
        string? gameDir = GameDirProvider?.Invoke()?.Trim();
        if (string.IsNullOrWhiteSpace(gameDir)) return;

        SaveDeleteResult? deleted = null;
        SetBusy(true);
        try
        {
            Result<SaveDeleteResult> result = await Task.Run(() => SaveManager.DeleteSave(gameDir, save.Profile, save.FileName));
            if (!result.Success || result.Value is null)
            {
                ShowError(result.ErrorMessage ?? Strings.Get("Save_Error_Delete", Strings.Get("Save_Error_UnknownDetail")));
                return;
            }
            deleted = result.Value;
            LogMessage?.Invoke(Strings.Get("Gui_Save_Deleted", deleted.RecoveryArchivePath));
        }
        finally { SetBusy(false); }
        RefreshCatalog();
        if (deleted is not null)
            MessageBox.Show(this, Strings.Get("Gui_Save_Deleted", deleted.RecoveryArchivePath), Strings.Get("Gui_Save_Delete"), MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private async Task EditStatisticsAsync()
    {
        if (_busy || _profile.SelectedItem is not ProfileChoice profile) return;
        string? gameDir = GameDirProvider?.Invoke()?.Trim();
        if (string.IsNullOrWhiteSpace(gameDir)) return;

        Result<PlayerStatisticsSummary> current = PlayerStatistics.Load(gameDir, profile.Name);
        if (!current.Success || current.Value is null)
        {
            ShowError(current.ErrorMessage ?? Strings.Get("Save_Error_StatisticsRead", Strings.Get("Save_Error_UnknownDetail")));
            return;
        }

        using var dialog = new PlayerStatisticsDialog(current.Value);
        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.ProposedUpdate is null) return;
        if (MessageBox.Show(
                this,
                Strings.Get("Gui_Save_Stats_Confirm"),
                Strings.Get("Gui_Save_EditStatistics"),
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return;

        SetBusy(true);
        try
        {
            Result<PlayerStatisticsSummary> result = await Task.Run(() =>
                PlayerStatistics.Update(gameDir, profile.Name, dialog.ProposedUpdate));
            if (!result.Success || result.Value is null)
            {
                ShowError(result.ErrorMessage ?? Strings.Get("Save_Error_StatisticsWrite", Strings.Get("Save_Error_UnknownDetail")));
                return;
            }
            foreach (string warning in result.Warnings) LogMessage?.Invoke(warning);
            string message = Strings.Get("Gui_Save_Stats_Updated", result.Value.GameCount, result.Value.MilitaryRating);
            SetStatus(message);
            LogMessage?.Invoke(message);
        }
        finally { SetBusy(false); }
        RefreshCatalog();
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        UseWaitCursor = busy;
        BusyChanged?.Invoke(busy);
        RefreshButtons();
    }

    private void RefreshButtons()
    {
        bool hasProfile = _profile.SelectedItem is ProfileChoice;
        bool hasSave = SelectedSave is not null;
        _profile.Enabled = !_busy;
        _refresh.Enabled = !_busy;
        _import.Enabled = !_busy && !_gameRunning && hasProfile;
        _export.Enabled = !_busy && !_gameRunning && hasSave;
        _delete.Enabled = !_busy && !_gameRunning && hasSave;
        _editStats.Enabled = !_busy && !_gameRunning && hasProfile;
        _grid.Enabled = !_busy;
        _savePlayer.Enabled = !_busy && !_gameRunning && hasProfile && !string.IsNullOrWhiteSpace(_playerName.Text);
    }

    private void ClearCatalog()
    {
        _catalog = null;
        _gameRunning = false;
        _refreshing = true;
        try { _profile.Items.Clear(); }
        finally { _refreshing = false; }
        _grid.Rows.Clear();
        SetPlayerControls(null);
        ClearPreview();
        RefreshButtons();
    }

    private void ClearPreview()
    {
        Image? old = _preview.Image;
        _preview.Image = null;
        old?.Dispose();
    }

    private void ShowError(string message)
    {
        SetStatus(message, isError: true);
        LogMessage?.Invoke(message);
        MessageBox.Show(this, message, Strings.Get("Gui_ErrorTitle"), MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    private void SetStatus(string message, bool isError = false)
    {
        _status.Text = message;
        _status.ForeColor = isError ? Danger : Color.FromArgb(71, 85, 105);
    }

    private GameSaveInfo? SelectedSave => _grid.SelectedRows.Count == 1
        ? _grid.SelectedRows[0].Tag as GameSaveInfo
        : null;

    private static void ConfigureFieldLabel(Label label)
    {
        label.AutoSize = true;
        label.Anchor = AnchorStyles.Left;
        label.Margin = new Padding(0, 6, 8, 0);
    }

    private static void ConfigureToolbarButton(Button button, Color back, Color fore)
    {
        button.AutoSize = true;
        button.MinimumSize = new Size(105, 34);
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderColor = fore == Color.White ? back : Color.FromArgb(203, 213, 225);
        button.BackColor = back;
        button.ForeColor = fore;
        button.Font = new Font("Microsoft JhengHei UI", 9F, FontStyle.Bold);
        button.Margin = new Padding(0, 0, 8, 0);
    }

    private static void SelectOption(ComboBox combo, int id)
    {
        int index = Enumerable.Range(0, combo.Items.Count)
            .FirstOrDefault(i => (combo.Items[i] as OptionChoice)?.Id == id, -1);
        if (combo.Items.Count > 0) combo.SelectedIndex = index >= 0 ? index : 0;
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1024 * 1024) return $"{bytes / 1024d / 1024d:F1} MB";
        if (bytes >= 1024) return $"{bytes / 1024d:F1} KB";
        return $"{bytes} B";
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) ClearPreview();
        base.Dispose(disposing);
    }

    private sealed record ProfileChoice(string Name, bool IsDefault)
    {
        public override string ToString() => IsDefault ? $"{Name} ★" : Name;
    }

    private sealed record OptionChoice(int Id, string Label)
    {
        public override string ToString() => Label;
    }
}
