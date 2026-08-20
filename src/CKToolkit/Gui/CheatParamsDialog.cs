using System.Globalization;
using CKToolkit.Core.Trainer;
using CKToolkit.I18n;

namespace CKToolkit.Gui;

/// <summary>
/// 修改器作弊參數的視覺化圖形設定對話框。
/// 支援數值微調（NumericUpDown）、62 種全遊戲單位的分類挑選（最多 20 種）、
/// 初始等級設定（Level 1~100）與 23 種攜帶物品裝備挑選（最多 6 件）。
/// 所有單位與物品均以 3 欄等寬表格排列整齊，並支援即時搜尋與預設組合。
/// </summary>
public sealed class CheatParamsDialog : Form
{
    private readonly Cheat _cheat;
    private readonly Dictionary<string, string> _parameters;
    private readonly Dictionary<string, Control> _inputControls = new(StringComparer.Ordinal);
    private readonly List<CheckBox> _unitCheckBoxes = [];
    private readonly List<CheckBox> _itemCheckBoxes = [];
    private readonly HashSet<string> _selectedUnits = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _selectedItems = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (TableLayoutPanel Panel, List<CheckBox> AllBoxes)> _unitCategoryGrids = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (TableLayoutPanel Panel, List<CheckBox> AllBoxes)> _itemCategoryGrids = new(StringComparer.Ordinal);
    private TableLayoutPanel? _itemsGrid;
    private Label? _unitCountLabel;
    private Label? _itemCountLabel;
    private TextBox? _searchBox;
    private readonly ToolTip _toolTip = new() { AutoPopDelay = 8000, InitialDelay = 300, ReshowDelay = 100 };

    public IReadOnlyDictionary<string, string> ResultParameters => _parameters;

    public CheatParamsDialog(Cheat cheat, IReadOnlyDictionary<string, string>? currentParameters)
    {
        _cheat = cheat;
        _parameters = new Dictionary<string, string>(StringComparer.Ordinal);

        // 填入預設值
        foreach (var param in cheat.Parameters.Where(p => !p.Hidden))
        {
            string val = Convert.ToString(param.Default, CultureInfo.InvariantCulture) ?? string.Empty;
            if (currentParameters is not null && currentParameters.TryGetValue(param.Name, out string? configured) && !string.IsNullOrWhiteSpace(configured))
                val = configured;
            _parameters[param.Name] = val;
        }

        InitializeUi();
    }

