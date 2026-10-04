using System.Reflection;
using CKToolkit.Gui.Layout;
using CKToolkit.I18n;

namespace CKToolkit.Gui;

public sealed class AboutPage : ScrollPage
{
    private readonly Label _name = new();
    private readonly Label _version = new();
    private readonly Label _description = new();
    private readonly Label _features = new();
    private readonly Label _safety = new();
    private readonly Label _license = new();

    public AboutPage()
    {
        Padding = new Padding(28, 24, 28, 24);
        _name.AutoSize = true;
        _name.UseMnemonic = false;
        _name.Font = Ui.UiFont(19F, FontStyle.Bold);
        _name.ForeColor = Ui.TextPrimary;
        Content.Add(_name);
        Ui.Text(_version).Margin = new Padding(0, 4, 0, 18);
        Content.Add(_version);
        foreach (Label label in new[] { _description, _features, _safety, _license })
        {
            Ui.Text(label, Ui.TextPrimary).Margin = new Padding(0, 0, 0, 16);
            Content.Add(label);
        }
        Ui.Banner(_safety, Color.FromArgb(239, 246, 255), Color.FromArgb(30, 64, 175)).Margin = new Padding(0, 0, 0, 16);
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
