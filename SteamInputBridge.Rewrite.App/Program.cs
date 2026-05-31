using System;
using System.Windows.Forms;
using SteamInputBridge.App.Cli;

namespace SteamInputBridge.App;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            args = ["tray"];
        }

        try
        {
            return CliMode.RunAsync(args).ConfigureAwait(false).GetAwaiter().GetResult();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            ReportUnhandledException(exception);
            return 1;
        }
    }

    private static void ReportUnhandledException(Exception exception)
    {
        Application.EnableVisualStyles();
        Application.SetColorMode(SystemColorMode.System);

        try
        {
            // TODO: Review style
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
