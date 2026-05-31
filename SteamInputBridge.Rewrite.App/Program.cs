using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using SteamInputBridge.App.Host;
using SteamInputBridge.App.Shortcut;
using SteamInputBridge.App.Tray;

namespace SteamInputBridge.App;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetColorMode(AppErrorDialog.ColorMode);

        try
        {
            return RunAsync(args).ConfigureAwait(false).GetAwaiter().GetResult();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            AppErrorDialog.Show(exception);
            return 1;
        }
    }

    private static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0)
        {
            args = ["tray"];
        }

        return args[0].Equals("tray", StringComparison.OrdinalIgnoreCase)
            ? TrayMode.Run()
            : args[0].Equals("shortcut", StringComparison.OrdinalIgnoreCase)
            ? args.Length != 2
                ? throw new ArgumentException("shortcut requires a profile id.")
                : await ShortcutMode.RunAsync(args[1], CancellationToken.None).ConfigureAwait(false)
            : throw new ArgumentException($"Unknown app command '{args[0]}'.");
    }
}
