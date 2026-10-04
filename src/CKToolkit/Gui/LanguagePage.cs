using CKToolkit.Core.Common;
using CKToolkit.Core.Lang;
using CKToolkit.Gui.Layout;
using CKToolkit.I18n;

namespace CKToolkit.Gui;

public sealed class LanguagePage : ScrollPage
{
    private readonly CheckBox _enabled = new();
    private readonly Label _packLabel = new();
    private readonly ComboBox _pack = new UiComboBox();
    private readonly Label _fontLabel = new();
    private readonly ComboBox _font = new UiComboBox();
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
        BuildUi();
        ReloadPacks();
    }

    /// <summary>版面（ISSUE-101）：一張卡片，欄位表＋說明文字，全部由內容決定高度。</summary>
    private void BuildUi()
    {
        var card = new Card();
        card.Add(Ui.Option(_enabled, bold: true));
        _enabled.CheckedChanged += (_, _) => RefreshEnabledState();

        _pack.DropDownStyle = ComboBoxStyle.DropDownList;
        _pack.SelectedIndexChanged += (_, _) => ShowPackDetails();
        _font.DropDownStyle = ComboBoxStyle.DropDown;
        _font.Items.AddRange(["微軟正黑體", "Microsoft JhengHei", "Noto Sans CJK TC", "Arial Unicode MS"]);
        var fields = Ui.FieldTable();
        fields.Margin = new Padding(0, 8, 0, 8);
        Ui.AddRow(fields, _packLabel, _pack);
        Ui.AddRow(fields, _fontLabel, _font);
        _pack.Margin = _font.Margin = new Padding(0, 3, 0, 3);
        // 下拉選單用固定的邏輯寬度（隨 DPI 換算）並靠左，不被表格撐到整列寬。
        _pack.Anchor = _font.Anchor = AnchorStyles.Left;
        _pack.Width = _font.Width = 360;
        card.Add(fields);

        card.Add(Ui.Text(_details, Color.FromArgb(51, 65, 85))).Margin = new Padding(0, 4, 0, 10);

        Ui.Button(_importBtn, Color.White, Ui.Accent, bold: true, minWidth: 120);
        Ui.Button(_exportBtn, Color.White, Ui.TextSecondary, bold: true, minWidth: 120);
        _importBtn.Click += (_, _) => HandleImportPack();
        _exportBtn.Click += (_, _) => HandleExportTemplate();
        card.Add(Ui.ButtonRow(_importBtn, _exportBtn));

        card.Add(Ui.Text(_extensionHint)).Margin = new Padding(0, 8, 0, 6);
        card.Add(Ui.Text(_compatHint, Ui.TextMuted));
        Content.Add(card);
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

            var fontFaces = new List<string>();
            if (!string.IsNullOrWhiteSpace(languagePack.Meta.Font.Face))
                fontFaces.Add(languagePack.Meta.Font.Face);
            if (languagePack.Meta.Font.FallbackFaces is not null)
                fontFaces.AddRange(languagePack.Meta.Font.FallbackFaces.Where(f => !string.IsNullOrWhiteSpace(f)));
            fontFaces.AddRange(["微軟正黑體", "Microsoft YaHei", "Meiryo", "Segoe UI", "Arial", "Tahoma"]);

            _font.Items.Clear();
            foreach (var face in fontFaces.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                _font.Items.Add(face);
            }

            if (string.IsNullOrWhiteSpace(_font.Text))
            {
                _font.Text = string.IsNullOrWhiteSpace(languagePack.Meta.Font.Face) ? "微軟正黑體" : languagePack.Meta.Font.Face;
            }
            else if (!fontFaces.Contains(_font.Text, StringComparer.OrdinalIgnoreCase))
            {
                // The list is only a set of suggestions. Preserve an explicitly configured
                // system font even when the current language pack did not advertise it.
                _font.Items.Add(_font.Text);
            }
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
    private readonly ComboBox _langCombo = new UiComboBox();
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
        // 所有尺寸都是 96 DPI 的邏輯像素，結尾由 Ui.EndForm 一次換算（ISSUE-101）。
        // 可縮放：英文說明比中文長，固定大小的對話框會把按鈕擠出畫面。
        Ui.BeginForm(this);
        ClientSize = new Size(560, 340);
        MinimumSize = new Size(500, 320);
        FormBorderStyle = FormBorderStyle.Sizable;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        BackColor = Color.FromArgb(248, 250, 252);

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
        _headerTitle.Font = Ui.UiFont(12F, FontStyle.Bold);
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
        _exportBtn.Font = Ui.UiFont(9F, FontStyle.Bold);
        _exportBtn.Margin = new Padding(0, 0, 10, 0);
        _exportBtn.Click += (_, _) => DoExport();

        buttonPanel.Controls.Add(_cancelBtn);
        buttonPanel.Controls.Add(_exportBtn);

        layout.Controls.Add(buttonPanel, 0, 4);
        layout.SetColumnSpan(buttonPanel, 3);

        Controls.Add(layout);

        AcceptButton = _exportBtn;
        CancelButton = _cancelBtn;
        Ui.EndForm(this);
        Load += (_, _) => Ui.FitToScreen(this);
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
