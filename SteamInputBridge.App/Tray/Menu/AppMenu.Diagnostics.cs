using System.Collections.Generic;
using System.Windows.Forms;
using SteamInputBridge.Forwarding.Controller.Routing;
using SteamInputBridge.Forwarding.Mouse;
using SteamInputBridge.Hosting.Server.Orchestration;

namespace SteamInputBridge.App.Tray.Menu;

internal sealed partial class AppMenu
{
    private ToolStripMenuItem CreateDiagnosticsMenu(ServerStatus? status, TrayActivitySnapshot? lastActivity)
    {
        ToolStripMenuItem menu = MenuItem("Diagnostics");
        Add(menu.DropDownItems, CreateInputsMenu(status));
        Add(menu.DropDownItems, CreateOutputsMenu(status));
        Add(menu.DropDownItems, CreateSteamInputMenu(status, lastActivity));
        Add(menu.DropDownItems, CreateShortcutsMenu(status));
        return menu;
    }

    private ToolStripMenuItem CreateInputsMenu(ServerStatus? status)
    {
        ToolStripMenuItem menu = MenuItem("Inputs");
        if (status is null)
        {
            Add(menu.DropDownItems, DisabledItem(AppText.WaitingForStatus));
            return menu;
        }

        Add(menu.DropDownItems, StatusItem(
            "Physical controllers",
            AppText.ControllerInput(status.Inputs.Controller),
            InputPhysicalControllersStatus));
        Add(menu.DropDownItems, StatusItem(
            "Client streams",
            AppText.Count(CountClientControllers(status.ControllerPipes)),
            InputClientStreamsStatus));
        Add(menu.DropDownItems, StatusItem(
            "Raw Input Mouse",
            AppText.MouseInput(status.Inputs.Mouse),
            InputRawMouseStatus));
        AddInputErrors(menu.DropDownItems, status);
        return menu;
    }

    private ToolStripMenuItem CreateOutputsMenu(ServerStatus? status)
    {
        ToolStripMenuItem menu = MenuItem("Outputs");
        if (status is null)
        {
            Add(menu.DropDownItems, DisabledItem(AppText.WaitingForStatus));
            return menu;
        }

        Add(menu.DropDownItems, StatusItem(
            "Controller Emulation",
            AppText.Enabled(HasConnectedControllerOutput(status.Forwarding)),
            OutputControllerEmulationStatus));
        Add(menu.DropDownItems, StatusItem(
            "Motion",
            AppText.Enabled(status.Forwarding.PhysicalMotionEnabled),
            OutputMotionStatus));
        Add(menu.DropDownItems, StatusItem(
            "Mouse Emulation",
            AppText.Enabled(HasConnectedMouseOutput(status.MouseForwarding)),
            OutputMouseEmulationStatus));
        Add(menu.DropDownItems, StatusItem(
            "Pointer",
            AppText.Enabled(status.MouseForwarding.PointerOutputEnabled),
            OutputPointerStatus));
        AddSeparator(menu.DropDownItems);
        Add(menu.DropDownItems, StatusItem(
            "Virtual mouse",
            AppText.FormatMouseOutput(status.MouseForwarding),
            OutputVirtualMouseStatus));
        Add(menu.DropDownItems, StatusItem(
            "Controller slots",
            ControllerSlots(status.Forwarding.Slots),
            OutputControllerSlotsStatus));

        return menu;
    }

    private ToolStripMenuItem CreateSteamInputMenu(ServerStatus? status, TrayActivitySnapshot? lastActivity)
    {
        ToolStripMenuItem menu = MenuItem("Steam input");
        if (status is null)
        {
            Add(menu.DropDownItems, DisabledItem(AppText.WaitingForStatus));
            return menu;
        }

        ServerSteamInputStatus steamInput = lastActivity?.SteamInput ?? status.SteamInput;
        Add(menu.DropDownItems, StatusItem("Forced", AppText.Enabled(steamInput.Forced), SteamForcedStatus));
        Add(menu.DropDownItems, StatusItem("App ID", AppText.AppId(steamInput.AppId), SteamAppIdStatus));
        if (lastActivity is not null)
        {
            Add(menu.DropDownItems, StatusItem("Profile", lastActivity.ActiveClient.ProfileId, SteamProfileStatus));
        }

        if (!string.IsNullOrWhiteSpace(steamInput.LastError))
        {
            AddSeparator(menu.DropDownItems);
            Add(menu.DropDownItems, DisabledItem(AppText.Error(steamInput.LastError)));
        }

        return menu;
    }

    private ToolStripMenuItem CreateShortcutsMenu(ServerStatus? status)
    {
        ToolStripMenuItem menu = MenuItem("Shortcuts");
        if (status is null)
        {
            Add(menu.DropDownItems, DisabledItem(AppText.WaitingForStatus));
            return menu;
        }

        Add(menu.DropDownItems, StatusItem(
            "Action color",
            status.Overlay.ActionColor ?? AppText.None,
            ShortcutColorStatus));
        Add(menu.DropDownItems, StatusItem(
            "Mic",
            AppText.MicrophoneMuted(status.Overlay.Microphone),
            ShortcutMicStatus));
        AddSeparator(menu.DropDownItems);

        if (status.Shortcuts.Shortcuts.Count == 0)
        {
            Add(menu.DropDownItems, DisabledItem(AppText.None));
            return menu;
        }

        foreach (ShortcutStatus shortcut in status.Shortcuts.Shortcuts)
        {
            Add(menu.DropDownItems, StatusItem(
                shortcut.Keys,
                AppText.Held(shortcut.Held),
                ShortcutStatusKey(shortcut.ShortcutId)));
        }

        return menu;
    }

    private static void AddInputErrors(ToolStripItemCollection items, ServerStatus status)
    {
        if (!string.IsNullOrWhiteSpace(status.Inputs.Mouse.LastError))
        {
            AddSeparator(items);
            Add(items, DisabledItem(AppText.Error("Raw input", status.Inputs.Mouse.LastError)));
        }

        if (!string.IsNullOrWhiteSpace(status.Inputs.Controller.LastError))
        {
            AddSeparator(items);
            Add(items, DisabledItem(AppText.Error("Physical controllers", status.Inputs.Controller.LastError)));
        }
    }

    private static bool HasConnectedControllerOutput(ControllerBrokerStatus status)
    {
        foreach (ControllerSlotStatus slot in status.Slots)
        {
            if (slot.OutputConnected)
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasConnectedMouseOutput(MouseBrokerStatus status)
    {
        return status.OutputConnected && status.Output != MouseOutput.None;
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
}
