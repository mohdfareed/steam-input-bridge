using System.Windows.Forms;
using SteamInputBridge.Microphone;

namespace SteamInputBridge.App.Tray.Menu;

internal sealed class DiagnosticsMenuSection
{
    private ToolStripMenuItem? _microphone;
    private ToolStripMenuItem? _actionColor;

    // MARK: Publics
    // ========================================================================

    public ToolStripMenuItem Build(MicrophoneStatus microphone, string? actionColor)
    {
        ToolStripMenuItem menu = TrayMenuItems.Menu("Diagnostics");
        _microphone = TrayMenuItems.Item("Microphone", FormatMicrophone(microphone));
        _actionColor = TrayMenuItems.Item("Action color", FormatActionColor(actionColor));

        _ = menu.DropDownItems.Add(_microphone);
        _ = menu.DropDownItems.Add(_actionColor);
        return menu;
    }

    public void Update(MicrophoneStatus microphone, string? actionColor)
    {
        if (_microphone is not null)
        {
            TrayMenuItems.SetValue(_microphone, FormatMicrophone(microphone));
        }

        if (_actionColor is not null)
        {
            TrayMenuItems.SetValue(_actionColor, FormatActionColor(actionColor));
        }
    }

    // MARK: Format
    // ========================================================================

    private static string FormatMicrophone(MicrophoneStatus status)
    {
        return status switch
        {
            { Available: false } => "None",
            { Muted: true } => "Muted",
            { IsActive: true } => "Active",
            _ => "Available",
        };
    }

    private static string FormatActionColor(string? color)
    {
        return string.IsNullOrWhiteSpace(color) ? "None" : color;
    }
}
