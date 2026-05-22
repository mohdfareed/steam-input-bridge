using System;
using System.Threading.Tasks;
using System.Windows;
using SteamInputBridge.App.Cli;
using SteamInputBridge.App.Tray.Core;

namespace SteamInputBridge.App;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            return RunAsync(args).GetAwaiter().GetResult();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            ReportUnhandledException(args, exception);
            return 1;
        }
    }

    private static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0 || IsMode(args[0], "tray"))
        {
            return TrayMode.Run();
        }

        if (IsMode(args[0], "shortcut"))
        {
            return await CliMode.RunAsync(args).ConfigureAwait(false);
        }

        WindowsConsole.AttachForCli();
        return await CliMode.RunAsync(args).ConfigureAwait(false);
    }

    private static bool IsMode(string value, string mode)
    {
        return string.Equals(value, mode, StringComparison.OrdinalIgnoreCase);
    }

    private static void ReportUnhandledException(string[] args, Exception exception)
    {
        if (args.Length == 0 || IsMode(args[0], "tray") || IsMode(args[0], "shortcut"))
        {
            _ = MessageBox.Show(
                exception.Message,
                "Steam Input Bridge",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }

        WindowsConsole.AttachForCli();
        Console.Error.WriteLine($"Unhandled exception: {exception}");
    }
}