    private void InitializeUi()
    {
        bool isZh = Strings.EffectiveLanguage == "zh-TW";
        string cheatTitle = isZh ? _cheat.Name : _cheat.Id;
        Text = Strings.Get("Gui_Trainer_DialogTitle", cheatTitle);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        Font = new Font("Microsoft JhengHei UI", 9F);
        BackColor = Color.White;

        bool isSpawnUnit = _cheat.Id == Cheats.SpawnUnitId;
        bool isSpawnItem = _cheat.Id == Cheats.SpawnItemId;
        Size = (isSpawnUnit || isSpawnItem) ? new Size(820, 720) : new Size(560, 380);
        MinimumSize = Size;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(16),
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        // 頂部：名稱與說明
        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 10),
        };
        header.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        header.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        header.RowStyles.Add(new RowStyle(SizeType.Absolute, 10F));

        var titleLabel = new Label
        {
            Text = cheatTitle,
            Font = new Font(Font, FontStyle.Bold),
            ForeColor = Color.FromArgb(30, 41, 59),
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 4),
        };
        var descLabel = new Label
        {
            Text = isZh ? _cheat.Description : _cheat.Id,
            ForeColor = Color.FromArgb(71, 85, 105),
            AutoSize = true,
            MaximumSize = new Size((isSpawnUnit || isSpawnItem) ? 770 : 510, 0),
            Margin = new Padding(0, 0, 0, 6),
        };
        var divider = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 1,
            BackColor = Color.FromArgb(226, 232, 240),
        };

        header.Controls.Add(titleLabel, 0, 0);
        header.Controls.Add(descLabel, 0, 1);
        header.Controls.Add(divider, 0, 2);
        root.Controls.Add(header, 0, 0);

        // 中間內容
        if (isSpawnUnit)
            root.Controls.Add(BuildSpawnUnitContent(), 0, 1);
        else if (isSpawnItem)
            root.Controls.Add(BuildSpawnItemContent(), 0, 1);
        else
            root.Controls.Add(BuildGenericContent(), 0, 1);

        // 底部按鈕
        root.Controls.Add(BuildBottomButtons(), 0, 2);

        Controls.Add(root);
    }

    private Control BuildGenericContent()
    {
        bool isZh = Strings.EffectiveLanguage == "zh-TW";
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            AutoScroll = true,
            Padding = new Padding(4),
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150F));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

        int row = 0;
        foreach (var param in _cheat.Parameters.Where(p => !p.Hidden))
        {
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var lbl = new Label
            {
                Text = param.DisplayLabel(!isZh),
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Font = new Font(Font, FontStyle.Bold),
                Margin = new Padding(4, 8, 12, 8),
            };

            var num = new NumericUpDown
            {
                Minimum = param.Minimum,
                Maximum = param.Maximum,
                ThousandsSeparator = true,
                Anchor = AnchorStyles.Left | AnchorStyles.Right,
                Margin = new Padding(4, 6, 12, 6),
            };

            if (_parameters.TryGetValue(param.Name, out string? valStr) &&
                decimal.TryParse(valStr, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal val))
            {
                num.Value = Math.Clamp(val, param.Minimum, param.Maximum);
            }
            else if (param.Default is decimal or int or long)
            {
                num.Value = Math.Clamp(Convert.ToDecimal(param.Default, CultureInfo.InvariantCulture), param.Minimum, param.Maximum);
            }

            _inputControls[param.Name] = num;

            var hint = new Label
            {
                Text = Strings.Get("Gui_Trainer_ParamRange",
                    Convert.ToString(param.Default, CultureInfo.InvariantCulture) ?? "",
                    param.Minimum.ToString("N0", CultureInfo.CurrentCulture),
                    param.Maximum.ToString("N0", CultureInfo.CurrentCulture)),
                ForeColor = Color.FromArgb(100, 116, 139),
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(4, 8, 4, 8),
            };

            panel.Controls.Add(lbl, 0, row);
            panel.Controls.Add(num, 1, row);
            panel.Controls.Add(hint, 2, row);
            row++;
        }

        return panel;
    }

    private Control BuildSpawnUnitContent()
    {
        bool isZh = Strings.EffectiveLanguage == "zh-TW";
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
        };
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        // 1. 數量與等級設定列
        var countRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            WrapContents = false,
            Margin = new Padding(0, 0, 0, 8),
        };

        var countLabel = new Label
        {
            Text = isZh ? "每次生成數量：" : "Spawn Count:",
            AutoSize = true,
            Font = new Font(Font, FontStyle.Bold),
            Margin = new Padding(0, 4, 4, 4),
        };
        var countNum = new NumericUpDown
        {
            Minimum = 1,
            Maximum = 50,
            Value = 5,
            Width = 65,
            Margin = new Padding(0, 1, 16, 0),
        };
        if (_parameters.TryGetValue("count", out string? countStr) &&
            int.TryParse(countStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out int cVal))
        {
            countNum.Value = Math.Clamp(cVal, 1, 50);
        }
        _inputControls["count"] = countNum;

        var levelLabel = new Label
        {
            Text = isZh ? "初始等級 (Level)：" : "Spawn Level:",
            AutoSize = true,
            Font = new Font(Font, FontStyle.Bold),
            Margin = new Padding(0, 4, 4, 4),
        };
        var levelNum = new NumericUpDown
        {
            Minimum = 1,
            Maximum = 100,
            Value = 1,
            Width = 65,
            Margin = new Padding(0, 1, 16, 0),
        };
        if (_parameters.TryGetValue("level", out string? levelStr) &&
            int.TryParse(levelStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out int lVal))
        {
            levelNum.Value = Math.Clamp(lVal, 1, 100);
        }
        _inputControls["level"] = levelNum;

        _unitCountLabel = new Label
        {
            Text = Strings.Get("Gui_Trainer_UnitCount", 0, Cheats.MaxUnitListLength),
            AutoSize = true,
            Font = new Font(Font, FontStyle.Bold),
            ForeColor = Color.FromArgb(37, 99, 235),
            Margin = new Padding(4, 4, 16, 4),
        };

        _itemCountLabel = new Label
        {
            Text = Strings.Get("Gui_Trainer_ItemCount", 0, Cheats.MaxItemListLength),
            AutoSize = true,
            Font = new Font(Font, FontStyle.Bold),
            ForeColor = Color.FromArgb(16, 185, 129),
            Margin = new Padding(4, 4, 0, 4),
        };

        countRow.Controls.AddRange([countLabel, countNum, levelLabel, levelNum, _unitCountLabel, _itemCountLabel]);
        panel.Controls.Add(countRow, 0, 0);

        // 2. 搜尋與預設組合按鈕列
        var actionRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            WrapContents = true,
            Margin = new Padding(0, 0, 0, 8),
        };

        _searchBox = new TextBox
        {
            Width = 170,
            PlaceholderText = Strings.Get("Gui_Trainer_UnitSearch"),
            Margin = new Padding(0, 2, 8, 2),
        };
        _searchBox.TextChanged += (_, _) => FilterAllGrids();

        var btnDefault = CreatePresetButton(Strings.Get("Gui_Trainer_Preset_Default"), () => ApplyUnitPreset(Cheats.DefaultUnitList.Split(',')));
        var btnGaul = CreatePresetButton(Strings.Get("Gui_Trainer_Preset_Gaul"), () => ApplyUnitPreset(["GAxeman", "GSwordsman", "GArcher", "GSpearman", "GHorseman", "GWFighter", "GVikingLord", "GDruid"]));
        var btnRome = CreatePresetButton(Strings.Get("Gui_Trainer_Preset_Rome"), () => ApplyUnitPreset(["RHastatus", "RPrinciple", "RArcher", "RGladiator", "RScout", "RPraetorian", "RLiberatus", "RPriest"]));
        var btnHeroes = CreatePresetButton(Strings.Get("Gui_Trainer_Preset_Heroes"), () => ApplyUnitPreset(["Larax", "Keltill", "Caesar", "DLleldoryn", "GHeroWoman", "RHero6", "NHero01", "NHero02"]));
        var btnClearUnits = CreatePresetButton(Strings.Get("Gui_Trainer_Preset_Clear"), () => ApplyUnitPreset([]));

        var btnGodItems = CreatePresetButton(Strings.Get("Gui_Trainer_Preset_GodItems"), () => ApplyItemPreset(["Fur gloves of health", "Concentration stone", "King's Belt", "Belt of snakes"]));
        var btnAtkItems = CreatePresetButton(Strings.Get("Gui_Trainer_Preset_AtkItems"), () => ApplyItemPreset(["Concentration stone", "Belt of snakes", "Snake skin", "Bear teeth amulet"]));
        var btnDefItems = CreatePresetButton(Strings.Get("Gui_Trainer_Preset_DefItems"), () => ApplyItemPreset(["Fur gloves of health", "King's Belt", "Feather amulet", "Belt of might"]));
        var btnClearItems = CreatePresetButton(Strings.Get("Gui_Trainer_Preset_ClearItems"), () => ApplyItemPreset([]));

        actionRow.Controls.AddRange([
            _searchBox,
            btnDefault, btnGaul, btnRome, btnHeroes, btnClearUnits,
            btnGodItems, btnAtkItems, btnDefItems, btnClearItems
        ]);
        panel.Controls.Add(actionRow, 0, 1);

        // 3. 分類分頁清單
        var tabs = new TabControl { Dock = DockStyle.Fill };
        var unitsParam = _cheat.Parameters.FirstOrDefault(p => p.Name == "units");
        var unitOptions = unitsParam?.Options ?? Cheats.UnitOptions;

        // 初始化已選取的單位集合
        string initialUnits = _parameters.TryGetValue("units", out string? u) ? u : Cheats.DefaultUnitList;
        foreach (string unit in Cheats.ParseUnitList(initialUnits))
            _selectedUnits.Add(unit);

        // 初始化已選取的物品集合
        string initialItems = _parameters.TryGetValue("items", out string? it) ? it : string.Empty;
        foreach (string item in Cheats.ParseItemList(initialItems))
            _selectedItems.Add(item);

        // 單位分類對應
        var categories = new (string Key, string Name, Func<CheatParamOption, bool> Predicate)[]
        {
            ("all", Strings.Get("Gui_Trainer_Cat_All"), _ => true),
            ("gaul_units", Strings.Get("Gui_Trainer_Cat_GaulUnits"), opt => opt.Category == "GaulUnits"),
            ("gaul_heroes", Strings.Get("Gui_Trainer_Cat_GaulHeroes"), opt => opt.Category == "GaulHeroes"),
            ("rome_units", Strings.Get("Gui_Trainer_Cat_RomeUnits"), opt => opt.Category == "RomeUnits"),
            ("rome_heroes", Strings.Get("Gui_Trainer_Cat_RomeHeroes"), opt => opt.Category == "RomeHeroes"),
            ("special", Strings.Get("Gui_Trainer_Cat_SpecialVehicles"), opt => opt.Category == "SpecialVehicles"),
            ("animals", Strings.Get("Gui_Trainer_Cat_Animals"), opt => opt.Category == "Animals"),
        };

        foreach (var (key, name, pred) in categories)
        {
            var tab = new TabPage(name) { BackColor = Color.FromArgb(248, 250, 252), Padding = new Padding(6) };
            var grid = CreateGrid(3);
            var boxList = new List<CheckBox>();

            foreach (var opt in unitOptions.Where(pred))
            {
                var cb = new CheckBox
                {
                    Text = isZh ? $"{opt.Label} ({opt.Value})" : $"{opt.EnglishLabel} ({opt.Value})",
                    Tag = opt,
                    Dock = DockStyle.Fill,
                    AutoEllipsis = true,
                    Margin = new Padding(4, 2, 4, 2),
                    Checked = _selectedUnits.Contains(opt.Value),
                };
                _toolTip.SetToolTip(cb, isZh ? $"{opt.Label}\n代號: {opt.Value}\n英文: {opt.EnglishLabel}" : $"{opt.EnglishLabel} ({opt.Value})");
                cb.CheckedChanged += (_, _) => OnUnitCheckedChanged(cb, opt.Value);
                _unitCheckBoxes.Add(cb);
                boxList.Add(cb);
            }

            _unitCategoryGrids[key] = (grid, boxList);
            PopulateGrid(grid, boxList, 3);

            tab.Controls.Add(grid);
            tabs.Controls.Add(tab);
        }

        // 物品分頁 (Carried Items) - 2 欄寬度以完整顯示裝備名稱與能力加成
        var itemsTab = new TabPage(Strings.Get("Gui_Trainer_Cat_Items")) { BackColor = Color.FromArgb(248, 250, 252), Padding = new Padding(6) };
        _itemsGrid = CreateGrid(2);

        foreach (var opt in Cheats.ItemOptions)
        {
            var cb = new CheckBox
            {
                Text = isZh ? opt.Label : opt.EnglishLabel,
                Tag = opt,
                Dock = DockStyle.Fill,
                AutoEllipsis = true,
                Margin = new Padding(4, 2, 4, 2),
                Checked = _selectedItems.Contains(opt.Value),
            };
            _toolTip.SetToolTip(cb, isZh ? $"{opt.Label}\n代號: {opt.Value}\n英文: {opt.EnglishLabel}" : $"{opt.EnglishLabel} (ID: {opt.Value})");
            cb.CheckedChanged += (_, _) => OnItemCheckedChanged(cb, opt.Value);
            _itemCheckBoxes.Add(cb);
        }

        PopulateGrid(_itemsGrid, _itemCheckBoxes, 2);
        itemsTab.Controls.Add(_itemsGrid);
        tabs.Controls.Add(itemsTab);

        panel.Controls.Add(tabs, 0, 2);
        UpdateUnitCountDisplay();
        UpdateItemCountDisplay();

        return panel;
    }

    private Control BuildSpawnItemContent()
    {
        bool isZh = Strings.EffectiveLanguage == "zh-TW";
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
        };
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        // 1. 數量設定列
        var countRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            WrapContents = false,
            Margin = new Padding(0, 0, 0, 8),
        };

        var countLabel = new Label
        {
            Text = isZh ? "每次生成數量：" : "Spawn Count:",
            AutoSize = true,
            Font = new Font(Font, FontStyle.Bold),
            Margin = new Padding(0, 4, 4, 4),
        };
        var countNum = new NumericUpDown
        {
            Minimum = 1,
            Maximum = 20,
            Value = 1,
            Width = 65,
            Margin = new Padding(0, 1, 16, 0),
        };
        if (_parameters.TryGetValue("count", out string? countStr) &&
            int.TryParse(countStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out int cVal))
        {
            countNum.Value = Math.Clamp(cVal, 1, 20);
        }
        _inputControls["count"] = countNum;

        _itemCountLabel = new Label
        {
            Text = Strings.Get("Gui_Trainer_SwitchableItemCount", 0, Cheats.MaxSwitchableItemListLength),
            AutoSize = true,
            Font = new Font(Font, FontStyle.Bold),
            ForeColor = Color.FromArgb(37, 99, 235),
            Margin = new Padding(4, 4, 0, 4),
        };

        countRow.Controls.AddRange([countLabel, countNum, _itemCountLabel]);
        panel.Controls.Add(countRow, 0, 0);

        // 2. 搜尋與預設組合按鈕列
        var actionRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            WrapContents = true,
            Margin = new Padding(0, 0, 0, 8),
        };

        _searchBox = new TextBox
        {
            Width = 170,
            PlaceholderText = Strings.Get("Gui_Trainer_ItemSearch"),
            Margin = new Padding(0, 2, 8, 2),
        };
        _searchBox.TextChanged += (_, _) => FilterAllGrids();

        var btnDefault = CreatePresetButton(Strings.Get("Gui_Trainer_Preset_Default"), () => ApplyItemPreset(Cheats.DefaultItemList.Split(','), Cheats.MaxSwitchableItemListLength));
        var btnGodItems = CreatePresetButton(Strings.Get("Gui_Trainer_Preset_GodItems"), () => ApplyItemPreset(["King's Belt", "Fur gloves of health", "Concentration stone", "Finger of death"], Cheats.MaxSwitchableItemListLength));
        var btnAtkItems = CreatePresetButton(Strings.Get("Gui_Trainer_Preset_AtkItems"), () => ApplyItemPreset(["Concentration stone", "Belt of snakes", "Snake skin", "Boar tooth", "Horn of victory"], Cheats.MaxSwitchableItemListLength));
        var btnDefItems = CreatePresetButton(Strings.Get("Gui_Trainer_Preset_DefItems"), () => ApplyItemPreset(["King's Belt", "Fur gloves of health", "Feather amulet", "Eagle feather", "Belt of might", "Herb amulet of luck"], Cheats.MaxSwitchableItemListLength));
        var btnHealItems = CreatePresetButton(Strings.Get("Gui_Trainer_Preset_HealItems"), () => ApplyItemPreset(["Healing herbs", "Healing water", "Ash of druid heart", "Rye spikes"], Cheats.MaxSwitchableItemListLength));
        var btnSpecialItems = CreatePresetButton(Strings.Get("Gui_Trainer_Preset_SpecialItems"), () => ApplyItemPreset(["Boar teeth", "Poison Mushroom", "Gem of Power", "Glowing gem", "Faded gem", "Bloodstone"], Cheats.MaxSwitchableItemListLength));
        var btnAllItems = CreatePresetButton(Strings.Get("Gui_Trainer_Preset_AllItems"), () => ApplyItemPreset(Cheats.ItemOptions.Select(o => o.Value), Cheats.MaxSwitchableItemListLength));
        var btnClearItems = CreatePresetButton(Strings.Get("Gui_Trainer_Preset_ClearItems"), () => ApplyItemPreset([], Cheats.MaxSwitchableItemListLength));

        actionRow.Controls.AddRange([
            _searchBox,
            btnDefault, btnGodItems, btnAtkItems, btnDefItems, btnHealItems, btnSpecialItems, btnAllItems, btnClearItems
        ]);
        panel.Controls.Add(actionRow, 0, 1);

        // 3. 分類分頁清單
        var tabs = new TabControl { Dock = DockStyle.Fill };

        // 初始化已選取的物品集合
        string initialItems = _parameters.TryGetValue("items", out string? it) ? it : Cheats.DefaultItemList;
        foreach (string item in Cheats.ParseItemList(initialItems, Cheats.MaxSwitchableItemListLength))
            _selectedItems.Add(item);

        // 物品分類對應
        var categories = new (string Key, string Name, Func<CheatParamOption, bool> Predicate)[]
        {
            ("all", Strings.Get("Gui_Trainer_Cat_All"), _ => true),
            ("god_tier", Strings.Get("Gui_Trainer_Cat_GodTier"), opt => opt.Category == "GodTier"),
            ("attack", Strings.Get("Gui_Trainer_Cat_Attack"), opt => opt.Category == "Attack"),
            ("defense", Strings.Get("Gui_Trainer_Cat_Defense"), opt => opt.Category == "Defense"),
            ("heal", Strings.Get("Gui_Trainer_Cat_Heal"), opt => opt.Category == "Heal"),
            ("special", Strings.Get("Gui_Trainer_Cat_Special"), opt => opt.Category == "Special"),
        };

        foreach (var (key, name, pred) in categories)
        {
            var tab = new TabPage(name) { BackColor = Color.FromArgb(248, 250, 252), Padding = new Padding(6) };
            var grid = CreateGrid(2);
            var boxList = new List<CheckBox>();

            foreach (var opt in Cheats.ItemOptions.Where(pred))
            {
                var cb = new CheckBox
                {
                    Text = isZh ? opt.Label : opt.EnglishLabel,
                    Tag = opt,
                    Dock = DockStyle.Fill,
                    AutoEllipsis = true,
                    Margin = new Padding(4, 2, 4, 2),
                    Checked = _selectedItems.Contains(opt.Value),
                };
                _toolTip.SetToolTip(cb, isZh ? $"{opt.Label}\n代號: {opt.Value}\n英文: {opt.EnglishLabel}" : $"{opt.EnglishLabel} (ID: {opt.Value})");
                cb.CheckedChanged += (_, _) => OnItemCheckedChanged(cb, opt.Value);
                _itemCheckBoxes.Add(cb);
                boxList.Add(cb);
            }

            _itemCategoryGrids[key] = (grid, boxList);
            PopulateGrid(grid, boxList, 2);

            tab.Controls.Add(grid);
            tabs.Controls.Add(tab);
        }

        panel.Controls.Add(tabs, 0, 2);
        UpdateItemCountDisplay();

        return panel;
    }

    private static TableLayoutPanel CreateGrid(int columns = 3)
    {
        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            ColumnCount = columns,
            Padding = new Padding(6),
            BackColor = Color.FromArgb(248, 250, 252)
        };
        EnableDoubleBuffering(table);
        float percent = 100F / columns;
        for (int i = 0; i < columns; i++)
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, percent));
        return table;
    }

    private static void EnableDoubleBuffering(Control control)
    {
        typeof(Control).GetProperty("DoubleBuffered", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?.SetValue(control, true);
    }

    private static void PopulateGrid(TableLayoutPanel table, IEnumerable<CheckBox> checkBoxes, int columns = 3)
    {
        table.SuspendLayout();
        table.Controls.Clear();
        table.RowStyles.Clear();
        int index = 0;
        foreach (var cb in checkBoxes)
        {
            int col = index % columns;
            int row = index / columns;
            if (col == 0)
                table.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
            table.Controls.Add(cb, col, row);
            index++;
        }
        table.ResumeLayout();
    }

    private Button CreatePresetButton(string text, Action action)
    {
        var btn = new Button
        {
            Text = text,
            AutoSize = true,
            FlatStyle = FlatStyle.System,
            Margin = new Padding(0, 2, 6, 2),
        };
        btn.Click += (_, _) => action();
        return btn;
    }

    private void OnUnitCheckedChanged(CheckBox sender, string unitId)
    {
        if (sender.Checked)
        {
            if (_selectedUnits.Count >= Cheats.MaxUnitListLength && !_selectedUnits.Contains(unitId))
            {
                sender.Checked = false;
                MessageBox.Show(
                    Strings.Get("Gui_Trainer_UnitLimitReached", Cheats.MaxUnitListLength),
                    Text,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }
            _selectedUnits.Add(unitId);
        }
        else
        {
            _selectedUnits.Remove(unitId);
        }

        // 同步所有相同單位的 CheckBox (跨分頁)
        foreach (var cb in _unitCheckBoxes)
        {
            if (cb != sender && cb.Tag is CheatParamOption opt && string.Equals(opt.Value, unitId, StringComparison.OrdinalIgnoreCase))
            {
                if (cb.Checked != sender.Checked)
                    cb.Checked = sender.Checked;
            }
        }

        UpdateUnitCountDisplay();
    }

    private void OnItemCheckedChanged(CheckBox sender, string itemId)
    {
        int maxItems = _cheat.Id == Cheats.SpawnItemId
            ? Cheats.MaxSwitchableItemListLength
            : Cheats.MaxItemListLength;

        if (sender.Checked)
        {
            if (_selectedItems.Count >= maxItems && !_selectedItems.Contains(itemId))
            {
                sender.Checked = false;
                MessageBox.Show(
                    Strings.Get("Gui_Trainer_ItemLimitReached", maxItems),
                    Text,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }
            _selectedItems.Add(itemId);
        }
        else
        {
            _selectedItems.Remove(itemId);
        }

        // 同步所有相同物品的 CheckBox (跨分頁)
        foreach (var cb in _itemCheckBoxes)
        {
            if (cb != sender && cb.Tag is CheatParamOption opt && string.Equals(opt.Value, itemId, StringComparison.OrdinalIgnoreCase))
            {
                if (cb.Checked != sender.Checked)
                    cb.Checked = sender.Checked;
            }
        }

        UpdateItemCountDisplay();
    }

    private void ApplyUnitPreset(IEnumerable<string> units)
    {
        _selectedUnits.Clear();
        foreach (string u in units.Take(Cheats.MaxUnitListLength))
            _selectedUnits.Add(u);

        foreach (var cb in _unitCheckBoxes)
        {
            if (cb.Tag is CheatParamOption opt)
                cb.Checked = _selectedUnits.Contains(opt.Value);
        }

        UpdateUnitCountDisplay();
    }

    private void ApplyItemPreset(IEnumerable<string> items, int maxLimit = Cheats.MaxItemListLength)
    {
        _selectedItems.Clear();
        foreach (string it in items.Take(maxLimit))
            _selectedItems.Add(it);

        foreach (var cb in _itemCheckBoxes)
        {
            if (cb.Tag is CheatParamOption opt)
                cb.Checked = _selectedItems.Contains(opt.Value);
        }

        UpdateItemCountDisplay();
    }

    private void FilterAllGrids()
    {
        string query = _searchBox?.Text.Trim() ?? string.Empty;

        // 過濾單位表格
        foreach (var (_, (panel, allBoxes)) in _unitCategoryGrids)
        {
            var filtered = string.IsNullOrEmpty(query)
                ? allBoxes
                : allBoxes.Where(cb => cb.Tag is CheatParamOption opt &&
                    (opt.Value.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                     opt.Label.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                     opt.EnglishLabel.Contains(query, StringComparison.OrdinalIgnoreCase))).ToList();

            PopulateGrid(panel, filtered, 3);
        }

        // 過濾物品分類表格 (spawn_item)
        foreach (var (_, (panel, allBoxes)) in _itemCategoryGrids)
        {
            var filtered = string.IsNullOrEmpty(query)
                ? allBoxes
                : allBoxes.Where(cb => cb.Tag is CheatParamOption opt &&
                    (opt.Value.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                     opt.Label.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                     opt.EnglishLabel.Contains(query, StringComparison.OrdinalIgnoreCase))).ToList();

            PopulateGrid(panel, filtered, 2);
        }

        // 過濾單一物品表格 (spawn_unit 攜帶物品分頁)
        if (_itemsGrid is not null)
        {
            var filteredItems = string.IsNullOrEmpty(query)
                ? _itemCheckBoxes
                : _itemCheckBoxes.Where(cb => cb.Tag is CheatParamOption opt &&
                    (opt.Value.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                     opt.Label.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                     opt.EnglishLabel.Contains(query, StringComparison.OrdinalIgnoreCase))).ToList();

            PopulateGrid(_itemsGrid, filteredItems, 2);
        }
    }

    private void UpdateUnitCountDisplay()
    {
        if (_unitCountLabel is null) return;
        int count = _selectedUnits.Count;
        _unitCountLabel.Text = Strings.Get("Gui_Trainer_UnitCount", count, Cheats.MaxUnitListLength);
        _unitCountLabel.ForeColor = count >= Cheats.MaxUnitListLength ? Color.FromArgb(220, 38, 38) : Color.FromArgb(37, 99, 235);
    }

    private void UpdateItemCountDisplay()
    {
        if (_itemCountLabel is null) return;
        int count = _selectedItems.Count;
        if (_cheat.Id == Cheats.SpawnItemId)
        {
            _itemCountLabel.Text = Strings.Get("Gui_Trainer_SwitchableItemCount", count, Cheats.MaxSwitchableItemListLength);
            _itemCountLabel.ForeColor = count >= Cheats.MaxSwitchableItemListLength ? Color.FromArgb(220, 38, 38) : Color.FromArgb(37, 99, 235);
        }
        else
        {
            _itemCountLabel.Text = Strings.Get("Gui_Trainer_ItemCount", count, Cheats.MaxItemListLength);
            _itemCountLabel.ForeColor = count >= Cheats.MaxItemListLength ? Color.FromArgb(220, 38, 38) : Color.FromArgb(16, 185, 129);
        }
    }

    private Control BuildBottomButtons()
    {
        var bar = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            AutoSize = true,
            Margin = new Padding(0, 12, 0, 0),
        };
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var btnReset = new Button
        {
            Text = Strings.Get("Gui_Trainer_ResetParams"),
            AutoSize = true,
            FlatStyle = FlatStyle.System,
            Padding = new Padding(8, 4, 8, 4),
        };
        btnReset.Click += (_, _) => ResetToDefaults();

        var rightPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            WrapContents = false,
            FlowDirection = FlowDirection.RightToLeft,
        };

        var btnCancel = new Button
        {
            Text = Strings.Get("Gui_Cancel"),
            DialogResult = DialogResult.Cancel,
            AutoSize = true,
            FlatStyle = FlatStyle.System,
            Padding = new Padding(12, 4, 12, 4),
            Margin = new Padding(6, 0, 0, 0),
        };

        var btnOk = new Button
        {
            Text = Strings.Get("Gui_OK"),
            DialogResult = DialogResult.OK,
            AutoSize = true,
            FlatStyle = FlatStyle.System,
            Padding = new Padding(12, 4, 12, 4),
            Margin = new Padding(0),
        };
        btnOk.Click += (_, _) =>
        {
            if (!ValidateAndSave())
                DialogResult = DialogResult.None;
        };

        rightPanel.Controls.AddRange([btnCancel, btnOk]);
        bar.Controls.Add(btnReset, 0, 0);
        bar.Controls.Add(rightPanel, 2, 0);

        AcceptButton = btnOk;
        CancelButton = btnCancel;

        return bar;
    }

    private void ResetToDefaults()
    {
        if (_cheat.Id == Cheats.SpawnItemId)
        {
            foreach (var param in _cheat.Parameters.Where(p => !p.Hidden))
            {
                if (param.Name == "items")
                {
                    ApplyItemPreset(Cheats.ParseItemList(Convert.ToString(param.Default, CultureInfo.InvariantCulture), Cheats.MaxSwitchableItemListLength), Cheats.MaxSwitchableItemListLength);
                }
                else if (_inputControls.TryGetValue(param.Name, out Control? ctrl) && ctrl is NumericUpDown num)
                {
                    num.Value = Math.Clamp(Convert.ToDecimal(param.Default, CultureInfo.InvariantCulture), param.Minimum, param.Maximum);
                }
            }
        }
        else if (_cheat.Id == Cheats.SpawnUnitId)
        {
            foreach (var param in _cheat.Parameters.Where(p => !p.Hidden))
            {
                if (param.Name == "units")
                {
                    ApplyUnitPreset(Cheats.ParseUnitList(Convert.ToString(param.Default, CultureInfo.InvariantCulture)));
                }
                else if (param.Name == "items")
                {
                    ApplyItemPreset(Cheats.ParseItemList(Convert.ToString(param.Default, CultureInfo.InvariantCulture), Cheats.MaxItemListLength), Cheats.MaxItemListLength);
                }
                else if (_inputControls.TryGetValue(param.Name, out Control? ctrl) && ctrl is NumericUpDown num)
                {
                    num.Value = Math.Clamp(Convert.ToDecimal(param.Default, CultureInfo.InvariantCulture), param.Minimum, param.Maximum);
                }
            }
        }
        else
        {
            foreach (var param in _cheat.Parameters.Where(p => !p.Hidden))
            {
                if (_inputControls.TryGetValue(param.Name, out Control? ctrl) && ctrl is NumericUpDown num)
                {
                    num.Value = Math.Clamp(Convert.ToDecimal(param.Default, CultureInfo.InvariantCulture), param.Minimum, param.Maximum);
                }
            }
        }
    }

    private bool ValidateAndSave()
    {
        if (_cheat.Id == Cheats.SpawnUnitId)
        {
            if (_selectedUnits.Count == 0)
            {
                MessageBox.Show(
                    Strings.Get("Gui_Trainer_UnitMinRequired"),
                    Text,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return false;
            }
            _parameters["units"] = string.Join(',', _selectedUnits);
            if (_inputControls.TryGetValue("count", out Control? countCtrl) && countCtrl is NumericUpDown countNum)
                _parameters["count"] = ((int)countNum.Value).ToString(CultureInfo.InvariantCulture);
            if (_inputControls.TryGetValue("level", out Control? levelCtrl) && levelCtrl is NumericUpDown levelNum)
                _parameters["level"] = ((int)levelNum.Value).ToString(CultureInfo.InvariantCulture);
            _parameters["items"] = string.Join(',', _selectedItems);
        }
        else if (_cheat.Id == Cheats.SpawnItemId)
        {
            if (_selectedItems.Count == 0)
            {
                MessageBox.Show(
                    Strings.Get("Gui_Trainer_ItemMinRequired"),
                    Text,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return false;
            }
            _parameters["items"] = string.Join(',', _selectedItems);
            if (_inputControls.TryGetValue("count", out Control? countCtrl) && countCtrl is NumericUpDown countNum)
                _parameters["count"] = ((int)countNum.Value).ToString(CultureInfo.InvariantCulture);
        }
        else
        {
            foreach (var param in _cheat.Parameters.Where(p => !p.Hidden))
            {
                if (_inputControls.TryGetValue(param.Name, out Control? ctrl) && ctrl is NumericUpDown num)
                {
                    _parameters[param.Name] = ((long)num.Value).ToString(CultureInfo.InvariantCulture);
                }
            }
        }

        return true;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _toolTip.Dispose();
        }
        base.Dispose(disposing);
    }
}
