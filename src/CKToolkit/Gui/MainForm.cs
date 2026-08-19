using CKToolkit.Core.Common;
using CKToolkit.Core.Runtime;
using CKToolkit.I18n;

namespace CKToolkit.Gui;

/// <summary>單一 GUI 入口；所有遊戲檔寫入都經由 PatchPipeline。</summary>
public sealed class MainForm : Form
{
    private static readonly Color Accent = Color.FromArgb(37, 99, 235);
    private static readonly Color Success = Color.FromArgb(22, 163, 74);
    private static readonly Color Danger = Color.FromArgb(220, 38, 38);
    private static readonly Color Surface = Color.FromArgb(248, 250, 252);

    private ToolkitConfig _config;
    private readonly PatchPipeline _pipeline = PatchPipeline.CreateDefault();
    private bool _busy;
    private bool _initialising = true;

    private readonly Label _title = new();
    private readonly Label _subtitle = new();
    private readonly Label _pathLabel = new();
    private readonly TextBox _gamePath = new();
    private readonly Button _browse = new();
    private readonly Label _pathStatus = new();
    private readonly ComboBox _uiLanguage = new();
    private readonly TabControl _tabs = new();
    private readonly TabPage _perfTab = new();
    private readonly TabPage _langTab = new();
    private readonly TabPage _trainerTab = new();
    private readonly TabPage _profilerTab = new();
    private readonly TabPage _aboutTab = new();
    private readonly PerformancePage _performancePage = new();
    private readonly LanguagePage _languagePage = new();
    private readonly TrainerPage _trainerPage = new();
    private readonly ProfilerPage _profilerPage = new();
    private readonly AboutPage _aboutPage = new();
    private readonly Button _apply = new();
    private readonly Button _restore = new();
    private readonly Button _check = new();
    private readonly Button _diagLaunch = new();
    private readonly Button _diagAttach = new();
    private readonly Label _diagHint = new();
    private readonly Label _operationStatus = new();
    private readonly TextBox _log = new();

    public MainForm()
    {
        _config = ToolkitConfig.Load();
        Strings.Language = _config.UiLanguage;
        InitializeComponent();
        LoadConfigurationIntoControls();
        ApplyLanguage();
        _initialising = false;
        Shown += (_, _) => InitialiseGamePath();
        FormClosing += (_, _) => PersistCurrentUiSilently();
    }

