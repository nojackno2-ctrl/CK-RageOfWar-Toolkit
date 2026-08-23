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
    private readonly Label _operationStatus = new();
    private readonly TextBox _log = new();

    public MainForm()
    {
        _config = ToolkitConfig.Load();
        Strings.Language = _config.UiLanguage;
        InitializeComponent();
        _languagePage.GameDirProvider = () => _gamePath.Text.Trim();
        // 分析器分頁現在是唯一的診斷入口，所以它需要自己拿得到遊戲目錄與當下設定：
        // 前者用來啟動遊戲，後者寫進執行清單，事後看故障報告才知道當時掛了什麼。
        _profilerPage.GameDirProvider = () => _gamePath.Text.Trim();
        _profilerPage.ConfigProvider = SnapshotConfiguration;
        LoadConfigurationIntoControls();
        ApplyLanguage();
        _initialising = false;
        Shown += (_, _) => InitialiseGamePath();
        FormClosing += (_, _) => PersistCurrentUiSilently();
    }

    private void InitializeComponent()
    {
        AutoScaleMode = AutoScaleMode.Dpi;
        MinimumSize = new Size(900, 650);
        Size = new Size(1100, 800);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Surface;
        Font = new Font("Microsoft JhengHei UI", 9F);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3,
            BackColor = Surface, Padding = new Padding(12)
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 130F));
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
            BackColor = Color.White, Padding = new Padding(14, 10, 14, 10),
            Margin = new Padding(0, 0, 0, 8)
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
        _subtitle.Margin = new Padding(0, 2, 0, 8);
        panel.Controls.Add(_subtitle, 0, 1);
        panel.SetColumnSpan(_subtitle, 3);

        _uiLanguage.DropDownStyle = ComboBoxStyle.DropDownList;
        _uiLanguage.Width = 110;
        _uiLanguage.Items.AddRange([
            new LanguageChoice("zh-TW", "繁體中文"),
            new LanguageChoice("zh-CN", "简体中文"),
            new LanguageChoice("en", "English")
        ]);
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
        _trainerPage.LaunchGameRequested += async () => await ApplyThenLaunchAsync();
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
        _apply.Click += async (_, _) => await ApplyAsync();
        _restore.Click += async (_, _) => await RestoreAsync();
        _operationStatus.AutoSize = true;
        _operationStatus.Anchor = AnchorStyles.Left;
        _operationStatus.Margin = new Padding(18, 10, 0, 0);
        _operationStatus.ForeColor = Color.FromArgb(71, 85, 105);
        actions.Controls.AddRange([_apply, _restore, _operationStatus]);
        panel.Controls.Add(actions, 0, 0);

        // 這裡以前還有一排診斷按鈕（帶診斷啟動 / 掛載 / 常駐監看）。它們被整合進分析器
        // 分頁的「怎麼開始」卡片了：三者的差別從來只有「遊戲是誰開的」，那是一個選項，
        // 不是三顆按鈕；而且舊的那條路只做 ckperf.dll 注入，不會啟動取樣器與偵錯器，
        // 使用者按了卻以為分析器在記錄，實際上少掉半份證據。
        _log.Dock = DockStyle.Fill;
        _log.Multiline = true;
        _log.ReadOnly = true;
        _log.ScrollBars = ScrollBars.Vertical;
        _log.BackColor = Color.FromArgb(15, 23, 42);
        _log.ForeColor = Color.FromArgb(226, 232, 240);
        _log.Font = new Font("Cascadia Mono", 8.5F);
        _log.BorderStyle = BorderStyle.None;
        _log.WordWrap = false;
        panel.Controls.Add(_log, 0, 1);
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
        if (Strings.EffectiveLanguage == "zh-CN") _uiLanguage.SelectedIndex = 1;
        else if (Strings.EffectiveLanguage == "zh-TW") _uiLanguage.SelectedIndex = 0;
        else _uiLanguage.SelectedIndex = 2;
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

    private async Task<bool> ApplyAsync()
    {
        if (_busy || !TryPrepareOperation(out string gameDir, out ToolkitConfig snapshot)) return false;
        SetBusy(true);
        AppendLog(Strings.Get("Gui_Log_ApplyStart"));
        try
        {
            Result<ApplyReport> result = await Task.Run(() => _pipeline.ApplyAll(gameDir, snapshot));
            if (!result.Success)
            {
                ShowOperationError(result.ErrorMessage ?? Strings.Get("Error_GeneralFailure", "Unknown error"));
                return false;
            }
            foreach (string warning in result.Warnings) AppendLog(Strings.Get("Gui_Log_Warning", warning));
            string files = result.Value is null || result.Value.FilesWritten.Count == 0
                ? Strings.Get("Gui_NoFilesChanged") : string.Join(", ", result.Value.FilesWritten);
            AppendLog(Strings.Get("Gui_Log_ApplyComplete", files));
            ShowOperationSuccess(Strings.Get("Apply_Success"));
            return true;
        }
        catch (Exception ex)
        {
            ShowOperationError(Strings.Get("Error_GeneralFailure", ex.Message));
            return false;
        }
        finally { SetBusy(false); }
    }

    /// <summary>
    /// 修改器頁「啟動遊戲」按鈕的日常產品流程：先套用目前設定（跟「一鍵套用」同一路徑），
    /// 成功後依效能頁選擇啟動「已驗證穩定保護／實驗性保護／完全不注入」。
    /// 不再強迫切到分析器；分析器是出問題時才使用的證據工具。
    /// 遊戲裡看到的還是上一次套用的舊設定。
    /// </summary>
    private async Task ApplyThenLaunchAsync()
    {
        if (_busy) return;
        bool applied = await ApplyAsync();
        if (!applied) return;

        string gameDir = _gamePath.Text.Trim();
        PerfConfig perf = _config.Perf;
        SetBusy(true);
        try
        {
            Result<RunOutcome> launched = await Task.Run(() => perf.StabilityProtection
                ? GameRunner.LaunchWithDiagnostics(gameDir, GameRunner.CreateStabilityOptions(perf), AppendLog)
                : GameRunner.LaunchPlain(gameDir));

            if (!launched.Success || launched.Value is null)
            {
                ShowOperationError(Strings.Get("Gui_LaunchFailed", launched.ErrorMessage ?? "Unknown"));
                return;
            }

            foreach (string warning in launched.Warnings) AppendLog(Strings.Get("Gui_Log_Warning", warning));
            AppendLog(perf.StabilityProtection
                ? Strings.Get(perf.ExperimentalStability
                    ? "Gui_Log_LaunchStabilityExperimental"
                    : "Gui_Log_LaunchStabilityVerified", launched.Value.ProcessId)
                : Strings.Get("Gui_Log_LaunchPlain", launched.Value.ProcessId));
            ShowOperationSuccess(Strings.Get("Gui_LaunchSuccess"));
        }
        catch (Exception ex)
        {
            ShowOperationError(Strings.Get("Gui_LaunchFailed", ex.Message));
        }
        finally
        {
            SetBusy(false);
        }
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

    private void SetBusy(bool busy, bool profilerOwnsBusy = false)
    {
        if (InvokeRequired) { BeginInvoke(() => SetBusy(busy, profilerOwnsBusy)); return; }
        _busy = busy;
        _apply.Enabled = !busy;
        _restore.Enabled = !busy;
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
