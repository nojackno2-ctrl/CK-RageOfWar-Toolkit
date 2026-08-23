using System.Diagnostics;
using CKToolkit.Core.Common;
using CKToolkit.Core.Perf;
using CKToolkit.Core.Runtime;
using CKToolkit.I18n;

namespace CKToolkit.Gui;

/// <summary>
/// 分析器分頁——工具裡<b>唯一</b>啟動診斷的地方。
///
/// 整合之前，「開始記錄」這件事散在五顆按鈕上：修改器頁的「啟動遊戲」、底部那排的
/// 「帶診斷啟動 / 掛載 / 常駐監看」，以及這裡的「開始分析」。前四顆只做 ckperf.dll
/// 注入，第五顆只做取樣器與偵錯器，兩邊的輸出還落在不同資料夾。2026-08-22 的大軍團
/// 閃退就是這樣少掉半份證據的：使用者按了「啟動遊戲」，以為分析器在記錄，其實沒有。
///
/// 現在只有一顆「開始記錄」，背後一律走 <see cref="DiagnosticSession"/>：兩層一起開、
/// 接同一個 pid、寫同一個資料夾。剩下的選擇只有「怎麼讓遊戲跟診斷層碰頭」這一個問題，
/// 由最上面那張卡片的三個選項回答。
///
/// 版面依用途分成五張卡片，每個控制項下面都帶一行灰色說明文字，解釋「這個選項是做什麼、
/// 什麼時候該用」，不用再靠猜的：
///   怎麼開始   —— 工具啟動遊戲／掛到執行中／等遊戲出現（Steam 走這個）。
///   取樣設定   —— 多久記錄一次、記多久。
///   閃退攔截   —— 每秒詳細記錄與偵錯器模式，這兩個是「抓到閃退現場」的核心。
///   遊戲加速器 —— 讓需要跑很久才會出現的問題提早發生。
///   記錄檔     —— 兩層的輸出都放這裡；預設桌面，每次執行遊戲一個記錄檔。
/// </summary>
public sealed class ProfilerPage : UserControl
{
    private static readonly Color DescColor = Color.FromArgb(100, 116, 139);

    private readonly GroupBox _modeGroup = new();
    private readonly RadioButton _modeLaunch = new();
    private readonly Label _modeLaunchDesc = new();
    private readonly RadioButton _modeAttach = new();
    private readonly Label _modeAttachDesc = new();
    private readonly RadioButton _modeWait = new();
    private readonly Label _modeWaitDesc = new();

    private readonly GroupBox _samplingGroup = new();
    private readonly Label _hzLabel = new();
    private readonly NumericUpDown _hz = new();
    private readonly Label _hzDesc = new();
    private readonly Label _secondsLabel = new();
    private readonly NumericUpDown _seconds = new();
    private readonly Label _secondsDesc = new();
    private readonly Label _segmentLabel = new();
    private readonly NumericUpDown _segment = new();
    private readonly Label _segmentDesc = new();

    private readonly GroupBox _crashGroup = new();
    private readonly CheckBox _detailed = new();
    private readonly Label _detailedDesc = new();
    private readonly CheckBox _catchCrash = new();
    private readonly Label _catchCrashDesc = new();
    private readonly CheckBox _fullDump = new();
    private readonly Label _fullDumpDesc = new();

    private readonly GroupBox _speedGroup = new();
    private readonly Label _speedLabel = new();
    private readonly ComboBox _speed = new();
    private readonly Label _speedDesc = new();
    private readonly Label _speedMethodLabel = new();
    private readonly ComboBox _speedMethod = new();
    private readonly Label _speedMethodDesc = new();

    private readonly GroupBox _outputGroup = new();
    private readonly Label _outputLabel = new();
    private readonly TextBox _output = new();
    private readonly Button _browse = new();
    private readonly Button _openFolder = new();
    private readonly Label _logFolderDesc = new();

