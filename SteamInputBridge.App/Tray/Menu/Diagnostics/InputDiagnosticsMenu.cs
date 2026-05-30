using System.Collections.Generic;
using SteamInputBridge.Hosting.Server.Orchestration;

namespace SteamInputBridge.App.Tray.Menu;

internal sealed partial class AppMenu
{
    private static TrayMenuItem CreateInputsMenu(ServerStatus? status)
    {
        if (status is null)
        {
            return TrayMenuItem.Menu("Inputs", [TrayMenuItem.Disabled(AppText.WaitingForStatus)]);
        }

        int clientStreamCount = CountClientControllers(status.ControllerPipes);
        List<TrayMenuItem> items =
        [
            TrayMenuItem.Status(
                "Physical controllers",
                AppText.ControllerInput(status.Inputs.Controller),
                status.Inputs.Controller.SourceCount != 0),
            TrayMenuItem.Status(
                "Client streams",
                AppText.Count(clientStreamCount),
                clientStreamCount != 0),
            TrayMenuItem.Status("Raw Input Mouse", AppText.MouseInput(status.Inputs.Mouse), status.Inputs.Mouse.Running),
        ];

        if (!string.IsNullOrWhiteSpace(status.Inputs.Mouse.LastError))
        {
            items.Add(TrayMenuItem.Separator);
            items.Add(TrayMenuItem.Disabled(AppText.Error("Raw input", status.Inputs.Mouse.LastError)));
        }

        if (!string.IsNullOrWhiteSpace(status.Inputs.Controller.LastError))
        {
            items.Add(TrayMenuItem.Separator);
            items.Add(TrayMenuItem.Disabled(AppText.Error(
                "Physical controllers",
                status.Inputs.Controller.LastError)));
        }

        return TrayMenuItem.Menu("Inputs", items);
    }

    private static int CountClientControllers(IReadOnlyList<ControllerPipeStatus> pipes)
    {
        int count = 0;
        foreach (ControllerPipeStatus pipe in pipes)
        {
            count += pipe.Controllers.Count;
        }

        return count;
    }
}
