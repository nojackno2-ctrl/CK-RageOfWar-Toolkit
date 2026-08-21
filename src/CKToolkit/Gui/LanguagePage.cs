using CKToolkit.Core.Common;
using CKToolkit.Core.Lang;
using CKToolkit.I18n;

namespace CKToolkit.Gui;

public sealed class LanguagePage : UserControl
{
    private readonly CheckBox _enabled = new();
    private readonly Label _packLabel = new();
    private readonly ComboBox _pack = new();
    private readonly Label _fontLabel = new();
    private readonly ComboBox _font = new();
    private readonly Label _details = new();
    private readonly Button _importBtn = new();
    private readonly Button _exportBtn = new();
    private readonly Label _extensionHint = new();
    private readonly Label _compatHint = new();
    private Dictionary<string, LanguagePack> _packs = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 主視窗提供的目前遊戲路徑委派。
    /// </summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Func<string?>? GameDirProvider { get; set; }

    public LanguagePage()
    {
        BackColor = Color.White;
        Padding = new Padding(24);
        BuildUi();
        ReloadPacks();
    }

    private void BuildUi()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 2,
            Padding = new Padding(16),
            BackColor = Color.FromArgb(248, 250, 252)
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

        _enabled.AutoSize = true;
        _enabled.Font = new Font(Font, FontStyle.Bold);
        _enabled.CheckedChanged += (_, _) => RefreshEnabledState();
        panel.Controls.Add(_enabled, 0, 0);
        panel.SetColumnSpan(_enabled, 2);

        _packLabel.AutoSize = true;
        _packLabel.Anchor = AnchorStyles.Left;
        _packLabel.Margin = new Padding(0, 14, 12, 0);
        panel.Controls.Add(_packLabel, 0, 1);
        _pack.DropDownStyle = ComboBoxStyle.DropDownList;
        _pack.Width = 360;
        _pack.Margin = new Padding(0, 10, 0, 0);
        _pack.SelectedIndexChanged += (_, _) => ShowPackDetails();
        panel.Controls.Add(_pack, 1, 1);

        _fontLabel.AutoSize = true;
        _fontLabel.Anchor = AnchorStyles.Left;
        _fontLabel.Margin = new Padding(0, 14, 12, 0);
        panel.Controls.Add(_fontLabel, 0, 2);
        _font.DropDownStyle = ComboBoxStyle.DropDown;
        _font.Items.AddRange(["微軟正黑體", "Microsoft JhengHei", "Noto Sans CJK TC", "Arial Unicode MS"]);
        _font.Width = 360;
        _font.Margin = new Padding(0, 10, 0, 0);
        panel.Controls.Add(_font, 1, 2);

        _details.AutoSize = true;
        _details.MaximumSize = new Size(800, 0);
        _details.ForeColor = Color.FromArgb(51, 65, 85);
        _details.Margin = new Padding(0, 16, 0, 0);
        panel.Controls.Add(_details, 0, 3);
        panel.SetColumnSpan(_details, 2);

