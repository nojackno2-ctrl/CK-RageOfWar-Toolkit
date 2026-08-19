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
            Application.Run(new MainForm());
            return 0;
        }

        return CliHost.Run(args);
    }
}
