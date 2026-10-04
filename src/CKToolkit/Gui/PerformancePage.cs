using CKToolkit.Core.Common;
using CKToolkit.Core.Perf;
using CKToolkit.Gui.Layout;
using CKToolkit.I18n;

namespace CKToolkit.Gui;

public sealed class PerformancePage : ScrollPage
{
    private readonly Card _compatGroup = new();
    private readonly CheckBox _laa = new();
    private readonly CheckBox _videoFix = new();
    private readonly CheckBox _keepResolution = new();
    private readonly Card _resolutionGroup = new();
    private readonly CheckBox _hires = new();
    private readonly Label _capacityLabel = new();
    private readonly NumericUpDown _capacity = new();
    private readonly Label _resolutionLabel = new();
    private readonly ComboBox _resolution = new UiComboBox();
    private readonly Button _autoDetectBtn = new();
    private readonly RadioButton _autoSwitch = new();
    private readonly RadioButton _suppressDisplay = new();
    private readonly Label _warning = new();
    private readonly Card _animationGroup = new();
    private readonly CheckBox _noObjectAnimations = new();
    private readonly CheckBox _noWaterAnimation = new();
    private readonly Card _stabilityGroup = new();
    private readonly CheckBox _stabilityProtection = new();
    private readonly Label _stabilityDescription = new();
    private readonly CheckBox _experimentalStability = new();
    private readonly Label _experimentalDescription = new();

    private bool _isLoading;

    public PerformancePage()
    {
        BuildUi();
    }

    /// <summary>
    /// 版面（ISSUE-101）：四張卡片全部由內容決定高度。寬螢幕上「相容性修補」與「降低軟體光柵化負載」
    /// 並排，窄螢幕上自動改成上下排；說明文字依卡片寬度換行。以前的 26/30/44 百分比列高與
    /// 150/185/245px 的最小高度全部拿掉了——那些數字是照開發機的字型高度猜的，換到字比較大的
    /// 螢幕上，說明文字就被群組框切掉半行。
    /// </summary>
    private void BuildUi()
    {
        var top = new ColumnsPanel { Margin = Padding.Empty };
        top.Controls.Add(_compatGroup);
        top.Controls.Add(_animationGroup);
        _compatGroup.Add(Ui.Option(_laa));
        _compatGroup.Add(Ui.Option(_videoFix));
        _compatGroup.Add(Ui.Option(_keepResolution));
        _animationGroup.Add(Ui.Option(_noObjectAnimations));
        _animationGroup.Add(Ui.Option(_noWaterAnimation));
        Content.Add(top);

        _stabilityGroup.Add(Ui.Option(_stabilityProtection, bold: true));
        _stabilityProtection.CheckedChanged += (_, _) => RefreshEnabledState();
        _stabilityGroup.Add(Ui.Description(_stabilityDescription, Ui.TextSecondary));
        _stabilityGroup.Add(Ui.Option(_experimentalStability, bold: true)).Margin = new Padding(0, 8, 0, 2);
        _stabilityGroup.Add(Ui.Description(_experimentalDescription, Ui.Warning));
        Content.Add(_stabilityGroup);

        _resolutionGroup.Add(Ui.Option(_hires));
        _hires.CheckedChanged += (_, _) => RefreshEnabledState();

        _capacity.Minimum = 1600;
        _capacity.Maximum = CellGridPatch.MaxSurfaceWidth;
        _capacity.Increment = 160;
        _capacity.Width = 110;

        _resolution.DropDownStyle = ComboBoxStyle.DropDown;
        _resolution.Items.AddRange(["1024x768", "1152x864", "1280x1024", "1600x1200", "1920x1080", "2560x1440", "3840x2160"]);
        _resolution.Width = 170;
        _resolution.Margin = new Padding(0, 0, 8, 0);
        _resolution.SelectedIndexChanged += (_, _) => OnResolutionChanged();
        _resolution.TextChanged += (_, _) => OnResolutionChanged();
        _autoDetectBtn.AutoSize = true;
        _autoDetectBtn.UseMnemonic = false;
        _autoDetectBtn.Margin = Padding.Empty;
        _autoDetectBtn.Click += (_, _) => AutoDetectScreenResolution();
        var resolutionCell = Ui.ButtonRow(_resolution, _autoDetectBtn);
        resolutionCell.Margin = Padding.Empty;

        var fields = Ui.FieldTable();
        Ui.AddRow(fields, _capacityLabel, _capacity);
        Ui.AddRow(fields, _resolutionLabel, resolutionCell);
        fields.Margin = new Padding(0, 4, 0, 6);
        _resolutionGroup.Add(fields);

        _resolutionGroup.Add(Ui.Option(_autoSwitch));
        _resolutionGroup.Add(Ui.Option(_suppressDisplay));
        _resolutionGroup.Add(Ui.Text(_warning, Ui.Warning)).Margin = new Padding(0, 8, 0, 0);
        Content.Add(_resolutionGroup);
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
            _capacity.Value = Math.Clamp(config.Hires <= 0 ? 1920 : config.Hires, 1600, CellGridPatch.MaxSurfaceWidth);
            _autoSwitch.Checked = !string.Equals(config.DesktopMode, "suppress", StringComparison.OrdinalIgnoreCase);
            _suppressDisplay.Checked = !_autoSwitch.Checked;
            _noObjectAnimations.Checked = config.NoObjectAnimations;
            _noWaterAnimation.Checked = config.NoWaterAnimation;
            _stabilityProtection.Checked = config.StabilityProtection;
            _experimentalStability.Checked = config.ExperimentalStability;
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
        if (!TryParseResolution(resolution, out int width, out int height))
            throw new InvalidOperationException(Strings.Get("Gui_InvalidResolution", resolution));

        // CVXVisible 的 32px 網格只覆蓋 4096x2400。超過就會同時帶回捲動塗抹與
        // 75 列溢位閃退，寧可擋下存檔也不要寫出一份會讓遊戲壞掉的設定。
        if (!CellGridPatch.IsSurfaceSupported(width, height))
        {
            throw new InvalidOperationException(Strings.Get("Error_ResolutionExceedsGridCeiling",
                resolution, CellGridPatch.MaxSurfaceWidth, CellGridPatch.MaxSurfaceHeight));
        }

        config.Laa = _laa.Checked;
        config.VideoFix = _videoFix.Checked;
        config.KeepRes = _keepResolution.Checked;
        config.Hires = _hires.Checked ? (int)_capacity.Value : 0;
        config.Resolution = resolution;
        config.DesktopMode = _suppressDisplay.Checked ? "suppress" : "autoSwitch";
        config.NoObjectAnimations = _noObjectAnimations.Checked;
        config.NoWaterAnimation = _noWaterAnimation.Checked;
        config.StabilityProtection = _stabilityProtection.Checked;
        config.ExperimentalStability = _stabilityProtection.Checked && _experimentalStability.Checked;

        string[] stock = ["1024x768", "1152x864", "1280x1024", "1600x1200"];
        config.AddRes = stock.Contains(resolution, StringComparer.OrdinalIgnoreCase) ? [] : [resolution];
        if (_hires.Checked && width > config.Hires)
            throw new InvalidOperationException(Strings.Get("Gui_ResolutionOverCapacity", resolution, config.Hires));
    }