    private void InitializeComponent()
    {
        AutoScaleMode = AutoScaleMode.Dpi;
        MinimumSize = new Size(980, 720);
        Size = new Size(1180, 860);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Surface;
        Font = new Font("Microsoft JhengHei UI", 9F);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3,
            BackColor = Surface, Padding = new Padding(16)
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 190F));
        root.Controls.Add(BuildHeader(), 0, 0);
        root.Controls.Add(BuildTabs(), 0, 1);
        root.Controls.Add(BuildBottomArea(), 0, 2);
        Controls.Add(root);
        AcceptButton = _apply;
    }

    private Control BuildHeader()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Top, AutoSize = true, ColumnCount = 5, RowCount = 3,
            BackColor = Color.White, Padding = new Padding(18, 14, 18, 14),
            Margin = new Padding(0, 0, 0, 12)
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        _title.AutoSize = true;
        _title.Font = new Font(Font.FontFamily, 17F, FontStyle.Bold);
        _title.ForeColor = Color.FromArgb(15, 23, 42);
        panel.Controls.Add(_title, 0, 0);
        panel.SetColumnSpan(_title, 3);

        _subtitle.AutoSize = true;
        _subtitle.ForeColor = Color.FromArgb(71, 85, 105);
        _subtitle.Margin = new Padding(0, 2, 0, 12);
        panel.Controls.Add(_subtitle, 0, 1);
        panel.SetColumnSpan(_subtitle, 3);

        _uiLanguage.DropDownStyle = ComboBoxStyle.DropDownList;
        _uiLanguage.Width = 96;
        _uiLanguage.Items.AddRange([new LanguageChoice("zh-TW", "中文"), new LanguageChoice("en", "EN")]);
        _uiLanguage.SelectedIndexChanged += (_, _) => ChangeUiLanguage();
        panel.Controls.Add(_uiLanguage, 4, 0);

        _pathLabel.AutoSize = true;
        _pathLabel.Anchor = AnchorStyles.Left;
        _pathLabel.Margin = new Padding(0, 5, 10, 0);
        panel.Controls.Add(_pathLabel, 0, 2);

        _gamePath.Dock = DockStyle.Fill;
        _gamePath.Margin = new Padding(0, 2, 8, 0);
        _gamePath.TextChanged += (_, _) => RefreshPathStatus();
        panel.Controls.Add(_gamePath, 1, 2);

        _browse.AutoSize = true;
        _browse.Margin = new Padding(0, 1, 8, 0);
        _browse.Click += (_, _) => BrowseGameDirectory();
        panel.Controls.Add(_browse, 2, 2);

        _pathStatus.AutoSize = true;
        _pathStatus.Anchor = AnchorStyles.Left;
        _pathStatus.Margin = new Padding(0, 5, 0, 0);
        panel.Controls.Add(_pathStatus, 3, 2);
        panel.SetColumnSpan(_pathStatus, 2);
        return panel;
    }

    private Control BuildTabs()
    {
        _tabs.Dock = DockStyle.Fill;
        _tabs.Padding = new Point(18, 7);
        _tabs.Controls.AddRange([_perfTab, _langTab, _trainerTab, _profilerTab, _aboutTab]);
        _performancePage.Dock = DockStyle.Fill;
        _languagePage.Dock = DockStyle.Fill;
        _trainerPage.Dock = DockStyle.Fill;
        _profilerPage.Dock = DockStyle.Fill;
        _aboutPage.Dock = DockStyle.Fill;
        _perfTab.Controls.Add(_performancePage);
        _langTab.Controls.Add(_languagePage);
        _trainerTab.Controls.Add(_trainerPage);
        _profilerTab.Controls.Add(_profilerPage);
        _aboutTab.Controls.Add(_aboutPage);
        _profilerPage.BusyChanged += busy => SetBusy(busy, profilerOwnsBusy: true);
        _profilerPage.LogMessage += message => AppendLog(message);
        return _tabs;
    }

    private Control BuildBottomArea()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2,
            Margin = new Padding(0, 12, 0, 0)
        };
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false, Padding = new Padding(0, 0, 0, 8)
        };
        ConfigureActionButton(_apply, Accent, Color.White);
        ConfigureActionButton(_restore, Color.White, Danger);
        ConfigureActionButton(_check, Color.White, Color.FromArgb(51, 65, 85));
        _apply.Click += async (_, _) => await ApplyAsync();
        _restore.Click += async (_, _) => await RestoreAsync();
        _check.Click += async (_, _) => await CheckAsync();
        _operationStatus.AutoSize = true;
        _operationStatus.Anchor = AnchorStyles.Left;
        _operationStatus.Margin = new Padding(18, 10, 0, 0);
        _operationStatus.ForeColor = Color.FromArgb(71, 85, 105);
        actions.Controls.AddRange([_apply, _restore, _check, _operationStatus]);
        panel.Controls.Add(actions, 0, 0);

        // 診斷這一列刻意獨立於「一鍵套用 / 還原原版」之外：它不寫任何遊戲檔案，
        // 跟上面那一排的性質完全不同，混在一起會讓人以為按了會改東西。
        var diagnostics = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false, Padding = new Padding(0, 0, 0, 8)
        };
        ConfigureActionButton(_diagLaunch, Color.White, Color.FromArgb(21, 94, 117));
        ConfigureActionButton(_diagAttach, Color.White, Color.FromArgb(21, 94, 117));
        _diagLaunch.Click += async (_, _) => await LaunchWithDiagnosticsAsync(attachToRunning: false);
        _diagAttach.Click += async (_, _) => await LaunchWithDiagnosticsAsync(attachToRunning: true);
        _diagHint.AutoSize = true;
        _diagHint.Anchor = AnchorStyles.Left;
        _diagHint.Margin = new Padding(18, 10, 0, 0);
        _diagHint.ForeColor = Color.FromArgb(100, 116, 139);
        diagnostics.Controls.AddRange([_diagLaunch, _diagAttach, _diagHint]);
        panel.RowStyles.Insert(1, new RowStyle(SizeType.AutoSize));
        panel.RowCount = 3;
        panel.Controls.Add(diagnostics, 0, 1);

        _log.Dock = DockStyle.Fill;
        _log.Multiline = true;
        _log.ReadOnly = true;
        _log.ScrollBars = ScrollBars.Vertical;
        _log.BackColor = Color.FromArgb(15, 23, 42);
        _log.ForeColor = Color.FromArgb(226, 232, 240);
        _log.Font = new Font("Cascadia Mono", 8.5F);
        _log.BorderStyle = BorderStyle.None;
        _log.WordWrap = false;
        panel.Controls.Add(_log, 0, 2);
        return panel;
    }

    private static void ConfigureActionButton(Button button, Color back, Color fore)
    {
        button.AutoSize = true;
        button.MinimumSize = new Size(120, 38);
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderColor = fore == Color.White ? back : Color.FromArgb(203, 213, 225);
        button.BackColor = back;
        button.ForeColor = fore;
        button.Font = new Font(button.Font, FontStyle.Bold);
        button.Margin = new Padding(0, 0, 8, 0);
    }

    private void LoadConfigurationIntoControls()
    {
        _gamePath.Text = _config.GameDir ?? string.Empty;
        _performancePage.LoadConfig(_config.Perf);
        _languagePage.LoadConfig(_config.Lang);
        _trainerPage.LoadConfig(_config.Trainer);
        _uiLanguage.SelectedIndex = Strings.EffectiveLanguage == "zh-TW" ? 0 : 1;
    }

    private void InitialiseGamePath()
    {
        if (_config.LoadError is not null) AppendLog(_config.LoadError);
        foreach (string migration in _config.MigrationsApplied) AppendLog(migration);
        string? detected = GamePaths.FindGameDir(rememberedDir: _gamePath.Text);
        if (detected is not null && !string.Equals(_gamePath.Text, detected, StringComparison.OrdinalIgnoreCase))
        {
            _gamePath.Text = detected;
            AppendLog(Strings.Get("Gui_Log_AutoDetected", detected));
        }
        RefreshPathStatus();
        AppendLog(Strings.Get("Gui_Log_Ready"));
    }

    private void ApplyLanguage()
    {
        Text = Strings.Get("Gui_WindowTitle");
        _title.Text = Strings.Get("AppTitle");
        _subtitle.Text = Strings.Get("AppDescription");
        _pathLabel.Text = Strings.Get("Gui_GamePath");
        _browse.Text = Strings.Get("Gui_Browse");
        _perfTab.Text = Strings.Get("Gui_Tab_Performance");
        _langTab.Text = Strings.Get("Gui_Tab_Language");
        _trainerTab.Text = Strings.Get("Gui_Tab_Trainer");
        _profilerTab.Text = Strings.Get("Gui_Tab_Profiler");
        _aboutTab.Text = Strings.Get("Gui_Tab_About");
        _apply.Text = Strings.Get("Gui_Apply");
        _restore.Text = Strings.Get("Gui_Restore");
        _check.Text = Strings.Get("Gui_Check");
        _diagLaunch.Text = Strings.Get("Gui_Diag_Launch");
        _diagAttach.Text = Strings.Get("Gui_Diag_Attach");
        _diagHint.Text = Strings.Get("Gui_Diag_Hint");
        _operationStatus.Text = _busy ? Strings.Get("Gui_Working") : Strings.Get("Gui_Ready");
        _performancePage.ApplyLanguage();
        _languagePage.ApplyLanguage();
        _trainerPage.ApplyLanguage();
        _profilerPage.ApplyLanguage();
        _aboutPage.ApplyLanguage();
        RefreshPathStatus();
    }

    private void ChangeUiLanguage()
    {
        if (_initialising || _uiLanguage.SelectedItem is not LanguageChoice choice) return;
        Strings.Language = choice.Code;
        _config.UiLanguage = choice.Code;
        ApplyLanguage();
        PersistCurrentUiSilently();
    }

    private void BrowseGameDirectory()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = Strings.Get("Gui_SelectGameFolder"), UseDescriptionForTitle = true,
            SelectedPath = Directory.Exists(_gamePath.Text) ? _gamePath.Text : string.Empty,
            ShowNewFolderButton = false
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _gamePath.Text = dialog.SelectedPath;
            PersistCurrentUiSilently();
        }
    }

    private void RefreshPathStatus()
    {
        bool valid = GamePaths.IsGameDir(_gamePath.Text.Trim());
        _pathStatus.Text = "● " + Strings.Get(valid ? "Gui_GameFound" : "Gui_GameMissing");
        _pathStatus.ForeColor = valid ? Success : Danger;
    }

    private ToolkitConfig SnapshotConfiguration()
    {
        var snapshot = ToolkitConfig.FromJson(_config.ToJson());
        snapshot.LoadError = null;
        snapshot.GameDir = _gamePath.Text.Trim();
        snapshot.UiLanguage = Strings.Language;
        _performancePage.SaveConfig(snapshot.Perf);
        _languagePage.SaveConfig(snapshot.Lang);
        _trainerPage.SaveConfig(snapshot.Trainer);
        return snapshot;
    }

    private bool TryPrepareOperation(out string gameDir, out ToolkitConfig snapshot)
    {
        gameDir = _gamePath.Text.Trim();
        snapshot = ToolkitConfig.CreateDefault();
        if (!GamePaths.IsGameDir(gameDir))
        {
            ShowOperationError(Strings.Get("Error_GameNotFound"));
            return false;
        }
        try
        {
            snapshot = SnapshotConfiguration();
            _config = snapshot;
            _config.Save();
            return true;
        }
        catch (Exception ex)
        {
            ShowOperationError(Strings.Get("Error_GeneralFailure", ex.Message));
            return false;
        }
    }

    private async Task ApplyAsync()
    {
        if (_busy || !TryPrepareOperation(out string gameDir, out ToolkitConfig snapshot)) return;
        SetBusy(true);
        AppendLog(Strings.Get("Gui_Log_ApplyStart"));
        try
        {
            Result<ApplyReport> result = await Task.Run(() => _pipeline.ApplyAll(gameDir, snapshot));
            if (!result.Success)
            {
                ShowOperationError(result.ErrorMessage ?? Strings.Get("Error_GeneralFailure", "Unknown error"));
                return;
            }
            foreach (string warning in result.Warnings) AppendLog(Strings.Get("Gui_Log_Warning", warning));
            string files = result.Value is null || result.Value.FilesWritten.Count == 0
                ? Strings.Get("Gui_NoFilesChanged") : string.Join(", ", result.Value.FilesWritten);
            AppendLog(Strings.Get("Gui_Log_ApplyComplete", files));
            ShowOperationSuccess(Strings.Get("Apply_Success"));
        }
        catch (Exception ex) { ShowOperationError(Strings.Get("Error_GeneralFailure", ex.Message)); }
        finally { SetBusy(false); }
    }

    /// <summary>
    /// 帶診斷層啟動遊戲，或掛載到已經在跑的遊戲。
    ///
    /// 這條路徑<b>不寫任何遊戲檔案</b>，只動被啟動／被掛載行程的記憶體，
    /// 所以刻意不走 <c>TryPrepareOperation</c>（那會存設定並準備寫檔）。
    /// 但配置清單仍然要寫，否則事後拿到故障報告會不知道當時掛了什麼。
    /// </summary>
    private async Task LaunchWithDiagnosticsAsync(bool attachToRunning)
    {
        if (_busy) return;

        string gameDir = _gamePath.Text.Trim();
        if (!GamePaths.IsGameDir(gameDir))
        {
            ShowOperationError(Strings.Get("Error_GameNotFound"));
            return;
        }

        SetBusy(true);
        AppendLog(Strings.Get(attachToRunning ? "Gui_Log_DiagAttaching" : "Gui_Log_DiagLaunching"));
        try
        {
            var diag = new DiagnosticsOptions();
            ToolkitConfig snapshot = SnapshotConfiguration();

            Result<RunOutcome> result = await Task.Run(() =>
            {
                try
                {
                    Directory.CreateDirectory(GameRunner.DiagnosticsDirectory);
                    RunManifest.Write(GameRunner.DiagnosticsDirectory, gameDir, snapshot, diag);
                }
                catch
                {
                    // 清單寫不出來不該擋住診斷本身；故障報告仍然有價值，
                    // 只是解讀時要自己回想當時的設定。
                }

                return attachToRunning
                    ? GameRunner.AttachToRunningGame(diag, m => AppendLog(m))
                    : GameRunner.LaunchWithDiagnostics(gameDir, diag, m => AppendLog(m));
            });

            if (!result.Success)
            {
                ShowOperationError(result.ErrorMessage ?? Strings.Get("Error_GeneralFailure", "Unknown error"));
                return;
            }

            foreach (string warning in result.Warnings) AppendLog(Strings.Get("Gui_Log_Warning", warning));
            RunOutcome outcome = result.Value!;
            AppendLog(Strings.Get("Gui_Log_DiagReady", outcome.ProcessId, outcome.OutputDirectory));
            AppendLog(Strings.Get("Gui_Log_DiagFolder", outcome.OutputDirectory));
            ShowOperationSuccess(Strings.Get("Gui_Log_DiagFolder", outcome.OutputDirectory));
        }
        catch (Exception ex) { ShowOperationError(Strings.Get("Error_GeneralFailure", ex.Message)); }
        finally { SetBusy(false); }
    }

    private async Task RestoreAsync()
    {
        if (_busy || !GamePaths.IsGameDir(_gamePath.Text.Trim()))
        {
            if (!_busy) ShowOperationError(Strings.Get("Error_GameNotFound"));
            return;
        }
        if (MessageBox.Show(this, Strings.Get("Gui_RestoreConfirm"), Strings.Get("Gui_Restore"),
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;

        string gameDir = _gamePath.Text.Trim();
        SetBusy(true);
        AppendLog(Strings.Get("Gui_Log_RestoreStart"));
        try
        {
            Result<RestoreReport> result = await Task.Run(() => _pipeline.RestoreAll(gameDir));
            if (!result.Success)
            {
                ShowOperationError(result.ErrorMessage ?? Strings.Get("Error_GeneralFailure", "Unknown error"));
                return;
            }
            string files = result.Value is null || result.Value.RestoredFiles.Count == 0
                ? Strings.Get("Gui_NoFilesChanged") : string.Join(", ", result.Value.RestoredFiles);
            AppendLog(Strings.Get("Gui_Log_RestoreComplete", files));
            ShowOperationSuccess(Strings.Get("Restore_Success"));
        }
        catch (Exception ex) { ShowOperationError(Strings.Get("Error_GeneralFailure", ex.Message)); }
        finally { SetBusy(false); }
    }

    private async Task CheckAsync()
    {
        if (_busy || !TryPrepareOperation(out string gameDir, out ToolkitConfig snapshot)) return;
        SetBusy(true);
        AppendLog(Strings.Get("Gui_Log_CheckStart"));
        try
        {
            Result<VerificationReport> result = await Task.Run(() => _pipeline.Verify(gameDir, snapshot));
            if (!result.Success || result.Value is null)
            {
                ShowOperationError(result.ErrorMessage ?? Strings.Get("Error_GeneralFailure", "Unknown error"));
                return;
            }
            foreach (var file in result.Value.Files.Values)
                AppendLog(Strings.Get("Gui_Log_FileStatus", file.File, file.State,
                    file.AppliedPatches.Count == 0 ? "-" : string.Join(", ", file.AppliedPatches)));
            foreach (string warning in result.Warnings) AppendLog(Strings.Get("Gui_Log_Warning", warning));
            if (result.Value.AllRecognised && result.Value.AllMatchesConfig)
                ShowOperationSuccess(Strings.Get("Verify_AllOk"));
            else ShowOperationWarning(Strings.Get("Verify_Mismatch"));
        }
        catch (Exception ex) { ShowOperationError(Strings.Get("Error_GeneralFailure", ex.Message)); }
        finally { SetBusy(false); }
    }

    private void SetBusy(bool busy, bool profilerOwnsBusy = false)
    {
        if (InvokeRequired) { BeginInvoke(() => SetBusy(busy, profilerOwnsBusy)); return; }
        _busy = busy;
        _apply.Enabled = !busy;
        _restore.Enabled = !busy;
        _check.Enabled = !busy;
        _diagLaunch.Enabled = !busy;
        _diagAttach.Enabled = !busy;
        _browse.Enabled = !busy;
        _tabs.Enabled = !busy || profilerOwnsBusy;
        _operationStatus.Text = busy ? Strings.Get("Gui_Working") : Strings.Get("Gui_Ready");
        UseWaitCursor = busy;
    }

    private void ShowOperationSuccess(string message)
    {
        _operationStatus.Text = message;
        _operationStatus.ForeColor = Success;
        AppendLog(message);
    }

    private void ShowOperationWarning(string message)
    {
        _operationStatus.Text = message;
        _operationStatus.ForeColor = Color.FromArgb(180, 83, 9);
        AppendLog(message);
    }

    private void ShowOperationError(string message)
    {
        _operationStatus.Text = message;
        _operationStatus.ForeColor = Danger;
        AppendLog(Strings.Get("Gui_Log_Error", message));
        MessageBox.Show(this, message, Strings.Get("Gui_ErrorTitle"), MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    private void AppendLog(string message)
    {
        if (InvokeRequired) { BeginInvoke(() => AppendLog(message)); return; }
        _log.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
        _log.SelectionStart = _log.TextLength;
        _log.ScrollToCaret();
    }

    private void PersistCurrentUiSilently()
    {
        if (_initialising || _busy || _config.LoadError is not null) return;
        try { _config = SnapshotConfiguration(); _config.Save(); } catch { }
    }

    private sealed record LanguageChoice(string Code, string Label)
    {
        public override string ToString() => Label;
    }
}
