using System.Collections.Generic;
using SteamInputBridge.Forwarding.Controller.Routing;
using SteamInputBridge.Hosting.Server.Orchestration;

namespace SteamInputBridge.App.Tray.Menu;

internal sealed partial class AppMenu
{
    private static TrayMenuItem CreateOutputsMenu(ServerStatus? status)
    {
        if (status is null)
        {
            return TrayMenuItem.Menu("Outputs", [TrayMenuItem.Disabled(AppText.WaitingForStatus)]);
        }

        List<TrayMenuItem> items =
        [
            CreateBoolStatus("Controller Emulation", status.Forwarding.ControllerOutputEnabled),
            CreateBoolStatus("Motion", status.Forwarding.PhysicalMotionEnabled),
            CreateBoolStatus("Mouse Emulation", status.MouseForwarding.MouseOutputEnabled),
            CreateBoolStatus("Pointer", status.MouseForwarding.PointerOutputEnabled),
            TrayMenuItem.Separator,
            TrayMenuItem.Status(
                "Virtual mouse",
                AppText.FormatMouseOutput(status.MouseForwarding),
                status.MouseForwarding.OutputConnected),
            TrayMenuItem.Status(
                "Controller slots",
                ControllerSlots(status.Forwarding.Slots),
                HasConnectedControllerOutput(status.Forwarding.Slots)),
        ];

        return TrayMenuItem.Menu("Outputs", items);
    }

    private static string ControllerSlots(IReadOnlyList<ControllerSlotStatus> slots)
    {
        if (slots.Count == 0)
        {
            return AppText.None;
        }

        int connected = 0;
        foreach (ControllerSlotStatus slot in slots)
        {
            if (slot.OutputConnected)
            {
                connected++;
            }
        }

        return $"{connected}/{slots.Count} connected";
    }

    private static bool HasConnectedControllerOutput(IReadOnlyList<ControllerSlotStatus> slots)
    {
        foreach (ControllerSlotStatus slot in slots)
        {
            if (slot.OutputConnected)
            {
                return true;
            }
        }

        return false;
    }
}