    public void ApplyLanguage()
    {
        _compatGroup.Title = Strings.Get("Gui_Perf_Compatibility");
        _laa.Text = Strings.Get("Gui_Perf_Laa");
        _videoFix.Text = Strings.Get("Gui_Perf_VideoFix");
        _keepResolution.Text = Strings.Get("Gui_Perf_KeepResolution");
        _resolutionGroup.Title = Strings.Get("Gui_Perf_ResolutionGroup");
        _hires.Text = Strings.Get("Gui_Perf_Hires");
        _capacityLabel.Text = Strings.Get("Gui_Perf_Capacity");
        _resolutionLabel.Text = Strings.Get("Gui_Perf_Resolution");
        _autoDetectBtn.Text = Strings.Get("Gui_Perf_AutoDetectScreen");
        _autoSwitch.Text = Strings.Get("Gui_Perf_AutoSwitch");
        _suppressDisplay.Text = Strings.Get("Gui_Perf_SuppressDisplay");
        _warning.Text = Strings.Get("Perf_HdCeilingNote");
        _animationGroup.Title = Strings.Get("Gui_Perf_Animations");
        _noObjectAnimations.Text = Strings.Get("Gui_Perf_NoObjectAnimations");
        _noWaterAnimation.Text = Strings.Get("Gui_Perf_NoWaterAnimation");
        _stabilityGroup.Title = Strings.Get("Gui_Perf_StabilityGroup");
        _stabilityProtection.Text = Strings.Get("Gui_Perf_StabilityProtection");
        _stabilityDescription.Text = Strings.Get("Gui_Perf_StabilityProtectionDesc");
        _experimentalStability.Text = Strings.Get("Gui_Perf_ExperimentalStability");
        _experimentalDescription.Text = Strings.Get("Gui_Perf_ExperimentalStabilityDesc");
    }

    private void RefreshEnabledState()
    {
        _capacity.Enabled = _hires.Checked;
        _experimentalStability.Enabled = _stabilityProtection.Checked;
        _experimentalDescription.Enabled = _stabilityProtection.Checked;
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
                _capacity.Value = Math.Clamp(width, 1600, CellGridPatch.MaxSurfaceWidth);
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
