using CKToolkit.Core.Common;
using CKToolkit.I18n;

namespace CKToolkit.Gui;

public sealed class PerformancePage : UserControl
{
    private readonly GroupBox _compatGroup = new();
    private readonly CheckBox _laa = new();
    private readonly CheckBox _videoFix = new();
    private readonly CheckBox _keepResolution = new();
    private readonly GroupBox _resolutionGroup = new();
    private readonly CheckBox _hires = new();
    private readonly Label _capacityLabel = new();
    private readonly NumericUpDown _capacity = new();
    private readonly Label _resolutionLabel = new();
    private readonly ComboBox _resolution = new();
    private readonly Button _autoDetectBtn = new();
    private readonly RadioButton _autoSwitch = new();
    private readonly RadioButton _suppressDisplay = new();
    private readonly Label _warning = new();
    private readonly GroupBox _animationGroup = new();
    private readonly CheckBox _noObjectAnimations = new();
    private readonly CheckBox _noWaterAnimation = new();

    private bool _isLoading;

    public PerformancePage()
    {
        AutoScroll = true;
        BackColor = Color.White;
        Padding = new Padding(18);
        BuildUi();
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, RowCount = 2 };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));

        ConfigureGroup(_compatGroup);
        var compat = Stack(_laa, _videoFix, _keepResolution);
        _compatGroup.Controls.Add(compat);

        ConfigureGroup(_animationGroup);
        _animationGroup.Controls.Add(Stack(_noObjectAnimations, _noWaterAnimation));

        ConfigureGroup(_resolutionGroup);
        _resolutionGroup.MinimumSize = new Size(0, 245);
        var res = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, Padding = new Padding(8) };
        res.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        res.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        _hires.AutoSize = true;
        _hires.CheckedChanged += (_, _) => RefreshEnabledState();
        res.Controls.Add(_hires, 0, 0);
        res.SetColumnSpan(_hires, 2);
        _capacityLabel.AutoSize = true;
        _capacityLabel.Anchor = AnchorStyles.Left;
        res.Controls.Add(_capacityLabel, 0, 1);
        _capacity.Minimum = 1600;
        _capacity.Maximum = 16384;
        _capacity.Increment = 160;
        _capacity.Width = 120;
        res.Controls.Add(_capacity, 1, 1);
        _resolutionLabel.AutoSize = true;
        _resolutionLabel.Anchor = AnchorStyles.Left;
        res.Controls.Add(_resolutionLabel, 0, 2);

        var resPanel = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0)
        };
        _resolution.DropDownStyle = ComboBoxStyle.DropDown;
        _resolution.Items.AddRange(["1024x768", "1152x864", "1280x1024", "1600x1200", "1920x1080", "2560x1440", "3840x2160"]);
        _resolution.Width = 180;
        _resolution.SelectedIndexChanged += (_, _) => OnResolutionChanged();
        _resolution.TextChanged += (_, _) => OnResolutionChanged();

        _autoDetectBtn.AutoSize = true;
        _autoDetectBtn.Margin = new Padding(6, 0, 0, 0);
        _autoDetectBtn.Click += (_, _) => AutoDetectScreenResolution();

        resPanel.Controls.Add(_resolution);
        resPanel.Controls.Add(_autoDetectBtn);
        res.Controls.Add(resPanel, 1, 2);

        _autoSwitch.AutoSize = true;
        _suppressDisplay.AutoSize = true;
        res.Controls.Add(_autoSwitch, 0, 3);
        res.SetColumnSpan(_autoSwitch, 2);
        res.Controls.Add(_suppressDisplay, 0, 4);
        res.SetColumnSpan(_suppressDisplay, 2);
        _warning.AutoSize = true;
        _warning.MaximumSize = new Size(760, 0);
        _warning.ForeColor = Color.FromArgb(180, 83, 9);
        _warning.Padding = new Padding(0, 10, 0, 0);
        res.Controls.Add(_warning, 0, 5);
        res.SetColumnSpan(_warning, 2);
        _resolutionGroup.Controls.Add(res);

        root.Controls.Add(_compatGroup, 0, 0);
        root.Controls.Add(_animationGroup, 1, 0);
        root.Controls.Add(_resolutionGroup, 0, 1);
        root.SetColumnSpan(_resolutionGroup, 2);
        Controls.Add(root);
    }

    private static FlowLayoutPanel Stack(params Control[] controls)
    {
        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown,
            WrapContents = false, AutoScroll = true, Padding = new Padding(10)
        };
        foreach (Control control in controls)
        {
            control.AutoSize = true;
            control.Margin = new Padding(3, 5, 3, 7);
            panel.Controls.Add(control);
        }
        return panel;
    }

    private static void ConfigureGroup(GroupBox group)
    {
        group.Dock = DockStyle.Fill;
        group.Padding = new Padding(10);
        group.Margin = new Padding(6);
        group.MinimumSize = new Size(0, 150);
    }

    public void LoadConfig(PerfConfig config)
    {
        _isLoading = true;
        try
        {
            _laa.Checked = config.Laa;
            _videoFix.Checked = config.VideoFix;
            _keepResolution.Checked = config.KeepRes;
            _resolution.Text = string.IsNullOrWhiteSpace(config.Resolution) ? "1920x1080" : config.Resolution;
            _hires.Checked = config.Hires >= 1600;
            _capacity.Value = Math.Clamp(config.Hires <= 0 ? 1920 : config.Hires, 1600, 16384);
            _autoSwitch.Checked = !string.Equals(config.DesktopMode, "suppress", StringComparison.OrdinalIgnoreCase);
            _suppressDisplay.Checked = !_autoSwitch.Checked;
            _noObjectAnimations.Checked = config.NoObjectAnimations;
            _noWaterAnimation.Checked = config.NoWaterAnimation;
            RefreshEnabledState();
        }
        finally
        {
            _isLoading = false;
        }
    }

    public void SaveConfig(PerfConfig config)
    {
        string resolution = _resolution.Text.Trim();
        if (!TryParseResolution(resolution, out int width, out _))
            throw new InvalidOperationException(Strings.Get("Gui_InvalidResolution", resolution));

        config.Laa = _laa.Checked;
        config.VideoFix = _videoFix.Checked;
        config.KeepRes = _keepResolution.Checked;
        config.Hires = _hires.Checked ? (int)_capacity.Value : 0;
        config.Resolution = resolution;
        config.DesktopMode = _suppressDisplay.Checked ? "suppress" : "autoSwitch";
        config.NoObjectAnimations = _noObjectAnimations.Checked;
        config.NoWaterAnimation = _noWaterAnimation.Checked;

        string[] stock = ["1024x768", "1152x864", "1280x1024", "1600x1200"];
        config.AddRes = stock.Contains(resolution, StringComparer.OrdinalIgnoreCase) ? [] : [resolution];
        if (_hires.Checked && width > config.Hires)
            throw new InvalidOperationException(Strings.Get("Gui_ResolutionOverCapacity", resolution, config.Hires));
    }

    public void ApplyLanguage()
    {
        _compatGroup.Text = Strings.Get("Gui_Perf_Compatibility");
        _laa.Text = Strings.Get("Gui_Perf_Laa");
        _videoFix.Text = Strings.Get("Gui_Perf_VideoFix");
        _keepResolution.Text = Strings.Get("Gui_Perf_KeepResolution");
        _resolutionGroup.Text = Strings.Get("Gui_Perf_ResolutionGroup");
        _hires.Text = Strings.Get("Gui_Perf_Hires");
        _capacityLabel.Text = Strings.Get("Gui_Perf_Capacity");
        _resolutionLabel.Text = Strings.Get("Gui_Perf_Resolution");
        _autoDetectBtn.Text = Strings.Get("Gui_Perf_AutoDetectScreen");
        _autoSwitch.Text = Strings.Get("Gui_Perf_AutoSwitch");
        _suppressDisplay.Text = Strings.Get("Gui_Perf_SuppressDisplay");
        _warning.Text = Strings.Get("Perf_HdCeilingWarning");
        _animationGroup.Text = Strings.Get("Gui_Perf_Animations");
        _noObjectAnimations.Text = Strings.Get("Gui_Perf_NoObjectAnimations");
        _noWaterAnimation.Text = Strings.Get("Gui_Perf_NoWaterAnimation");
    }

    private void RefreshEnabledState()
    {
        _capacity.Enabled = _hires.Checked;
    }

    private void OnResolutionChanged()
    {
        if (_isLoading) return;

        string text = _resolution.Text.Trim();
        if (TryParseResolution(text, out int width, out _))
        {
            if (width > 1600)
            {
                _hires.Checked = true;
                _capacity.Value = Math.Clamp(width, 1600, 16384);
            }
            else
            {
                _capacity.Value = 1600;
            }
        }
    }

    private void AutoDetectScreenResolution()
    {
        var bounds = Screen.PrimaryScreen?.Bounds ?? Screen.AllScreens.FirstOrDefault()?.Bounds;
        if (bounds is { Width: > 0, Height: > 0 })
        {
            string detRes = $"{bounds.Value.Width}x{bounds.Value.Height}";
            if (!_resolution.Items.Contains(detRes))
            {
                _resolution.Items.Add(detRes);
            }
            _resolution.Text = detRes;
        }
    }

    private static bool TryParseResolution(string text, out int width, out int height)
    {
        width = height = 0;
        string[] parts = text.ToLowerInvariant().Split('x');
        return parts.Length == 2 && int.TryParse(parts[0], out width) && int.TryParse(parts[1], out height)
            && width >= 640 && height >= 480;
    }
}
