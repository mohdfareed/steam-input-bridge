using System;
using System.Threading.Tasks;
using System.Windows.Forms;
using SteamInputBridge.Hosting;

namespace SteamInputBridge.App.Tray.Menu;

internal sealed class TeensyMenuSection
{
    private ToolStripMenuItem? _teensy;
    private ToolStripMenuItem? _status;

    // MARK: Publics
    // ========================================================================

    public ToolStripMenuItem Build(
        BridgeTeensyStatus status,
        Func<Task> uploadFirmware,
        Action<Exception> onError)
    {
        _teensy = TrayMenuItems.Menu("Teensy");
        _status = TrayMenuItems.Item("Status", FormatStatus(status));
        TrayMenuItems.SetCheckMark(_teensy, status.Connected);
        TrayMenuItems.SetCheckMark(_status, status.Connected);

        _ = _teensy.DropDownItems.Add(_status);
        _ = _teensy.DropDownItems.Add(TrayMenuItems.ActionItem("Upload firmware...", uploadFirmware, onError));
        return _teensy;
    }

    public void Update(BridgeTeensyStatus status)
    {
        if (_teensy is not null)
        {
            TrayMenuItems.SetCheckMark(_teensy, status.Connected);
        }

        if (_status is not null)
        {
            TrayMenuItems.SetValue(_status, FormatStatus(status));
            TrayMenuItems.SetCheckMark(_status, status.Connected);
        }
    }

    private static string FormatStatus(BridgeTeensyStatus status)
    {
        return status.Connected && !string.IsNullOrWhiteSpace(status.ConnectedPort)
            ? $"Connected: {status.ConnectedPort}"
            : status.State;
    }
}