        // 工具列按鈕區：匯入語言包 與 匯出翻譯範本
        var actionsPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0, 16, 0, 0)
        };

        ConfigureToolbarButton(_importBtn, Color.White, Color.FromArgb(37, 99, 235));
        ConfigureToolbarButton(_exportBtn, Color.White, Color.FromArgb(71, 85, 105));

        _importBtn.Click += (_, _) => HandleImportPack();
        _exportBtn.Click += (_, _) => HandleExportTemplate();

        actionsPanel.Controls.Add(_importBtn);
        actionsPanel.Controls.Add(_exportBtn);
        panel.Controls.Add(actionsPanel, 0, 4);
        panel.SetColumnSpan(actionsPanel, 2);

        // 擴充說明
        _extensionHint.AutoSize = true;
        _extensionHint.MaximumSize = new Size(800, 0);
        _extensionHint.ForeColor = Color.FromArgb(71, 85, 105);
        _extensionHint.Margin = new Padding(0, 16, 0, 0);
        panel.Controls.Add(_extensionHint, 0, 5);
        panel.SetColumnSpan(_extensionHint, 2);

        // 相容性說明
        _compatHint.AutoSize = true;
        _compatHint.MaximumSize = new Size(800, 0);
        _compatHint.ForeColor = Color.FromArgb(100, 116, 139);
        _compatHint.Margin = new Padding(0, 8, 0, 0);
        panel.Controls.Add(_compatHint, 0, 6);
        panel.SetColumnSpan(_compatHint, 2);

        Controls.Add(panel);
    }

    private static void ConfigureToolbarButton(Button btn, Color back, Color fore)
    {
        btn.AutoSize = true;
        btn.MinimumSize = new Size(130, 34);
        btn.FlatStyle = FlatStyle.Flat;
        btn.FlatAppearance.BorderColor = Color.FromArgb(203, 213, 225);
        btn.BackColor = back;
        btn.ForeColor = fore;
        btn.Font = new Font("Microsoft JhengHei UI", 9F, FontStyle.Bold);
        btn.Margin = new Padding(0, 0, 10, 0);
        btn.Cursor = Cursors.Hand;
    }

    private void ReloadPacks()
    {
        string? selected = (_pack.SelectedItem as PackChoice)?.Id;
        _packs = PackLoader.DiscoverAll();
        _pack.Items.Clear();
        foreach (LanguagePack languagePack in _packs.Values.OrderBy(p => p.Meta.NativeName))
        {
            string name = string.IsNullOrWhiteSpace(languagePack.Meta.NativeName)
                ? languagePack.Meta.Name : languagePack.Meta.NativeName;
            _pack.Items.Add(new PackChoice(languagePack.Meta.Id, $"{name} ({languagePack.Meta.Id})"));
        }
        if (_pack.Items.Count > 0)
        {
            int index = Enumerable.Range(0, _pack.Items.Count)
                .FirstOrDefault(i => (_pack.Items[i] as PackChoice)?.Id.Equals(selected, StringComparison.OrdinalIgnoreCase) == true, -1);
            _pack.SelectedIndex = index >= 0 ? index : 0;
        }
    }

    public void LoadConfig(LangConfig config)
    {
        ReloadPacks();
        _enabled.Checked = !string.IsNullOrWhiteSpace(config.Pack);
        if (_enabled.Checked)
        {
            int found = Enumerable.Range(0, _pack.Items.Count)
                .FirstOrDefault(i => (_pack.Items[i] as PackChoice)?.Id.Equals(config.Pack, StringComparison.OrdinalIgnoreCase) == true, -1);
            if (found >= 0) _pack.SelectedIndex = found;
            else
            {
                _pack.Items.Add(new PackChoice(config.Pack, config.Pack));
                _pack.SelectedIndex = _pack.Items.Count - 1;
            }
        }
        _font.Text = string.IsNullOrWhiteSpace(config.FontFace) ? "微軟正黑體" : config.FontFace;
        RefreshEnabledState();
        ShowPackDetails();
    }

    public void SaveConfig(LangConfig config)
    {
        if (!_enabled.Checked)
        {
            config.Pack = string.Empty;
            return;
        }
        if (_pack.SelectedItem is not PackChoice choice)
            throw new InvalidOperationException(Strings.Get("Gui_Lang_NoPack"));
        if (!_packs.ContainsKey(choice.Id))
            throw new InvalidOperationException(Strings.Get("Error_LangPackNotFound", choice.Id));
        config.Pack = choice.Id;
        config.FontFace = _font.Text.Trim();
        if (string.IsNullOrWhiteSpace(config.FontFace))
            throw new InvalidOperationException(Strings.Get("Gui_Lang_NoFont"));
    }

    public void ApplyLanguage()
    {
        _enabled.Text = Strings.Get("Gui_Lang_Enable");
        _packLabel.Text = Strings.Get("Gui_Lang_Pack");
        _fontLabel.Text = Strings.Get("Gui_Lang_Font");
        _importBtn.Text = Strings.Get("Gui_Lang_Import");
        _exportBtn.Text = Strings.Get("Gui_Lang_ExportTemplate");
        _extensionHint.Text = Strings.Get("Gui_Lang_ExtensionHint", Path.Combine(AppContext.BaseDirectory, "langpacks", "<id>"));
        _compatHint.Text = Strings.Get("Gui_Lang_CompatHint");
        ShowPackDetails();
    }

    private void RefreshEnabledState()
    {
        _pack.Enabled = _enabled.Checked;
        _font.Enabled = _enabled.Checked;
    }

    private void ShowPackDetails()
    {
        if (_pack.SelectedItem is PackChoice choice && _packs.TryGetValue(choice.Id, out LanguagePack? languagePack))
        {
            string authors = languagePack.Meta.Authors.Count > 0 ? string.Join(", ", languagePack.Meta.Authors) : "-";
            _details.Text = Strings.Get("Gui_Lang_Details", languagePack.Meta.Version, authors,
                languagePack.IsBuiltIn ? Strings.Get("Gui_Lang_BuiltIn") : languagePack.SourcePath ?? "-");
        }
        else _details.Text = Strings.Get("Gui_Lang_NoPack");
    }

    private void HandleImportPack()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = Strings.Get("Gui_Lang_SelectPackFolder"),
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false
        };

        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        string sourceDir = dialog.SelectedPath;
        var importRes = LangPackService.ImportPack(
            sourceDir,
            null,
            (id, targetPath) =>
            {
                string msg = Strings.Get("Gui_Lang_ImportOverwriteConfirm", id, targetPath);
                string title = Strings.Get("Gui_Lang_ImportTitle");
                return MessageBox.Show(this, msg, title, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes;
            });

        if (!importRes.Success || importRes.Value is null)
        {
            if (importRes.ExitCode == ExitCodes.Success) return;
            MessageBox.Show(this, importRes.ErrorMessage ?? Strings.Get("Error_LangImportFailed", "-"),
                Strings.Get("Gui_ErrorTitle"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        var importedPack = importRes.Value;
        ReloadPacks();

        int idx = Enumerable.Range(0, _pack.Items.Count)
            .FirstOrDefault(i => (_pack.Items[i] as PackChoice)?.Id.Equals(importedPack.Meta.Id, StringComparison.OrdinalIgnoreCase) == true, -1);
        if (idx >= 0)
        {
            _pack.SelectedIndex = idx;
        }

        if (!string.IsNullOrWhiteSpace(importedPack.Meta.Font.Face))
        {
            _font.Text = importedPack.Meta.Font.Face;
        }

        _enabled.Checked = true;
        RefreshEnabledState();
        ShowPackDetails();

        string successMsg = Strings.Get("Gui_Lang_ImportSuccess", importedPack.Meta.Id, importedPack.SourcePath ?? importedPack.Meta.Id);
        if (importRes.Warnings.Count > 0)
            successMsg += Environment.NewLine + Environment.NewLine + string.Join(Environment.NewLine, importRes.Warnings);
        MessageBox.Show(this, successMsg, Strings.Get("Gui_Lang_ImportTitle"), MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void HandleExportTemplate()
    {
        string? gameDir = GameDirProvider?.Invoke();
        if (string.IsNullOrWhiteSpace(gameDir) || !GamePaths.IsGameDir(gameDir))
        {
            MessageBox.Show(this, Strings.Get("Error_GameNotFound"),
                Strings.Get("Gui_ErrorTitle"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        string pakPath = GamePaths.GetLocalPakPath(gameDir);
        if (!File.Exists(pakPath))
        {
            MessageBox.Show(this, Strings.Get("Error_GameNotFound"),
                Strings.Get("Gui_ErrorTitle"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        HmmPak localPak;
        try
        {
            localPak = HmmPak.Load(pakPath);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, Strings.Get("Error_LangPakReadFailed", ex.Message),
                Strings.Get("Gui_ErrorTitle"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        var stockLangs = LangInstaller.DetectExportableStockLanguages(localPak);
        if (stockLangs.Count == 0)
        {
            MessageBox.Show(this, Strings.Get("Gui_Lang_NoStockLanguages"),
                Strings.Get("Gui_ErrorTitle"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        using var exportDialog = new ExportTemplateDialog(localPak, stockLangs);
        exportDialog.ShowDialog(this);
    }

    private sealed record PackChoice(string Id, string Label)
    {
        public override string ToString() => Label;
    }
}

/// <summary>
/// 匯出翻譯範本對話框。
/// 僅列出目前 local.pak 真正存在之官方語言供使用者選取，預設為 ENGLISH。
/// </summary>
internal sealed class ExportTemplateDialog : Form
{
    private readonly HmmPak _localPak;
    private readonly IReadOnlyList<string> _availableLangs;

    private readonly Label _headerTitle = new();
    private readonly Label _headerDesc = new();
    private readonly Label _langLabel = new();
    private readonly ComboBox _langCombo = new();
    private readonly Label _dirLabel = new();
    private readonly TextBox _dirBox = new();
    private readonly Button _browseBtn = new();
    private readonly Button _exportBtn = new();
    private readonly Button _cancelBtn = new();

    public ExportTemplateDialog(HmmPak localPak, IReadOnlyList<string> availableLangs)
    {
        _localPak = localPak;
        _availableLangs = availableLangs;
        BuildUi();
        ApplyLanguage();
    }

    private void BuildUi()
    {
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(560, 330);
        MinimumSize = new Size(500, 310);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        BackColor = Color.FromArgb(248, 250, 252);
        Font = new Font("Microsoft JhengHei UI", 9F);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 5,
            Padding = new Padding(20)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        // 標題
        _headerTitle.AutoSize = true;
        _headerTitle.Font = new Font(Font.FontFamily, 12F, FontStyle.Bold);
        _headerTitle.ForeColor = Color.FromArgb(15, 23, 42);
        layout.Controls.Add(_headerTitle, 0, 0);
        layout.SetColumnSpan(_headerTitle, 3);

        // 說明文字
        _headerDesc.AutoSize = true;
        _headerDesc.MaximumSize = new Size(510, 0);
        _headerDesc.ForeColor = Color.FromArgb(71, 85, 105);
        _headerDesc.Margin = new Padding(0, 4, 0, 16);
        layout.Controls.Add(_headerDesc, 0, 1);
        layout.SetColumnSpan(_headerDesc, 3);

        // 來源官方語言選單
        _langLabel.AutoSize = true;
        _langLabel.Anchor = AnchorStyles.Left;
        _langLabel.Margin = new Padding(0, 6, 12, 10);
        layout.Controls.Add(_langLabel, 0, 2);

        _langCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _langCombo.Dock = DockStyle.Fill;
        _langCombo.Margin = new Padding(0, 0, 0, 10);
        foreach (string lang in _availableLangs)
        {
            _langCombo.Items.Add(lang);
        }
        int defaultIdx = _availableLangs.ToList().FindIndex(l => l.Equals("ENGLISH", StringComparison.OrdinalIgnoreCase));
        _langCombo.SelectedIndex = defaultIdx >= 0 ? defaultIdx : 0;
        layout.Controls.Add(_langCombo, 1, 2);
        layout.SetColumnSpan(_langCombo, 2);

        // 輸出目錄
        _dirLabel.AutoSize = true;
        _dirLabel.Anchor = AnchorStyles.Left;
        _dirLabel.Margin = new Padding(0, 6, 12, 16);
        layout.Controls.Add(_dirLabel, 0, 3);

        _dirBox.Dock = DockStyle.Fill;
        _dirBox.Margin = new Padding(0, 2, 8, 16);
        string defaultDesktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        _dirBox.Text = Path.Combine(defaultDesktop, "CK_Language_Template");
        layout.Controls.Add(_dirBox, 1, 3);

        _browseBtn.AutoSize = true;
        _browseBtn.Margin = new Padding(0, 0, 0, 16);
        _browseBtn.Click += (_, _) => BrowseOutputFolder();
        layout.Controls.Add(_browseBtn, 2, 3);

        // 按鈕區
        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Margin = new Padding(0, 12, 0, 0)
        };

        _cancelBtn.AutoSize = true;
        _cancelBtn.MinimumSize = new Size(90, 34);
        _cancelBtn.FlatStyle = FlatStyle.Flat;
        _cancelBtn.FlatAppearance.BorderColor = Color.FromArgb(203, 213, 225);
        _cancelBtn.BackColor = Color.White;
        _cancelBtn.ForeColor = Color.FromArgb(51, 65, 85);
        _cancelBtn.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };

        _exportBtn.AutoSize = true;
        _exportBtn.MinimumSize = new Size(100, 34);
        _exportBtn.FlatStyle = FlatStyle.Flat;
        _exportBtn.FlatAppearance.BorderColor = Color.FromArgb(37, 99, 235);
        _exportBtn.BackColor = Color.FromArgb(37, 99, 235);
        _exportBtn.ForeColor = Color.White;
        _exportBtn.Font = new Font(Font, FontStyle.Bold);
        _exportBtn.Margin = new Padding(0, 0, 10, 0);
        _exportBtn.Click += (_, _) => DoExport();

        buttonPanel.Controls.Add(_cancelBtn);
        buttonPanel.Controls.Add(_exportBtn);

        layout.Controls.Add(buttonPanel, 0, 4);
        layout.SetColumnSpan(buttonPanel, 3);

        Controls.Add(layout);

        AcceptButton = _exportBtn;
        CancelButton = _cancelBtn;
    }

    private void ApplyLanguage()
    {
        Text = Strings.Get("Gui_Lang_ExportTitle");
        _headerTitle.Text = Strings.Get("Gui_Lang_ExportTitle");
        _headerDesc.Text = Strings.Get("Gui_Lang_ExportHint");
        _langLabel.Text = Strings.Get("Gui_Lang_ExportSourceLang");
        _dirLabel.Text = Strings.Get("Gui_Lang_ExportOutDir");
        _browseBtn.Text = Strings.Get("Gui_Browse");
        _exportBtn.Text = Strings.Get("Gui_Lang_ExportBtn");
        _cancelBtn.Text = Strings.Get("Gui_Cancel");
    }

    private void BrowseOutputFolder()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = Strings.Get("Gui_Lang_ExportSelectFolder"),
            UseDescriptionForTitle = true,
            SelectedPath = Directory.Exists(_dirBox.Text.Trim()) ? _dirBox.Text.Trim() : string.Empty
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _dirBox.Text = dialog.SelectedPath;
        }
    }

    private void DoExport()
    {
        string outDir = _dirBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(outDir))
        {
            MessageBox.Show(this, Strings.Get("Error_ExportTemplateMissingOut"),
                Strings.Get("Gui_ErrorTitle"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        string selectedLang = _langCombo.SelectedItem?.ToString() ?? "ENGLISH";

        try
        {
            LangInstaller.ExportTemplate(_localPak, selectedLang, outDir);
            MessageBox.Show(this, Strings.Get("Gui_Lang_ExportSuccess", outDir),
                Strings.Get("Gui_Lang_ExportTitle"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, Strings.Get("Error_GeneralFailure", ex.Message),
                Strings.Get("Gui_ErrorTitle"), MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
