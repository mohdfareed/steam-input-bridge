using System.Collections.Generic;
using System.Globalization;
using System.IO;
using SteamInputBridge.Forwarding.Controller.Routing;
using SteamInputBridge.Hosting.Server.Orchestration;

namespace SteamInputBridge.App.Tray.Menu;

internal sealed partial class AppMenu
{
    private static NativeMenuItem CreateDiagnosticsMenu(ServerStatus? status)
    {
        return NativeMenuItem.Menu(
            "Diagnostics",
            [
                CreateInputsMenu(status),
                CreateOutputsMenu(status),
                CreateHidHideMenu(status),
                CreateSteamInputMenu(status),
            ]);
    }

    private static NativeMenuItem CreateHidHideMenu(ServerStatus? status)
    {
        if (status is null)
        {
            return NativeMenuItem.Menu("HidHide", [NativeMenuItem.Disabled(AppText.WaitingForStatus)]);
        }

        ServerHidHideStatus hidHide = status.HidHide;
        List<NativeMenuItem> items =
        [
            CreateBoolStatus("Cloak mode", hidHide.CloakEnabled),
            CreateBoolStatus("Inverse mode", hidHide.InverseEnabled),
            CreateBoolStatus("Active scope", hidHide.Active),
            NativeMenuItem.Separator,
            CreateValueMenu("Hidden devices", GetHidHideDeviceDisplayValues(hidHide)),
            CreateValueMenu("Allowed apps", GetApplicationNames(hidHide.RegisteredApplications)),
        ];

        if (!string.IsNullOrWhiteSpace(hidHide.LastError))
        {
            items.Add(NativeMenuItem.Separator);
            items.Add(NativeMenuItem.Disabled(AppText.Error(hidHide.LastError)));
        }

        return NativeMenuItem.Menu("HidHide", items);
    }

    private static NativeMenuItem CreateInputsMenu(ServerStatus? status)
    {
        if (status is null)
        {
            return NativeMenuItem.Menu("Inputs", [NativeMenuItem.Disabled(AppText.WaitingForStatus)]);
        }

        int controllerCount = CountClientControllers(status.ControllerPipes);
        List<NativeMenuItem> items =
        [
            NativeMenuItem.Status(
                "Controller streams",
                AppText.Count(controllerCount),
                controllerCount != 0),
            NativeMenuItem.Status("Raw Input Mouse", AppText.MouseInput(status.Inputs.Mouse), status.Inputs.Mouse.Running),
        ];

        if (!string.IsNullOrWhiteSpace(status.Inputs.Mouse.LastError))
        {
            items.Add(NativeMenuItem.Separator);
            items.Add(NativeMenuItem.Disabled(AppText.Error("Raw input", status.Inputs.Mouse.LastError)));
        }

        items.Add(NativeMenuItem.Separator);
        foreach (string controller in GetControllerStreamLabels(status.ControllerPipes))
        {
            items.Add(NativeMenuItem.Disabled(controller));
        }

        return NativeMenuItem.Menu("Inputs", items);
    }

    private static NativeMenuItem CreateOutputsMenu(ServerStatus? status)
    {
        if (status is null)
        {
            return NativeMenuItem.Menu("Outputs", [NativeMenuItem.Disabled(AppText.WaitingForStatus)]);
        }

        List<NativeMenuItem> items =
        [
            CreateBoolStatus("Mouse output", status.MouseForwarding.MouseOutputEnabled),
            CreateBoolStatus("Pointer", status.MouseForwarding.PointerOutputEnabled),
            NativeMenuItem.Separator,
            CreateBoolStatus("Controller output", status.Forwarding.ControllerOutputEnabled),
            CreateBoolStatus("Motion", status.Forwarding.PhysicalMotionEnabled),
        ];

        items.Add(NativeMenuItem.Separator);
        items.Add(NativeMenuItem.Menu(
            "Virtual Mouse",
            [
                NativeMenuItem.Status("Output", AppText.FormatMouseOutput(status.MouseForwarding)),
                NativeMenuItem.Status(
                    "Client endpoints",
                    status.MouseForwarding.Clients.Count.ToString(CultureInfo.InvariantCulture)),
                CreateBoolStatus("Active client", status.MouseForwarding.ActiveClientId is not null),
            ]));

        if (status.Forwarding.Slots.Count == 0)
        {
            items.Add(NativeMenuItem.Status("Controller slots", AppText.None));
            return NativeMenuItem.Menu("Outputs", items);
        }

        foreach (ControllerSlotStatus slot in status.Forwarding.Slots)
        {
            items.Add(NativeMenuItem.Menu(
                AppText.ControllerSlot(slot),
                [
                    NativeMenuItem.Status("Output", AppText.Output(slot.Output), slot.OutputConnected),
                    CreateBoolStatus("Physical", slot.HasPhysicalEndpoint),
                    NativeMenuItem.Status("Client endpoints", slot.ClientEndpointCount.ToString(CultureInfo.InvariantCulture)),
                    CreateBoolStatus("Active client", slot.HasActiveClientEndpoint),
                ]));
        }

        return NativeMenuItem.Menu("Outputs", items);
    }

    private static NativeMenuItem CreateSteamInputMenu(ServerStatus? status)
    {
        if (status is null)
        {
            return NativeMenuItem.Menu("Steam input", [NativeMenuItem.Disabled(AppText.WaitingForStatus)]);
        }

        List<NativeMenuItem> items =
        [
            CreateBoolStatus("Forced", status.SteamInput.Forced),
            NativeMenuItem.Status("App ID", AppText.AppId(status.SteamInput.AppId)),
        ];

        if (!string.IsNullOrWhiteSpace(status.SteamInput.LastError))
        {
            items.Add(NativeMenuItem.Separator);
            items.Add(NativeMenuItem.Disabled(AppText.Error(status.SteamInput.LastError)));
        }

        return NativeMenuItem.Menu("Steam input", items);
    }

    private static NativeMenuItem CreateBoolStatus(string label, bool value)
    {
        return NativeMenuItem.Status(label, AppText.Enabled(value), isChecked: value);
    }

    private static NativeMenuItem CreateValueMenu(string title, IReadOnlyList<string> values)
    {
        if (values.Count == 0)
        {
            return NativeMenuItem.Menu(title, [NativeMenuItem.Disabled(AppText.None)]);
        }

        List<NativeMenuItem> items = [];
        foreach (string value in values)
        {
            items.Add(NativeMenuItem.Disabled(value));
        }

        return NativeMenuItem.Menu(title, items);
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

    private static List<string> GetControllerStreamLabels(IReadOnlyList<ControllerPipeStatus> pipes)
    {
        List<string> controllers = [];
        foreach (ControllerPipeStatus pipe in pipes)
        {
            foreach (ClientControllerStatus controller in pipe.Controllers)
            {
                controllers.Add(
                    $"{controller.Label} ({controller.InputFrameCount.ToString(CultureInfo.InvariantCulture)} frames)");
            }
        }

        return controllers;
    }

    private static IReadOnlyList<string> GetHidHideDeviceDisplayValues(ServerHidHideStatus hidHide)
    {
        return hidHide.HiddenDeviceLabels.Count == hidHide.HiddenDevices.Count
            ? hidHide.HiddenDeviceLabels
            : hidHide.HiddenDevices;
    }

    private static List<string> GetApplicationNames(IReadOnlyList<string> paths)
    {
        List<string> names = [];
        foreach (string path in paths)
        {
            string? name = Path.GetFileName(path);
            names.Add(string.IsNullOrWhiteSpace(name) ? path : name);
        }

        return names;
    }
}
