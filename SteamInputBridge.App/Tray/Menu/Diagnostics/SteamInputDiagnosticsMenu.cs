using System.Collections.Generic;
using SteamInputBridge.Hosting.Server.Orchestration;

namespace SteamInputBridge.App.Tray.Menu;

internal sealed partial class AppMenu
{
    private static TrayMenuItem CreateSteamInputMenu(ServerStatus? status)
    {
        if (status is null)
        {
            return TrayMenuItem.Menu("Steam input", [TrayMenuItem.Disabled(AppText.WaitingForStatus)]);
        }

        List<TrayMenuItem> items =
        [
            CreateBoolStatus("Forced", status.SteamInput.Forced),
            TrayMenuItem.Status("App ID", AppText.AppId(status.SteamInput.AppId)),
        ];

        if (!string.IsNullOrWhiteSpace(status.SteamInput.LastError))
        {
            items.Add(TrayMenuItem.Separator);
            items.Add(TrayMenuItem.Disabled(AppText.Error(status.SteamInput.LastError)));
        }

        return TrayMenuItem.Menu("Steam input", items);
    }
}
