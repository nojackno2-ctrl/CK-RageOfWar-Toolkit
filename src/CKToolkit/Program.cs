using System.Text;
using CKToolkit.Cli;
using CKToolkit.Gui;

namespace CKToolkit;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        var utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        Console.OutputEncoding = utf8NoBom;
        Console.InputEncoding = utf8NoBom;

        if (args.Length == 0)
        {
            ApplicationConfiguration.Initialize();
            // 介面字型依這台電腦實際安裝的字型挑選（英文版 Windows 不一定有正黑體），
            // 不寫死在專案檔裡（ISSUE-101）。
            Application.SetDefaultFont(Gui.Layout.Ui.UiFont());
            Application.Run(new MainForm());
            return 0;
        }

        try
        {
            return CliHost.Run(args);
        }
        catch (Exception ex)
        {
            return CliHost.ReportUnhandled(args, ex);
        }
    }
}
