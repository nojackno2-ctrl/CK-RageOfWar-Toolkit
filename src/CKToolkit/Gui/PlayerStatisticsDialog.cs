using CKToolkit.Core.Saves;
using CKToolkit.Core.Trainer;
using CKToolkit.I18n;

namespace CKToolkit.Gui;

public sealed class PlayerStatisticsDialog : Form
{
    private const decimal MaxAggregate = (decimal)int.MaxValue * PlayerStatistics.MaxGameRecords;
    private const decimal MaxHours = MaxAggregate / PlayerStatistics.MillisecondsPerHour;
    private readonly NumericUpDown _militaryRating = NewNumber(PlayerStatistics.MaxMilitaryRating);
    private readonly NumericUpDown _singleGames = NewNumber(PlayerStatistics.MaxGameRecords);
    private readonly NumericUpDown _singleWins = NewNumber(PlayerStatistics.MaxGameRecords);
    private readonly NumericUpDown _multiGames = NewNumber(PlayerStatistics.MaxGameRecords);
    private readonly NumericUpDown _multiWins = NewNumber(PlayerStatistics.MaxGameRecords);
    private readonly NumericUpDown _hours = NewNumber(MaxHours);
    private readonly ComboBox _favoriteNation = new();
    private readonly NumericUpDown _favoritePercent = NewNumber(100);
    private readonly ComboBox _favoriteUnit = new();
    private readonly NumericUpDown _gold = NewNumber(MaxAggregate);
    private readonly NumericUpDown _food = NewNumber(MaxAggregate);
    private readonly NumericUpDown _unitsKilled = NewNumber(MaxAggregate);
    private readonly NumericUpDown _unitsLost = NewNumber(MaxAggregate);
    private readonly NumericUpDown _health = NewNumber(MaxAggregate);
    private readonly ComboBox _experiencedUnit = new();
    private readonly NumericUpDown _maxLevel = NewNumber(PlayerStatistics.MaxUnitLevel);
    private readonly NumericUpDown _maxUnits = NewNumber(int.MaxValue);
    private readonly Label _derivedHint = new();
    private readonly Label _warning = new();
    private readonly Button _save = new();
    private readonly Button _cancel = new();

    public PlayerStatisticsUpdate? ProposedUpdate { get; private set; }

    public PlayerStatisticsDialog(PlayerStatisticsSummary current)
    {
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(760, 650);
        Size = new Size(860, 760);
        BackColor = Color.White;
        Font = new Font("Microsoft JhengHei UI", 9F);

        _favoriteNation.DropDownStyle = ComboBoxStyle.DropDownList;
        _favoriteNation.Items.AddRange([
            new NationChoice(-1, Strings.Get("Gui_Save_Stats_NationUnknown")),
            new NationChoice(0, Strings.Get("Gui_Save_RaceGaul")),
            new NationChoice(1, Strings.Get("Gui_Save_RaceRoman")),
            new NationChoice(2, Strings.Get("Gui_Save_RaceRandom"))
        ]);
        ConfigureUnitCombo(_favoriteUnit);
        ConfigureUnitCombo(_experiencedUnit);
        BuildUi();
        LoadCurrent(current);
    }

    private void BuildUi()
    {
        Text = Strings.Get("Gui_Save_Stats_Title");
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(16),
            BackColor = Color.White
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _derivedHint.AutoSize = true;
        _derivedHint.MaximumSize = new Size(800, 0);
        _derivedHint.ForeColor = Color.FromArgb(71, 85, 105);
        _derivedHint.Text = Strings.Get("Gui_Save_Stats_DerivedHint");
        _derivedHint.Margin = new Padding(0, 0, 0, 12);
        root.Controls.Add(_derivedHint, 0, 0);

        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            ColumnCount = 2,
            RowCount = 2
        };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        content.Controls.Add(BuildResultsGroup(), 0, 0);
        content.Controls.Add(BuildTotalsGroup(), 1, 0);
        Control units = BuildUnitsGroup();
        content.Controls.Add(units, 0, 1);
        content.SetColumnSpan(units, 2);
        root.Controls.Add(content, 0, 1);

