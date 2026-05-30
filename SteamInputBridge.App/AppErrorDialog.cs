using System;
using System.Windows.Forms;

namespace SteamInputBridge.App;

internal static class AppErrorDialog
{
    private const string Caption = "Steam Input Bridge";
    private const string Heading = "Steam Input Bridge stopped";
    private const string DetailsCollapsedText = "Show details";
    private const string DetailsExpandedText = "Hide details";

    public static void ShowException(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        string message = exception.Message;
        string text = message;
        Show(text, exception.ToString());
    }

    private static void Show(string text, string details)
    {
        Application.EnableVisualStyles();
        Application.SetColorMode(SystemColorMode.System);

        try
        {
            TaskDialogPage page = new()
            {
                Caption = Caption,
                Heading = Heading,
                Text = text,
                SizeToContent = true,
                Expander = new TaskDialogExpander(details)
                {
                    CollapsedButtonText = DetailsCollapsedText,
                    ExpandedButtonText = DetailsExpandedText,
                    Position = TaskDialogExpanderPosition.AfterText,
                },
            };

            page.Buttons.Add(TaskDialogButton.OK);
            _ = TaskDialog.ShowDialog(page);
        }
        catch (Exception exception) when (exception is InvalidOperationException or PlatformNotSupportedException)
        {
            _ = MessageBox.Show(
                text + Environment.NewLine + Environment.NewLine + details,
                Caption,
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }
}
