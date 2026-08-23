using System.Reflection;
using CKToolkit.I18n;

namespace CKToolkit.Gui;

public sealed class AboutPage : UserControl
{
    private readonly Label _name = new();
    private readonly Label _version = new();
    private readonly Label _description = new();
    private readonly Label _features = new();
    private readonly Label _safety = new();
    private readonly Label _license = new();

    public AboutPage()
    {
        AutoScroll = true;
        BackColor = Color.White;
        Padding = new Padding(32);
        var panel = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1 };
        _name.AutoSize = true;
        _name.Font = new Font(Font.FontFamily, 21F, FontStyle.Bold);
        _name.ForeColor = Color.FromArgb(15, 23, 42);
        _version.AutoSize = true;
        _version.ForeColor = Color.FromArgb(71, 85, 105);
        _version.Margin = new Padding(0, 4, 0, 18);
        foreach (Label label in new[] { _description, _features, _safety, _license })
        {
            label.AutoSize = true;
            label.MaximumSize = new Size(820, 0);
            label.Margin = new Padding(0, 0, 0, 16);
        }
        _safety.BackColor = Color.FromArgb(239, 246, 255);
        _safety.Padding = new Padding(14);
        panel.Controls.Add(_name);
        panel.Controls.Add(_version);
        panel.Controls.Add(_description);
        panel.Controls.Add(_features);
        panel.Controls.Add(_safety);
        panel.Controls.Add(_license);
        Controls.Add(panel);
    }

    public void ApplyLanguage()
    {
        _name.Text = Strings.Get("AppTitle");
        string version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";
        _version.Text = Strings.Get("Gui_About_Version", version);
        _description.Text = Strings.Get("Gui_About_Description");
        _features.Text = Strings.Get("Gui_About_Features");
        _safety.Text = Strings.Get("Gui_About_Safety");
        _license.Text = Strings.Get("Gui_About_License");
    }
}
