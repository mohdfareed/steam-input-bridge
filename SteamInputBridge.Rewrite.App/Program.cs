using System;
using System.Threading.Tasks;
using System.Windows.Forms;
using SteamInputBridge.App.Cli;

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
        if (args.Length == 0)
        {
            args = ["tray"];
        }

        return await CliMode.RunAsync(args).ConfigureAwait(false);
    }

    private static void ReportUnhandledException(string[] args, Exception exception)
    {
        if (args.Length != 0)
        {
            Console.Error.WriteLine($"Unhandled exception: {exception}");
            return;
        }

        Application.EnableVisualStyles();
        Application.SetColorMode(SystemColorMode.System);

        try
        {
            TaskDialogPage page = new()
            {
                Heading = "Steam Input Bridge",
                Caption = "An unexpected error occurred.",
                Text = exception.Message,
                SizeToContent = true,
                Expander = new TaskDialogExpander(exception.ToString())
                {
                    CollapsedButtonText = "Show details",
                    ExpandedButtonText = "Hide details",
                    Position = TaskDialogExpanderPosition.AfterText,
                },
            };

            page.Buttons.Add(TaskDialogButton.OK);
            _ = TaskDialog.ShowDialog(page);
        }
        catch (Exception dialogException) when (dialogException is InvalidOperationException or PlatformNotSupportedException)
        {
            _ = MessageBox.Show(
                exception.Message + Environment.NewLine + Environment.NewLine + exception,
                "Steam Input Bridge",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }
}
