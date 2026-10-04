using CKToolkit.Core.Common;
using CKToolkit.Core.Runtime;
using CKToolkit.Gui.Layout;
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
    // 這一次執行中被「還原原版」過的遊戲目錄：之後不再自動寫設定檔進去，直到下一次套用。
    private string? _restoredGameDir;
    private bool _gameDirSaveWarned;

    private readonly Label _title = new();
    private readonly Label _subtitle = new();
    private readonly Label _pathLabel = new();
    private readonly TextBox _gamePath = new();
    private readonly Button _browse = new();
    private readonly Label _pathStatus = new();
    private readonly ComboBox _uiLanguage = new UiComboBox();
    private readonly TabControl _tabs = new();
    private readonly TabPage _perfTab = new();
    private readonly TabPage _langTab = new();
    private readonly TabPage _trainerTab = new();
    private readonly TabPage _settingsTab = new();
    private readonly TabPage _saveTab = new();
    private readonly TabPage _profilerTab = new();
    private readonly TabPage _aboutTab = new();
    private readonly PerformancePage _performancePage = new();
    private readonly LanguagePage _languagePage = new();
    private readonly TrainerPage _trainerPage = new();
    private readonly GameSettingsPage _gameSettingsPage = new();
    private readonly SavePage _savePage = new();
    private readonly ProfilerPage _profilerPage = new();
    private readonly AboutPage _aboutPage = new();
    private readonly Button _apply = new();
    private readonly Button _restore = new();
    private readonly Label _operationStatus = new();
    private readonly TextBox _log = new();
    private InGamePanelForm? _panel;

    public MainForm()
    {
        _config = ToolkitConfig.Load();
        Strings.Language = _config.UiLanguage;
        InitializeComponent();
        _languagePage.GameDirProvider = () => _gamePath.Text.Trim();
        _savePage.GameDirProvider = () => _gamePath.Text.Trim();
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

    /// <summary>
    /// 主視窗外框（ISSUE-101）：標題列、分頁、按鈕列、記錄區由上而下堆疊，分頁吃掉剩餘高度。
    /// 記錄區的高度以「幾行字」表示，不再是百分比或寫死的像素。
    /// 所有尺寸都是 96 DPI 的邏輯像素，由 <see cref="Ui.EndForm"/> 一次換算到實際 DPI。
    /// </summary>
    private void InitializeComponent()
    {
        Ui.BeginForm(this);
        MinimumSize = new Size(760, 540);
        Size = new Size(1120, 820);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Surface;

        // 整個外框也是可捲動的堆疊：正常情況下分頁吃掉剩餘高度、什麼都不捲；
        // 只有在字特別大又把視窗縮到最小時，才由外框捲動，記錄區不會被擠出視窗。
        var frame = new ScrollPage { Dock = DockStyle.Fill, BackColor = Surface, Padding = new Padding(12), MinimumContentLogicalWidth = 600 };
        StackPanel root = frame.Content;
        root.Add(BuildHeader());
        root.AddFill(BuildTabs(), 220);
        root.Add(BuildActions());
        root.AddLines(BuildLog(), 5);
        Controls.Add(frame);
        AcceptButton = _apply;
        Ui.EndForm(this);
        Load += (_, _) => Ui.FitToScreen(this);
    }

    private Control BuildHeader()
    {
        var panel = new StackPanel { BackColor = Color.White, Padding = new Padding(14, 10, 14, 10), Margin = new Padding(0, 0, 0, 8) };

        // 第一列：標題（可換行）＋介面語言。
        var titleRow = Ui.FieldTable(2);
        titleRow.ColumnStyles[0] = new ColumnStyle(SizeType.Percent, 100F);
        titleRow.ColumnStyles[1] = new ColumnStyle(SizeType.AutoSize);
        titleRow.Margin = Padding.Empty;
        _title.AutoSize = true;
        _title.UseMnemonic = false;
        _title.Font = Ui.UiFont(16F, FontStyle.Bold);
        _title.ForeColor = Color.FromArgb(15, 23, 42);
        _title.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        _title.Margin = new Padding(0, 0, 12, 0);
        _uiLanguage.DropDownStyle = ComboBoxStyle.DropDownList;
        _uiLanguage.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _uiLanguage.Margin = new Padding(0, 4, 0, 0);
        _uiLanguage.Items.AddRange([
            new LanguageChoice("zh-TW", "繁體中文"),
            new LanguageChoice("zh-CN", "简体中文"),
            new LanguageChoice("en", "English")
        ]);
        _uiLanguage.SelectedIndexChanged += (_, _) => ChangeUiLanguage();
        Ui.FitComboToItems(_uiLanguage);
        titleRow.RowCount = 1;
        titleRow.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        titleRow.Controls.Add(_title, 0, 0);
        titleRow.Controls.Add(_uiLanguage, 1, 0);
        panel.Add(titleRow);

        Ui.Text(_subtitle).Margin = new Padding(0, 2, 0, 8);
        panel.Add(_subtitle);

        // 第二列：遊戲目錄。狀態文字放在輸入框下面一行，不跟輸入框搶寬度。
        var pathRow = Ui.FieldTable(3);
        pathRow.ColumnStyles[1] = new ColumnStyle(SizeType.Percent, 100F);
        pathRow.ColumnStyles[2] = new ColumnStyle(SizeType.AutoSize);
        pathRow.Margin = Padding.Empty;
        _gamePath.TextChanged += (_, _) => RefreshPathStatus();
        _gamePath.Margin = new Padding(0, 0, 8, 0);
        _browse.AutoSize = true;
        _browse.UseMnemonic = false;
        _browse.Margin = Padding.Empty;
        _browse.Click += (_, _) => BrowseGameDirectory();
        Ui.AddRow(pathRow, _pathLabel, _gamePath, _browse);
        _pathStatus.AutoSize = true;
        _pathStatus.UseMnemonic = false;
        _pathStatus.Margin = new Padding(0, 4, 0, 0);
        Ui.AddRow(pathRow, null, _pathStatus);
        pathRow.SetColumnSpan(_pathStatus, 2);
        panel.Add(pathRow);
        return panel;
    }

    private Control BuildTabs()
    {
        _tabs.Padding = new Point(14, 5);
        _tabs.Margin = Padding.Empty;
        _tabs.Controls.AddRange([_perfTab, _langTab, _trainerTab, _settingsTab, _saveTab, _profilerTab, _aboutTab]);
        foreach (Control page in new Control[] { _performancePage, _languagePage, _trainerPage, _gameSettingsPage, _savePage, _profilerPage, _aboutPage })
            page.Dock = DockStyle.Fill;
        foreach (TabPage tab in _tabs.TabPages)
        {
            tab.Padding = Padding.Empty;
            tab.BackColor = Color.White;
            tab.UseVisualStyleBackColor = false;
        }
        _perfTab.Controls.Add(_performancePage);
        _langTab.Controls.Add(_languagePage);
        _trainerTab.Controls.Add(_trainerPage);
        _settingsTab.Controls.Add(_gameSettingsPage);
        _saveTab.Controls.Add(_savePage);
        _profilerTab.Controls.Add(_profilerPage);
        _aboutTab.Controls.Add(_aboutPage);
        _gameSettingsPage.SettingsChanged += PersistCurrentUiSilently;
        _profilerPage.BusyChanged += busy => SetBusy(busy, profilerOwnsBusy: true);
        _profilerPage.LogMessage += message => AppendLog(message);
        _savePage.BusyChanged += busy => SetBusy(busy, profilerOwnsBusy: true);
        _savePage.LogMessage += message => AppendLog(message);
        _tabs.Selected += (_, e) => { if (e.TabPage == _saveTab) _savePage.RefreshCatalog(); };
        _trainerPage.LaunchGameRequested += async () => await ApplyThenLaunchAsync();
        _trainerPage.OpenPanelRequested += OpenInGamePanel;
        return _tabs;
    }

    private Control BuildActions()
    {
        // 這裡以前還有一排診斷按鈕（帶診斷啟動 / 掛載 / 常駐監看）。它們被整合進分析器
        // 分頁的「怎麼開始」卡片了：三者的差別從來只有「遊戲是誰開的」，那是一個選項，
        // 不是三顆按鈕；而且舊的那條路只做 ckperf.dll 注入，不會啟動取樣器與偵錯器，
        // 使用者按了卻以為分析器在記錄，實際上少掉半份證據。
        Ui.Button(_apply, Accent, Color.White, bold: true, minWidth: 120);
        Ui.Button(_restore, Color.White, Danger, bold: true, minWidth: 120);
        _apply.Click += async (_, _) => await ApplyAsync();
        _restore.Click += async (_, _) => await RestoreAsync();
        _operationStatus.AutoSize = true;
        _operationStatus.UseMnemonic = false;
        _operationStatus.Margin = new Padding(10, 8, 0, 0);
        _operationStatus.ForeColor = Color.FromArgb(71, 85, 105);
        // 狀態訊息可能很長（錯誤訊息），給它上限寬度讓它換行，而不是把按鈕列撐寬。
        var row = Ui.ButtonRow(_apply, _restore, _operationStatus);
        row.Margin = new Padding(0, 10, 0, 4);
        row.Layout += (_, _) =>
        {
            int max = Math.Max(row.Font.Height * 6, row.ClientSize.Width - _apply.Width - _restore.Width - row.LogicalToDeviceUnits(40));
            if (_operationStatus.MaximumSize.Width != max) _operationStatus.MaximumSize = new Size(max, 0);
        };
        return row;
    }

    private Control BuildLog()
    {
        _log.Multiline = true;
        _log.ReadOnly = true;
        _log.ScrollBars = ScrollBars.Vertical;
        _log.BackColor = Color.FromArgb(15, 23, 42);
        _log.ForeColor = Color.FromArgb(226, 232, 240);
        _log.Font = Ui.MonoFont(8.5F);
        _log.BorderStyle = BorderStyle.None;
        _log.WordWrap = false;
        _log.Margin = Padding.Empty;
        return _log;
    }

    private void LoadConfigurationIntoControls()
    {
        _gamePath.Text = _config.GameDir ?? string.Empty;
        _performancePage.LoadConfig(_config.Perf);
        _languagePage.LoadConfig(_config.Lang);
        _trainerPage.LoadConfig(_config.Trainer);
        _gameSettingsPage.LoadConfig(_config.GameSettings);
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
        AdoptGameDirConfig(_gamePath.Text.Trim());
        AppendLog(Strings.Get("Gui_Log_Ready"));
    }

    /// <summary>
    /// 讀回遊戲資料夾裡那份「上次套用的設定」（ISSUE-100）。
    ///
    /// 只有在它比手上這份新的時候才改用它——手上這份沒有時間戳（重新下載工具包、
    /// 或設定檔還是舊版格式）也算。這樣「使用者改了但還沒套用」的設定不會被
    /// 遊戲資料夾裡較舊的那份蓋掉，而換一份工具包、換一台電腦時設定會自己回來。
    /// </summary>
    private void AdoptGameDirConfig(string gameDir, bool force = false)
    {
        if (_busy || !GamePaths.IsGameDir(gameDir)) return;

        ToolkitConfig? fromGameDir = ToolkitConfig.TryLoadFromGameDir(gameDir, out string? error);
        if (error is not null) AppendLog(error);
        if (fromGameDir is null || !(force || _config.ShouldAdopt(fromGameDir))) return;

        // 介面語言是這台電腦的偏好，不屬於某一份遊戲安裝，不跟著搬。
        fromGameDir.UiLanguage = _config.UiLanguage;
        fromGameDir.GameDir = gameDir;
        _config = fromGameDir;

        bool wasInitialising = _initialising;
        _initialising = true;
        try
        {
            LoadConfigurationIntoControls();
        }
        finally
        {
            _initialising = wasInitialising;
        }

        foreach (string migration in _config.MigrationsApplied) AppendLog(migration);
        AppendLog(Strings.Get("Gui_Log_ConfigLoadedFromGameDir",
            ToolkitConfig.GameDirConfigPath(gameDir)));
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
        _settingsTab.Text = Strings.Get("Gui_Tab_GameSettings");
        _saveTab.Text = Strings.Get("Gui_Tab_Saves");
        _profilerTab.Text = Strings.Get("Gui_Tab_Profiler");
        _aboutTab.Text = Strings.Get("Gui_Tab_About");
        _apply.Text = Strings.Get("Gui_Apply");
        _restore.Text = Strings.Get("Gui_Restore");
        _operationStatus.Text = _busy ? Strings.Get("Gui_Working") : Strings.Get("Gui_Ready");
        _performancePage.ApplyLanguage();
        _languagePage.ApplyLanguage();
        _trainerPage.ApplyLanguage();
        _gameSettingsPage.ApplyLanguage();
        _savePage.ApplyLanguage();
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
            string previous = _config.GameDir ?? string.Empty;
            _gamePath.Text = dialog.SelectedPath;
            // 換到另一份遊戲安裝時，那一份安裝自己的設定才是權威，不比時間直接採用
            // （ISSUE-100）。指回原本那一份時就照常比時間。
            bool switched = !string.Equals(previous, _gamePath.Text.Trim(), StringComparison.OrdinalIgnoreCase);
            AdoptGameDirConfig(_gamePath.Text.Trim(), force: switched);
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
        _gameSettingsPage.SaveConfig(snapshot.GameSettings);
        return snapshot;
    }

    private bool TryPrepareOperation(out string gameDir, out ToolkitConfig snapshot)
    {
        gameDir = _gamePath.Text.Trim();
        snapshot = ToolkitConfig.CreateDefault();
        if (_config.LoadError is not null)
        {
            ShowOperationError(_config.LoadError);
            return false;
        }
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
            if (result.Value?.SettingsFile is string settingsFile)
            {
                // 套用時 ApplyAll 把設定寫進了遊戲資料夾，順手把工具包旁邊那份也更新，
                // 兩份的時間戳才會一致，下次啟動不會誤判誰比較新（ISSUE-100）。
                AppendLog(Strings.Get("Cli_Config_GameDirPath", settingsFile));
                try { _config.Save(); } catch { /* 套用已經成功，存設定失敗只是可惜 */ }
            }
            _restoredGameDir = null;
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
            // 修改器開著且支援小視窗就必須注入 ckperf.dll，否則遊戲中面板沒有腳本通道可用，
            // 使用者又會回到「沒鍵可按」的狀態（ISSUE-068）。效能頁的保護關掉時
            // 走 channel-only 選項：只開通道，不替使用者打開任何他關掉的保護。
            bool wantsChannel = _config.Trainer.Enabled && _config.Trainer.SupportsInGamePanel;
            DiagnosticsOptions? options = perf.StabilityProtection
                ? GameRunner.CreateStabilityOptions(perf, wantsChannel)
                : wantsChannel ? GameRunner.CreateScriptChannelOnlyOptions() : null;

            Result<RunOutcome> launched = await Task.Run(() => options is not null
                ? GameRunner.LaunchWithDiagnostics(gameDir, options, AppendLog)
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
                : options is not null
                    ? Strings.Get("Gui_Log_LaunchScriptChannelOnly", launched.Value.ProcessId)
                    : Strings.Get("Gui_Log_LaunchPlain", launched.Value.ProcessId));
            if (wantsChannel) AppendLog(Strings.Get("Gui_Log_ScriptChannelReady"));
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
            _restoredGameDir = gameDir;
            ShowOperationSuccess(Strings.Get("Restore_Success"));
        }
        catch (Exception ex) { ShowOperationError(Strings.Get("Error_GeneralFailure", ex.Message)); }
        finally { SetBusy(false); }
    }

    /// <summary>
    /// 開啟置頂的遊戲中面板（AGENTS.md §1 輔助視窗例外）。面板不改任何設定、不寫任何檔案，
    /// 由主視窗開關，關掉之後不留常駐。
    /// </summary>
    private void OpenInGamePanel()
    {
        if (_panel is { IsDisposed: false })
        {
            _panel.Show();
            _panel.BringToFront();
            return;
        }

        try
        {
            // 遊戲已經在跑、但這一場沒有腳本通道（典型情況：使用者直接從 Steam 開遊戲）
            // 就當場掛上去。不掛的話面板只能退回送鍵，18 個作弊又會有一半按不動。
            EnsureScriptChannelForRunningGame();

            // 面板要用目前畫面上的設定，所以先把 TrainerPage 的內容存回設定物件再建。
            _trainerPage.SaveConfig(_config.Trainer);
            _panel = new InGamePanelForm(_config.Trainer);
            _panel.FormClosed += (_, _) => _panel = null;
            _panel.Show(this);
            _panel.PositionNearGame();
        }
        catch (Exception ex)
        {
            ShowOperationError(ex.Message);
        }
    }

    /// <summary>
    /// 遊戲已在執行但這一場還沒有腳本通道時，就地掛載 <c>ckperf.dll</c> 把通道補上
    /// （ISSUE-068）。這條路服務的是「我照常從 Steam 開遊戲」的使用者。
    ///
    /// 失敗一律只寫進記錄區、不擋住面板：面板還有代送按鍵那條備援路徑，
    /// 掛不上去不代表面板不能開。
    /// </summary>
    private void EnsureScriptChannelForRunningGame()
    {
        if (!_config.Trainer.Enabled || !_config.Trainer.SupportsInGamePanel) return;

        int pid = GameRunner.FindGameProcessId();
        if (pid == 0) return;                                   // 遊戲還沒開，等啟動時注入
        if (ScriptChannelSession.ProcessId == (uint)pid) return; // 這一場已經有通道了

        var attached = GameRunner.AttachToProcess(
            (uint)pid, GameRunner.CreateScriptChannelOnlyOptions(), AppendLog);

        AppendLog(attached.Success
            ? Strings.Get("Gui_Log_ScriptChannelAttached", pid)
            : Strings.Get("Gui_Log_ScriptChannelAttachFailed", attached.ErrorMessage ?? string.Empty));
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
        try
        {
            _config = SnapshotConfiguration();
            SaveToGameDirQuietly(_config.GameDir ?? string.Empty);
            _config.Save();
        }
        catch (Exception ex)
        {
            AppendLog(Strings.Get("Gui_Log_Error", Strings.Get("Error_GeneralFailure", ex.Message)));
        }
    }

    /// <summary>
    /// 平常改的設定也存一份到遊戲資料夾（使用者需求，2026-10-04）。
    ///
    /// 以前只有「一鍵套用」成功時才寫遊戲資料夾那份（ISSUE-100），改了還沒套用就把工具包
    /// 搬到別的資料夾，那些修改就跟著舊位置的 cktoolkit.json 一起留在原地。現在遊戲資料夾
    /// 那份永遠是最新的設定，工具包放到哪裡、重新下載幾次，打開都會讀回來。
    ///
    /// 例外是「還原原版」之後：還原的承諾是遊戲資料夾不留任何本工具的檔案，
    /// 所以直到下一次套用或換了遊戲目錄之前都不再寫入。
    /// 寫不進去（唯讀、權限）只在記錄區提示一次，不打擾使用者。
    /// </summary>
    private void SaveToGameDirQuietly(string gameDir)
    {
        if (!GamePaths.IsGameDir(gameDir)) return;
        if (string.Equals(_restoredGameDir, gameDir, StringComparison.OrdinalIgnoreCase)) return;
        Result saved = _config.SaveToGameDir(gameDir, applied: false);
        if (!saved.Success && !_gameDirSaveWarned)
        {
            _gameDirSaveWarned = true;
            AppendLog(Strings.Get("Gui_Log_Warning", saved.ErrorMessage ?? string.Empty));
        }
    }

    private sealed record LanguageChoice(string Code, string Label)
    {
        public override string ToString() => Label;
    }
}