    private readonly Label _intro = new();
    private readonly Button _start = new();
    private readonly Button _stop = new();
    private readonly Label _hint = new();
    private readonly TextBox _report = new();
    private CancellationTokenSource? _cancellation;
    private string? _lastLogPath;
    private string? _lastOutputDir;

    public event Action<bool>? BusyChanged;
    public event Action<string>? LogMessage;

    /// <summary>遊戲目錄從哪裡來。由 MainForm 綁到最上面那個路徑輸入框。</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Func<string>? GameDirProvider { get; set; }

    /// <summary>當下的工具設定從哪裡來，用來寫執行清單。事後看故障報告才知道當時掛了什麼。</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Func<ToolkitConfig>? ConfigProvider { get; set; }

    public ProfilerPage()
    {
        AutoScroll = true;
        BackColor = Color.White;
        Padding = new Padding(18);
        BuildUi();
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, RowCount = 4 };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _intro.AutoSize = true;
        _intro.MaximumSize = new Size(1000, 0);
        _intro.ForeColor = DescColor;
        _intro.Margin = new Padding(0, 0, 0, 14);
        root.Controls.Add(_intro, 0, 0);

        var grid = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, RowCount = 4 };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        // 一定要顯式加滿 AutoSize 列樣式，否則 RowCount 是設了但 RowStyles 是空的，
        // 那些列拿不到內容實際需要的高度，欄位會被裁掉只剩第一個。
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        BuildModeGroup();
        BuildSamplingGroup();
        BuildCrashGroup();
        BuildSpeedGroup();
        BuildOutputGroup();

        grid.Controls.Add(_modeGroup, 0, 0);
        grid.SetColumnSpan(_modeGroup, 2);
        grid.Controls.Add(_samplingGroup, 0, 1);
        grid.Controls.Add(_crashGroup, 1, 1);
        grid.Controls.Add(_speedGroup, 0, 2);
        grid.SetColumnSpan(_speedGroup, 2);
        grid.Controls.Add(_outputGroup, 0, 3);
        grid.SetColumnSpan(_outputGroup, 2);
        root.Controls.Add(grid, 0, 2);

        var actions = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(0, 12, 0, 8) };
        _start.AutoSize = true;
        _start.MinimumSize = new Size(140, 36);
        _start.FlatStyle = FlatStyle.Flat;
        _start.BackColor = Color.FromArgb(37, 99, 235);
        _start.ForeColor = Color.White;
        _start.FlatAppearance.BorderColor = Color.FromArgb(37, 99, 235);
        _start.Font = new Font(Font, FontStyle.Bold);
        _start.Click += async (_, _) => await StartAsync(SelectedMode());
        _stop.AutoSize = true;
        _stop.MinimumSize = new Size(100, 36);
        _stop.Enabled = false;
        _stop.Click += (_, _) => _cancellation?.Cancel();
        _hint.AutoSize = true;
        _hint.MaximumSize = new Size(500, 0);
        _hint.ForeColor = DescColor;
        _hint.Margin = new Padding(15, 8, 0, 0);
        actions.Controls.AddRange([_start, _stop, _hint]);
        root.Controls.Add(actions, 0, 1);

        _report.Dock = DockStyle.Top;
        _report.Multiline = true;
        _report.ReadOnly = true;
        _report.ScrollBars = ScrollBars.Both;
        _report.WordWrap = false;
        _report.Font = new Font("Cascadia Mono", 8.5F);
        _report.BackColor = Color.FromArgb(15, 23, 42);
        _report.ForeColor = Color.FromArgb(226, 232, 240);
        _report.Height = 280;
        _report.MinimumSize = new Size(0, 180);
        root.Controls.Add(_report, 0, 3);
        Controls.Add(root);
    }

    /// <summary>
    /// 「怎麼開始」卡片。
    ///
    /// 這張卡片就是整合掉的那三顆診斷按鈕：它們之間的差別從來只有「遊戲是誰開的」，
    /// 而那是一個選項，不是三個動作。做成單選鈕之後，三個選項會並排在使用者眼前互相
    /// 對照，而不是散成三顆長得一樣、按下去結果不同的按鈕。
    /// </summary>
    private void BuildModeGroup()
    {
        ConfigureGroup(_modeGroup);
        var host = NewFieldHost();

        AddModeChoice(host, _modeLaunch, _modeLaunchDesc);
        AddModeChoice(host, _modeAttach, _modeAttachDesc);
        AddModeChoice(host, _modeWait, _modeWaitDesc);
        _modeLaunch.CheckedChanged += (_, _) => UpdateStartButtonText();
        _modeAttach.CheckedChanged += (_, _) => UpdateStartButtonText();
        _modeWait.CheckedChanged += (_, _) => UpdateStartButtonText();
        _modeLaunch.Checked = true;

        _modeGroup.Controls.Add(host);
    }

    private static void AddModeChoice(TableLayoutPanel host, RadioButton radio, Label desc)
    {
        int r = NewRow(host);
        radio.AutoSize = true;
        radio.Margin = new Padding(0, 4, 0, 2);
        host.Controls.Add(radio, 0, r);
        host.SetColumnSpan(radio, 2);
        AddDesc(host, desc, leftIndent: 18, maxWidth: 900);
    }

    private void BuildSamplingGroup()
    {
        ConfigureGroup(_samplingGroup);
        var host = NewFieldHost();

        int r = NewRow(host);
        _hzLabel.AutoSize = true;
        _hzLabel.Anchor = AnchorStyles.Left;
        host.Controls.Add(_hzLabel, 0, r);
        ConfigureNumeric(_hz, 1, 2000, 250);
        host.Controls.Add(_hz, 1, r);
        AddDesc(host, _hzDesc);

        r = NewRow(host);
        _secondsLabel.AutoSize = true;
        _secondsLabel.Anchor = AnchorStyles.Left;
        host.Controls.Add(_secondsLabel, 0, r);
        ConfigureNumeric(_seconds, 0, 86400, 0);
        host.Controls.Add(_seconds, 1, r);
        AddDesc(host, _secondsDesc);

        r = NewRow(host);
        _segmentLabel.AutoSize = true;
        _segmentLabel.Anchor = AnchorStyles.Left;
        host.Controls.Add(_segmentLabel, 0, r);
        ConfigureNumeric(_segment, 1, 3600, 60);
        host.Controls.Add(_segment, 1, r);
        AddDesc(host, _segmentDesc);

        _samplingGroup.Controls.Add(host);
    }

    private void BuildCrashGroup()
    {
        ConfigureGroup(_crashGroup);
        var host = NewFieldHost();

        int r = NewRow(host);
        _detailed.AutoSize = true;
        _detailed.Checked = true;
        _detailed.Margin = new Padding(0, 4, 0, 2);
        _detailed.CheckedChanged += (_, _) => SyncEnabled();
        host.Controls.Add(_detailed, 0, r);
        host.SetColumnSpan(_detailed, 2);
        AddDesc(host, _detailedDesc);

        r = NewRow(host);
        _catchCrash.AutoSize = true;
        _catchCrash.Checked = true;
        _catchCrash.Margin = new Padding(0, 4, 0, 2);
        _catchCrash.CheckedChanged += (_, _) => SyncEnabled();
        host.Controls.Add(_catchCrash, 0, r);
        host.SetColumnSpan(_catchCrash, 2);
        AddDesc(host, _catchCrashDesc);

        r = NewRow(host);
        _fullDump.AutoSize = true;
        _fullDump.Margin = new Padding(22, 4, 0, 2);
        host.Controls.Add(_fullDump, 0, r);
        host.SetColumnSpan(_fullDump, 2);
        AddDesc(host, _fullDumpDesc, leftIndent: 22);

        _crashGroup.Controls.Add(host);
    }

    private void BuildSpeedGroup()
    {
        ConfigureGroup(_speedGroup);
        var host = NewFieldHost();

        int r = NewRow(host);
        _speedLabel.AutoSize = true;
        _speedLabel.Anchor = AnchorStyles.Left;
        _speedLabel.Margin = new Padding(3, 0, 8, 0);
        host.Controls.Add(_speedLabel, 0, r);
        _speed.DropDownStyle = ComboBoxStyle.DropDownList;
        _speed.Width = 130;
        _speed.Margin = new Padding(3, 3, 24, 3);
        _speed.SelectedIndexChanged += (_, _) => SyncSpeedMethodEnabled();
        host.Controls.Add(_speed, 1, r);

        _speedMethodLabel.AutoSize = true;
        _speedMethodLabel.Anchor = AnchorStyles.Left;
        _speedMethodLabel.Margin = new Padding(3, 0, 8, 0);
        host.Controls.Add(_speedMethodLabel, 2, r);
        _speedMethod.DropDownStyle = ComboBoxStyle.DropDownList;
        _speedMethod.Width = 260;
        _speedMethod.Margin = new Padding(3, 3, 3, 3);
        host.Controls.Add(_speedMethod, 3, r);

        int descRow = NewRow(host);
        AddDesc(host, _speedDesc, column: 0, span: 2, row: descRow);
        AddDesc(host, _speedMethodDesc, column: 2, span: 2, row: descRow);

        _speedGroup.Controls.Add(host);
    }

    private void BuildOutputGroup()
    {
        ConfigureGroup(_outputGroup);
        var host = new TableLayoutPanel
        {
            Dock = DockStyle.Top, AutoSize = true, ColumnCount = 4, Padding = new Padding(8)
        };
        host.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        host.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        host.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        host.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        int r = NewRow(host);
        _outputLabel.AutoSize = true;
        _outputLabel.Anchor = AnchorStyles.Left;
        _outputLabel.Margin = new Padding(3, 6, 8, 0);
        host.Controls.Add(_outputLabel, 0, r);
        _output.Dock = DockStyle.Fill;
        _output.Margin = new Padding(0, 3, 8, 0);
        _output.Text = Profiler.DefaultLogDirectory();
        _output.Leave += (_, _) =>
        {
            if (_cancellation is null) EnsureOutputDirectory();
        };
        host.Controls.Add(_output, 1, r);
        _browse.AutoSize = true;
        _browse.Margin = new Padding(0, 2, 6, 0);
        _browse.Click += (_, _) => BrowseOutput();
        host.Controls.Add(_browse, 2, r);
        _openFolder.AutoSize = true;
        _openFolder.Margin = new Padding(0, 2, 0, 0);
        _openFolder.Click += (_, _) => OpenOutputFolder();
        host.Controls.Add(_openFolder, 3, r);

        AddDesc(host, _logFolderDesc, column: 0, span: 4, maxWidth: 900);

        _outputGroup.Controls.Add(host);
    }

    /// <summary>每張卡片內部欄位用的 TableLayoutPanel：欄 0 = 標籤，欄 1 = 控制項，兩欄都會自動撐開。</summary>
    private static TableLayoutPanel NewFieldHost()
    {
        var host = new TableLayoutPanel
        {
            Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Padding = new Padding(8)
        };
        host.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        host.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        return host;
    }

    private static int NewRow(TableLayoutPanel host)
    {
        host.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        host.RowCount = host.RowStyles.Count;
        return host.RowStyles.Count - 1;
    }

    /// <summary>在目前欄位下方加一行灰色說明文字，解釋這個選項實際的作用與使用時機。</summary>
    private static void AddDesc(TableLayoutPanel host, Label desc, int column = 0, int span = 2,
                                int leftIndent = 0, int? row = null, int maxWidth = 440)
    {
        int r = row ?? NewRow(host);
        desc.AutoSize = true;
        desc.ForeColor = DescColor;
        desc.Font = new Font(desc.Font.FontFamily, 8f);
        desc.MaximumSize = new Size(maxWidth, 0);
        desc.Margin = new Padding(leftIndent + 3, 0, 8, 12);
        host.Controls.Add(desc, column, r);
        if (span > 1) host.SetColumnSpan(desc, span);
    }

    private static void ConfigureGroup(GroupBox group)
    {
        group.Dock = DockStyle.Fill;
        group.Padding = new Padding(6);
        group.Margin = new Padding(0, 0, 12, 14);
        // 沒有這兩行，GroupBox 對外回報的 PreferredSize 不會反映內部 host 面板實際
        // 需要的高度，父層 grid 那一列就會分配到不夠的高度，欄位會被裁掉只剩第一個。
        group.AutoSize = true;
        group.AutoSizeMode = AutoSizeMode.GrowAndShrink;
    }

    private static void ConfigureNumeric(NumericUpDown control, decimal min, decimal max, decimal value)
    {
        control.Minimum = min;
        control.Maximum = max;
        control.Value = value;
        control.Width = 85;
        control.Margin = new Padding(3, 3, 18, 3);
    }

    private DiagnosticSession.AttachMode SelectedMode() =>
        _modeAttach.Checked ? DiagnosticSession.AttachMode.AttachRunning
        : _modeWait.Checked ? DiagnosticSession.AttachMode.WaitForGame
        : DiagnosticSession.AttachMode.LaunchGame;

    private void SyncEnabled()
    {
        if (_cancellation is not null) return;
        _fullDump.Enabled = _catchCrash.Checked;
    }

    /// <summary>
    /// 把輸出框裡的路徑當作「儲存位置」。立即在它下面建立固定的
    /// <c>CKToolkit 分析紀錄</c> 根資料夾，但回傳的仍是使用者選的儲存位置；
    /// <see cref="DiagnosticSession"/> 開跑時才在根資料夾內建「日期\單次執行」。
    /// 建不出來（權限、路徑非法）才退回預設位置，並明確記錄原因。
    /// </summary>
    private string EnsureOutputDirectory()
    {
        string text = _output.Text.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            text = Profiler.DefaultLogDirectory();
            _output.Text = text;
        }

        try
        {
            string selectedLocation = Path.GetFullPath(text);
            DiagnosticOutputLayout.EnsureCollectionDirectory(selectedLocation);
            _output.Text = selectedLocation;
            return selectedLocation;
        }
        catch (Exception ex)
        {
            string fallback = Profiler.DefaultLogDirectory();
            LogMessage?.Invoke($"輸出資料夾建立失敗（{text}）：{ex.Message}，這次改用預設位置 {fallback}");
            DiagnosticOutputLayout.EnsureCollectionDirectory(fallback);
            _output.Text = fallback;
            return fallback;
        }
    }

    private void BrowseOutput()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = Strings.Get("Gui_Profiler_SelectOutput"),
            UseDescriptionForTitle = true,
            SelectedPath = EnsureOutputDirectory()
        };
        if (dialog.ShowDialog(this) == DialogResult.OK) _output.Text = dialog.SelectedPath;
    }

    private void OpenOutputFolder()
    {
        try
        {
            if (_lastLogPath is not null && File.Exists(_lastLogPath))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{_lastLogPath}\"") { UseShellExecute = true });
                return;
            }

            string dir = _lastOutputDir is not null && Directory.Exists(_lastOutputDir)
                ? _lastOutputDir
                : DiagnosticOutputLayout.EnsureCollectionDirectory(EnsureOutputDirectory());
            Process.Start(new ProcessStartInfo(dir) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            LogMessage?.Invoke(ex.Message);
        }
    }

    private async Task StartAsync(DiagnosticSession.AttachMode mode)
    {
        if (_cancellation is not null) return;

        string gameDir = GameDirProvider?.Invoke().Trim() ?? string.Empty;
        // 只有「由工具啟動遊戲」非要有效的遊戲目錄不可；另外兩種模式是掛到別人開的
        // 行程上，沒有目錄照樣記錄得到（只是執行清單會少一份，不影響證據本身）。
        if (mode == DiagnosticSession.AttachMode.LaunchGame && !GamePaths.IsGameDir(gameDir))
        {
            string message = Strings.Get("Error_GameNotFound");
            LogMessage?.Invoke(message);
            MessageBox.Show(this, message, Strings.Get("Gui_ErrorTitle"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        // 所有控制項值在 UI 執行緒先快照，背景執行緒不讀取控制項。
        int seconds = (int)_seconds.Value;
        int hz = (int)_hz.Value;
        int segment = (int)_segment.Value;
        bool detailed = _detailed.Checked;
        bool catchCrash = _catchCrash.Checked;
        bool fullDump = _fullDump.Checked && catchCrash;
        int speedMultiplier = CurrentSpeedMultiplier();
        var speedMethod = _speedMethod.SelectedIndex == 1 ? GameSpeed.Method.Console : GameSpeed.Method.Hotkey;
        string output = EnsureOutputDirectory();
        ToolkitConfig? config = ConfigProvider?.Invoke();

        _cancellation = new CancellationTokenSource();
        CancellationTokenSource cancellation = _cancellation;
        SetBusy(true);
        _report.Clear();

        var options = new DiagnosticSession.Options
        {
            Mode = mode,
            GameDirectory = gameDir,
            Config = config,
            OutputDirectory = string.IsNullOrWhiteSpace(output) ? null : output,
            Cancel = cancellation.Token,
            Log = message =>
            {
                LogMessage?.Invoke(message);
                AppendReport(message + Environment.NewLine);
            },
            Sampler = new Profiler.Options
            {
                Seconds = seconds,
                Hz = hz,
                SegmentSeconds = segment,
                Detailed = detailed,
                CatchCrash = catchCrash,
                FullMemoryDump = fullDump,
                SpeedMultiplier = speedMultiplier,
                SpeedMethod = speedMethod,
            }
        };

        try
        {
            var result = await Task.Run(() => DiagnosticSession.Run(options));
            foreach (string warning in result.Warnings) AppendReport(Strings.Get("Gui_Log_Warning", warning) + Environment.NewLine);

            if (result.Success && result.Value is not null)
            {
                var value = result.Value;
                _lastLogPath = value.Sampler.LogPath;
                _lastOutputDir = value.OutputDirectory;
                _report.Text = BuildSummary(value);
                LogMessage?.Invoke(Strings.Get("Gui_Profiler_Complete"));

                if (value.Sampler.Crashed)
                {
                    MessageBox.Show(this,
                        Strings.Get("Gui_Profiler_CrashCaptured", value.Sampler.LogPath ?? "-",
                                    value.Sampler.DumpPath ?? "-", value.Sampler.StatePath ?? "-"),
                        Strings.Get("Gui_Tab_Profiler"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            else
            {
                string error = result.ErrorMessage ?? Strings.Get("Error_GeneralFailure", "Unknown error");
                AppendReport(error + Environment.NewLine);
                LogMessage?.Invoke(error);
                MessageBox.Show(this, error, Strings.Get("Gui_ErrorTitle"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        catch (Exception ex)
        {
            string error = Strings.Get("Error_GeneralFailure", ex.Message);
            AppendReport(error + Environment.NewLine);
            LogMessage?.Invoke(error);
        }
        finally
        {
            cancellation.Dispose();
            _cancellation = null;
            SetBusy(false);
        }
    }

    private static string BuildSummary(DiagnosticSession.SessionResult value)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(Strings.Get("Gui_Profiler_LayerRuntime", value.RuntimeLayerActive
            ? Strings.Get(value.InjectedBeforeEntryPoint
                ? "Gui_Profiler_LayerRuntimeEarly"
                : "Gui_Profiler_LayerRuntimeLate")
            : Strings.Get("Gui_Profiler_LayerRuntimeOff", value.RuntimeLayerNote ?? "-")));
        sb.AppendLine(Strings.Get("Gui_Profiler_OutputFolder", value.OutputDirectory));
        sb.AppendLine(Strings.Get("Gui_Profiler_LogWritten", value.Sampler.LogPath ?? "-"));
        if (value.Sampler.DumpPath is not null) sb.AppendLine(Strings.Get("Gui_Profiler_DumpWritten", value.Sampler.DumpPath));
        if (value.Sampler.StatePath is not null) sb.AppendLine(Strings.Get("Gui_Profiler_StateWritten", value.Sampler.StatePath));
        if (value.Sampler.ExitCodeKnown) sb.AppendLine($"Exit code: 0x{value.Sampler.ExitCode:X8}");
        sb.AppendLine();
        sb.Append(value.Sampler.Report);
        return sb.ToString();
    }

    private void AppendReport(string text)
    {
        if (InvokeRequired) { BeginInvoke(() => AppendReport(text)); return; }
        _report.AppendText(text);
    }

    /// <summary>目前選到的加速倍率；沒選到（-1）或選到「不加速」都算 0。</summary>
    private int CurrentSpeedMultiplier() =>
        _speed.SelectedIndex >= 0 && _speed.SelectedItem is SpeedChoice sc ? sc.Multiplier : 0;

    /// <summary>
    /// 「倍率」選了不加速時，「方式」下拉跟著灰掉——不然使用者只改了方式、
    /// 沒注意到倍率還停在預設的「不加速」，加速器整個沒動靜卻毫無提示
    /// （2026-08-22 使用者實測回報：選了原版按鍵綁定，遊戲速度完全沒變，
    /// 一路查下去才發現倍率預設是「不加速」，訊息只寫進記錄檔沒人會去看）。
    /// </summary>
    private void SyncSpeedMethodEnabled() => _speedMethod.Enabled = _speed.Enabled && CurrentSpeedMultiplier() > 1;

    private void SetBusy(bool busy)
    {
        if (InvokeRequired) { BeginInvoke(() => SetBusy(busy)); return; }
        _start.Enabled = !busy;
        _stop.Enabled = busy;
        _modeLaunch.Enabled = !busy;
        _modeAttach.Enabled = !busy;
        _modeWait.Enabled = !busy;
        _seconds.Enabled = !busy;
        _hz.Enabled = !busy;
        _segment.Enabled = !busy;
        _detailed.Enabled = !busy;
        _catchCrash.Enabled = !busy;
        _fullDump.Enabled = !busy && _catchCrash.Checked;
        _speed.Enabled = !busy;
        SyncSpeedMethodEnabled();
        _output.Enabled = !busy;
        _browse.Enabled = !busy;
        BusyChanged?.Invoke(busy);
    }

    private sealed record SpeedChoice(int Multiplier, string Text)
    {
        public override string ToString() => Text;
    }

    public void ApplyLanguage()
    {
        _intro.Text = Strings.Get("Gui_Profiler_Hint");

        _modeGroup.Text = Strings.Get("Gui_Profiler_SectionMode");
        _modeLaunch.Text = Strings.Get("Gui_Profiler_ModeLaunch");
        _modeLaunchDesc.Text = Strings.Get("Gui_Profiler_ModeLaunchDesc");
        _modeAttach.Text = Strings.Get("Gui_Profiler_ModeAttach");
        _modeAttachDesc.Text = Strings.Get("Gui_Profiler_ModeAttachDesc");
        _modeWait.Text = Strings.Get("Gui_Profiler_ModeWait");
        _modeWaitDesc.Text = Strings.Get("Gui_Profiler_ModeWaitDesc");

        _samplingGroup.Text = Strings.Get("Gui_Profiler_SectionSampling");
        _hzLabel.Text = Strings.Get("Gui_Profiler_Hz");
        _hzDesc.Text = Strings.Get("Gui_Profiler_HzDesc");
        _secondsLabel.Text = Strings.Get("Gui_Profiler_Seconds");
        _secondsDesc.Text = Strings.Get("Gui_Profiler_SecondsDesc");
        _segmentLabel.Text = Strings.Get("Gui_Profiler_Segment");
        _segmentDesc.Text = Strings.Get("Gui_Profiler_SegmentDesc");

        _crashGroup.Text = Strings.Get("Gui_Profiler_SectionCrash");
        _detailed.Text = Strings.Get("Gui_Profiler_Detailed");
        _detailedDesc.Text = Strings.Get("Gui_Profiler_DetailedDesc");
        _catchCrash.Text = Strings.Get("Gui_Profiler_CatchCrash");
        _catchCrashDesc.Text = Strings.Get("Gui_Profiler_CatchCrashDesc");
        _fullDump.Text = Strings.Get("Gui_Profiler_FullDump");
        _fullDumpDesc.Text = Strings.Get("Gui_Profiler_FullDumpDesc");

        _speedGroup.Text = Strings.Get("Gui_Profiler_SectionSpeed");
        _speedLabel.Text = Strings.Get("Gui_Profiler_Speed");
        _speedDesc.Text = Strings.Get("Gui_Profiler_SpeedDesc");
        _speedMethodLabel.Text = Strings.Get("Gui_Profiler_SpeedMethod");
        _speedMethodDesc.Text = Strings.Get("Gui_Profiler_SpeedMethodDesc");

        _outputGroup.Text = Strings.Get("Gui_Profiler_SectionOutput");
        _outputLabel.Text = Strings.Get("Gui_Profiler_LogFolder");
        _logFolderDesc.Text = Strings.Get("Gui_Profiler_LogFolderDesc");
        _browse.Text = Strings.Get("Gui_Browse");
        _openFolder.Text = Strings.Get("Gui_Profiler_OpenFolder");

        UpdateStartButtonText();
        _stop.Text = Strings.Get("Gui_Profiler_Stop");
        _hint.Text = Strings.Get("Gui_Profiler_RunHint");

        // -1（從沒選過，剛開頁面）才用「10x 極速」當預設；使用者自己選過的值
        // （哪怕是特意選回「不加速」）一律照舊保留，這裡只補第一次的預設。
        // 之前預設落在 index 0＝「不加速」，於是選了加速方式卻忘了另外調倍率的人
        // 加速器全程沒動靜、又只在記錄檔裡留一行訊息，等於整個功能看起來「沒用」。
        const int defaultSpeedIndex = 2; // 10x 極速
        int speedIndex = _speed.SelectedIndex < 0 ? defaultSpeedIndex : _speed.SelectedIndex;
        _speed.Items.Clear();
        _speed.Items.AddRange(
        [
            new SpeedChoice(0, Strings.Get("Gui_Profiler_SpeedOff")),
            new SpeedChoice(3, Strings.Get("Gui_Profiler_SpeedMax")),
            new SpeedChoice(10, Strings.Get("Gui_Profiler_SpeedTurbo")),
            new SpeedChoice(20, "20x"),
            new SpeedChoice(50, "50x")
        ]);
        _speed.SelectedIndex = Math.Min(speedIndex, _speed.Items.Count - 1);
        SyncSpeedMethodEnabled();

        int methodIndex = Math.Max(0, _speedMethod.SelectedIndex);
        _speedMethod.Items.Clear();
        _speedMethod.Items.AddRange(
        [
            Strings.Get("Gui_Profiler_SpeedHotkey"),
            Strings.Get("Gui_Profiler_SpeedConsole")
        ]);
        _speedMethod.SelectedIndex = Math.Min(methodIndex, _speedMethod.Items.Count - 1);
    }

    private void UpdateStartButtonText()
    {
        _start.Text = Strings.Get(_modeLaunch.Checked
            ? "Gui_Profiler_StartLaunch"
            : _modeAttach.Checked
                ? "Gui_Profiler_StartAttach"
                : "Gui_Profiler_StartWait");
    }
}
