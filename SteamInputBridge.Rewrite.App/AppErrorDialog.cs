using System;
using System.Windows.Forms;

namespace SteamInputBridge.App;

internal static partial class AppErrorDialog
{
    private const string Caption = "Steam Input Bridge";
    private const string Heading = "An unexpected error occurred";
    private const string DetailsCollapsedText = "Show details";
    private const string DetailsExpandedText = "Hide details";
    private const string CopyDetailsText = "Copy details";
    private const string DialogErrorText = "Dialog error:";

    // MARK: Publics
    // ========================================================================

    public const SystemColorMode ColorMode = SystemColorMode.System;

    public static void Show(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        // FIXME: Error dialog is always in light mode.
        Application.EnableVisualStyles();
        Application.SetColorMode(ColorMode);

        try
        {
            string details = exception.ToString();
            TaskDialogButton copyDetails = new(CopyDetailsText)
            {
                AllowCloseDialog = false,
            };
            copyDetails.Click += (_, _) => Clipboard.SetText(details);

            TaskDialogPage page = new()
            {
                Caption = Caption,
                Heading = Heading,
                Icon = TaskDialogIcon.Error,
                Text = exception.Message,
                Expander = new TaskDialogExpander(details)
                {
                    CollapsedButtonText = DetailsCollapsedText,
                    ExpandedButtonText = DetailsExpandedText,
                    Position = TaskDialogExpanderPosition.AfterText,
                },
            };

            page.Buttons.Add(copyDetails);
            page.Buttons.Add(TaskDialogButton.OK);
            _ = TaskDialog.ShowDialog(page);
        }
        catch (Exception dialogException) when (dialogException is not OutOfMemoryException and not StackOverflowException)
        {
            string fallbackText = $"""
                {exception.Message}

                {exception}

                {DialogErrorText}
                {dialogException}
                """;
            _ = MessageBox.Show(fallbackText, Caption, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