        _warning.AutoSize = true;
        _warning.MaximumSize = new Size(800, 0);
        _warning.ForeColor = Color.FromArgb(180, 83, 9);
        _warning.Text = Strings.Get("Gui_Save_Stats_RewriteWarning");
        _warning.Margin = new Padding(0, 10, 0, 10);
        root.Controls.Add(_warning, 0, 2);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false
        };
        ConfigureButton(_save, Color.FromArgb(37, 99, 235), Color.White);
        ConfigureButton(_cancel, Color.White, Color.FromArgb(71, 85, 105));
        _save.Text = Strings.Get("Gui_Save_Stats_Save");
        _cancel.Text = Strings.Get("Gui_Save_Stats_Cancel");
        _save.Click += (_, _) => Accept();
        _cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        actions.Controls.AddRange([_save, _cancel]);
        root.Controls.Add(actions, 0, 3);

        Controls.Add(root);
        AcceptButton = _save;
        CancelButton = _cancel;
    }

    private Control BuildResultsGroup()
    {
        var group = NewGroup(Strings.Get("Gui_Save_Stats_ResultsGroup"));
        var grid = NewFieldGrid();
        AddField(grid, Strings.Get("Gui_Save_Stats_MilitaryRating"), _militaryRating);
        AddField(grid, Strings.Get("Gui_Save_Stats_SingleGames"), _singleGames);
        AddField(grid, Strings.Get("Gui_Save_Stats_SingleWins"), _singleWins);
        AddField(grid, Strings.Get("Gui_Save_Stats_MultiGames"), _multiGames);
        AddField(grid, Strings.Get("Gui_Save_Stats_MultiWins"), _multiWins);
        AddField(grid, Strings.Get("Gui_Save_Stats_Hours"), _hours);
        AddField(grid, Strings.Get("Gui_Save_Stats_FavoriteNation"), _favoriteNation);
        AddField(grid, Strings.Get("Gui_Save_Stats_FavoritePercent"), _favoritePercent);
        group.Controls.Add(grid);
        return group;
    }

    private Control BuildTotalsGroup()
    {
        var group = NewGroup(Strings.Get("Gui_Save_Stats_TotalsGroup"));
        var grid = NewFieldGrid();
        AddField(grid, Strings.Get("Gui_Save_Stats_Gold"), _gold);
        AddField(grid, Strings.Get("Gui_Save_Stats_Food"), _food);
        AddField(grid, Strings.Get("Gui_Save_Stats_UnitsKilled"), _unitsKilled);
        AddField(grid, Strings.Get("Gui_Save_Stats_UnitsLost"), _unitsLost);
        AddField(grid, Strings.Get("Gui_Save_Stats_Health"), _health);
        AddField(grid, Strings.Get("Gui_Save_Stats_MaxUnits"), _maxUnits);
        group.Controls.Add(grid);
        return group;
    }

    private Control BuildUnitsGroup()
    {
        var group = NewGroup(Strings.Get("Gui_Save_Stats_UnitsGroup"));
        var grid = NewFieldGrid();
        AddField(grid, Strings.Get("Gui_Save_Stats_FavoriteUnit"), _favoriteUnit);
        AddField(grid, Strings.Get("Gui_Save_Stats_ExperiencedUnit"), _experiencedUnit);
        AddField(grid, Strings.Get("Gui_Save_Stats_MaxLevel"), _maxLevel);
        group.Controls.Add(grid);
        return group;
    }

    private void LoadCurrent(PlayerStatisticsSummary current)
    {
        SetNumeric(_militaryRating, current.MilitaryRating);
        SetNumeric(_singleGames, current.SinglePlayerGames);
        SetNumeric(_singleWins, current.SinglePlayerWins);
        SetNumeric(_multiGames, current.MultiplayerGames);
        SetNumeric(_multiWins, current.MultiplayerWins);
        SetNumeric(_hours, current.GameTimeHours);
        SetNumeric(_favoritePercent, current.FavoriteNationPercent);
        SelectNation(current.FavoriteNation);
        _favoriteUnit.Text = current.FavoriteUnit;
        SetNumeric(_gold, current.GoldSpent);
        SetNumeric(_food, current.FoodSpent);
        SetNumeric(_unitsKilled, current.UnitsKilled);
        SetNumeric(_unitsLost, current.UnitsLost);
        SetNumeric(_health, current.HealthSacrificed);
        _experiencedUnit.Text = current.MostExperiencedUnit;
        SetNumeric(_maxLevel, current.MaxUnitLevel);
        SetNumeric(_maxUnits, current.MaxUnits);
    }

    private void Accept()
    {
        int singleGames = decimal.ToInt32(_singleGames.Value);
        int singleWins = decimal.ToInt32(_singleWins.Value);
        int multiGames = decimal.ToInt32(_multiGames.Value);
        int multiWins = decimal.ToInt32(_multiWins.Value);
        if (singleWins > singleGames || multiWins > multiGames)
        {
            MessageBox.Show(this, Strings.Get("Save_Error_StatisticsWins"), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (_favoriteNation.SelectedItem is not NationChoice nation) return;

        long totalDuration;
        try { totalDuration = checked(decimal.ToInt64(_hours.Value) * PlayerStatistics.MillisecondsPerHour); }
        catch (OverflowException)
        {
            MessageBox.Show(this, Strings.Get("Save_Error_StatisticsInvalid"), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        ProposedUpdate = new PlayerStatisticsUpdate(
            singleGames,
            singleWins,
            multiGames,
            multiWins,
            totalDuration,
            decimal.ToInt32(_militaryRating.Value),
            nation.Id,
            decimal.ToInt32(_favoritePercent.Value),
            _favoriteUnit.Text.Trim(),
            decimal.ToInt64(_gold.Value),
            decimal.ToInt64(_food.Value),
            decimal.ToInt64(_unitsKilled.Value),
            decimal.ToInt64(_unitsLost.Value),
            decimal.ToInt64(_health.Value),
            _experiencedUnit.Text.Trim(),
            decimal.ToInt32(_maxLevel.Value),
            decimal.ToInt32(_maxUnits.Value));
        DialogResult = DialogResult.OK;
        Close();
    }

    private void SelectNation(int id)
    {
        int index = Enumerable.Range(0, _favoriteNation.Items.Count)
            .FirstOrDefault(i => (_favoriteNation.Items[i] as NationChoice)?.Id == id, -1);
        _favoriteNation.SelectedIndex = index >= 0 ? index : 0;
    }

    private static NumericUpDown NewNumber(decimal maximum) => new()
    {
        Minimum = 0,
        Maximum = maximum,
        ThousandsSeparator = true,
        Dock = DockStyle.Fill
    };

    private static void SetNumeric(NumericUpDown control, long value)
    {
        decimal decimalValue = value;
        control.Value = Math.Min(control.Maximum, Math.Max(control.Minimum, decimalValue));
    }

    private static GroupBox NewGroup(string title) => new()
    {
        Text = title,
        Dock = DockStyle.Top,
        AutoSize = true,
        Padding = new Padding(12),
        Margin = new Padding(4)
    };

    private static TableLayoutPanel NewFieldGrid()
    {
        var grid = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2 };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58F));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42F));
        return grid;
    }

    private static void AddField(TableLayoutPanel grid, string labelText, Control control)
    {
        int row = grid.RowCount++;
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var label = new Label
        {
            Text = labelText,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 7, 10, 5)
        };
        control.Dock = DockStyle.Fill;
        control.Margin = new Padding(0, 3, 0, 3);
        grid.Controls.Add(label, 0, row);
        grid.Controls.Add(control, 1, row);
    }

    private static void ConfigureUnitCombo(ComboBox combo)
    {
        combo.DropDownStyle = ComboBoxStyle.DropDown;
        combo.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
        combo.AutoCompleteSource = AutoCompleteSource.ListItems;
        combo.Items.Add(string.Empty);
        combo.Items.Add("Mule");
        foreach (string id in Cheats.UnitOptions
            .Select(option => option.Value)
            .Where(value => !value.Equals(Cheats.FoodMuleSentinel, StringComparison.Ordinal))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
        {
            combo.Items.Add(id);
        }
    }

    private static void ConfigureButton(Button button, Color back, Color fore)
    {
        button.AutoSize = true;
        button.MinimumSize = new Size(120, 36);
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderColor = fore == Color.White ? back : Color.FromArgb(203, 213, 225);
        button.BackColor = back;
        button.ForeColor = fore;
        button.Font = new Font(button.Font, FontStyle.Bold);
        button.Margin = new Padding(8, 0, 0, 0);
    }

    private sealed record NationChoice(int Id, string Label)
    {
        public override string ToString() => Label;
    }
}
