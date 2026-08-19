using CKToolkit.Core.Perf;
using CKToolkit.I18n;

namespace CKToolkit.Gui;

public sealed class ProfilerPage : UserControl
{
    private readonly Label _secondsLabel = new();
    private readonly NumericUpDown _seconds = new();
    private readonly Label _hzLabel = new();
    private readonly NumericUpDown _hz = new();
    private readonly Label _segmentLabel = new();
    private readonly NumericUpDown _segment = new();
    private readonly CheckBox _waitForGame = new();
    private readonly Label _outputLabel = new();
    private readonly TextBox _output = new();
    private readonly Button _browse = new();
    private readonly Button _start = new();
    private readonly Button _stop = new();
    private readonly Label _hint = new();
    private readonly TextBox _report = new();
    private CancellationTokenSource? _cancellation;

    public event Action<bool>? BusyChanged;
    public event Action<string>? LogMessage;

    public ProfilerPage()
    {
        BackColor = Color.White;
        Padding = new Padding(18);
        BuildUi();
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        var settings = new TableLayoutPanel
        {
            Dock = DockStyle.Top, AutoSize = true, ColumnCount = 6,
            BackColor = Color.FromArgb(248, 250, 252), Padding = new Padding(12)
        };
        for (int i = 0; i < 6; i++) settings.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        ConfigureNumeric(_seconds, 0, 86400, 60);
        ConfigureNumeric(_hz, 1, 2000, 250);
        ConfigureNumeric(_segment, 1, 3600, 60);
        settings.Controls.Add(_secondsLabel, 0, 0);
        settings.Controls.Add(_seconds, 1, 0);
        settings.Controls.Add(_hzLabel, 2, 0);
        settings.Controls.Add(_hz, 3, 0);
        settings.Controls.Add(_segmentLabel, 4, 0);
        settings.Controls.Add(_segment, 5, 0);
        _waitForGame.AutoSize = true;
        _waitForGame.Margin = new Padding(0, 12, 0, 0);
        settings.Controls.Add(_waitForGame, 0, 1);
        settings.SetColumnSpan(_waitForGame, 6);

        _outputLabel.AutoSize = true;
        _outputLabel.Anchor = AnchorStyles.Left;
        _outputLabel.Margin = new Padding(0, 12, 8, 0);
        settings.Controls.Add(_outputLabel, 0, 2);
        _output.Dock = DockStyle.Fill;
        _output.Margin = new Padding(0, 8, 8, 0);
        _output.Text = Path.Combine(AppContext.BaseDirectory, "profiler-report.txt");
        settings.Controls.Add(_output, 1, 2);
        settings.SetColumnSpan(_output, 4);
        _browse.AutoSize = true;
        _browse.Margin = new Padding(0, 7, 0, 0);
        _browse.Click += (_, _) => BrowseOutput();
        settings.Controls.Add(_browse, 5, 2);
        root.Controls.Add(settings, 0, 0);

        var actions = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(0, 10, 0, 8) };
        _start.AutoSize = true;
        _start.MinimumSize = new Size(120, 36);
        _start.Click += async (_, _) => await StartAsync();
        _stop.AutoSize = true;
        _stop.MinimumSize = new Size(100, 36);
        _stop.Enabled = false;
        _stop.Click += (_, _) => _cancellation?.Cancel();
        _hint.AutoSize = true;
        _hint.MaximumSize = new Size(650, 0);
        _hint.ForeColor = Color.FromArgb(71, 85, 105);
        _hint.Margin = new Padding(15, 8, 0, 0);
        actions.Controls.AddRange([_start, _stop, _hint]);
        root.Controls.Add(actions, 0, 1);

        _report.Dock = DockStyle.Fill;
        _report.Multiline = true;
        _report.ReadOnly = true;
        _report.ScrollBars = ScrollBars.Both;
        _report.WordWrap = false;
        _report.Font = new Font("Cascadia Mono", 8.5F);
        _report.BackColor = Color.FromArgb(15, 23, 42);
        _report.ForeColor = Color.FromArgb(226, 232, 240);
        root.Controls.Add(_report, 0, 2);
        Controls.Add(root);
    }

    private static void ConfigureNumeric(NumericUpDown control, decimal min, decimal max, decimal value)
    {
        control.Minimum = min;
        control.Maximum = max;
        control.Value = value;
        control.Width = 85;
        control.Margin = new Padding(4, 0, 18, 0);
    }

    private void BrowseOutput()
    {
        using var dialog = new SaveFileDialog
        {
            Title = Strings.Get("Gui_Profiler_SelectOutput"),
            Filter = Strings.Get("Gui_Profiler_ReportFilter"),
            FileName = Path.GetFileName(_output.Text),
            InitialDirectory = Directory.Exists(Path.GetDirectoryName(_output.Text))
                ? Path.GetDirectoryName(_output.Text) : AppContext.BaseDirectory
        };
        if (dialog.ShowDialog(this) == DialogResult.OK) _output.Text = dialog.FileName;
    }

    private async Task StartAsync()
    {
        if (_cancellation is not null) return;

        // 所有控制項值在 UI 執行緒先快照，背景執行緒不讀取控制項。
        int seconds = (int)_seconds.Value;
        int hz = (int)_hz.Value;
        int segment = (int)_segment.Value;
        bool wait = _waitForGame.Checked;
        string output = _output.Text.Trim();
        _cancellation = new CancellationTokenSource();
        CancellationTokenSource cancellation = _cancellation;
        SetBusy(true);
        _report.Clear();

        var options = new Profiler.Options
        {
            Seconds = seconds,
            Hz = hz,
            SegmentSeconds = segment,
            WaitForProcess = wait,
            OutFile = string.IsNullOrWhiteSpace(output) ? null : output,
            CancelRequested = () => cancellation.IsCancellationRequested,
            Log = message =>
            {
                LogMessage?.Invoke(message);
                AppendReport(message + Environment.NewLine);
            }
        };

        try
        {
            var result = await Task.Run(() => Profiler.Run(options));
            if (result.Success)
            {
                _report.Text = result.Value ?? string.Empty;
                LogMessage?.Invoke(Strings.Get("Gui_Profiler_Complete"));
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

    private void AppendReport(string text)
    {
        if (InvokeRequired) { BeginInvoke(() => AppendReport(text)); return; }
        _report.AppendText(text);
    }

    private void SetBusy(bool busy)
    {
        if (InvokeRequired) { BeginInvoke(() => SetBusy(busy)); return; }
        _start.Enabled = !busy;
        _stop.Enabled = busy;
        _seconds.Enabled = !busy;
        _hz.Enabled = !busy;
        _segment.Enabled = !busy;
        _waitForGame.Enabled = !busy;
        _output.Enabled = !busy;
        _browse.Enabled = !busy;
        BusyChanged?.Invoke(busy);
    }

    public void ApplyLanguage()
    {
        _secondsLabel.Text = Strings.Get("Gui_Profiler_Seconds");
        _hzLabel.Text = Strings.Get("Gui_Profiler_Hz");
        _segmentLabel.Text = Strings.Get("Gui_Profiler_Segment");
        _waitForGame.Text = Strings.Get("Gui_Profiler_Wait");
        _outputLabel.Text = Strings.Get("Gui_Profiler_Output");
        _browse.Text = Strings.Get("Gui_Browse");
        _start.Text = Strings.Get("Gui_Profiler_Start");
        _stop.Text = Strings.Get("Gui_Profiler_Stop");
        _hint.Text = Strings.Get("Gui_Profiler_Hint");
    }
}
