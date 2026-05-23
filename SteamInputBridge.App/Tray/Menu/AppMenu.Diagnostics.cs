using System.Collections.Generic;
using System.Globalization;
using System.IO;
using SteamInputBridge.Forwarding.Controller.Routing;
using SteamInputBridge.Forwarding.Mouse;
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
            CreateBoolStatus("Active scope", hidHide.Active),
            CreateBoolStatus("Cloak mode", hidHide.CloakEnabled),
            CreateBoolStatus("Inverse mode", hidHide.InverseEnabled),
            NativeMenuItem.Separator,
            CreateValueMenu("Hidden physical devices", GetHidHideDeviceDisplayValues(hidHide)),
            CreateValueMenu("Allowed applications", GetApplicationNames(hidHide.RegisteredApplications)),
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

        List<NativeMenuItem> items =
        [
            CreatePhysicalControllersMenu(status.Inputs.Controller),
            CreateClientStreamsMenu(status.ControllerPipes),
            NativeMenuItem.Status("Raw Input Mouse", AppText.MouseInput(status.Inputs.Mouse), status.Inputs.Mouse.Running),
        ];

        if (!string.IsNullOrWhiteSpace(status.Inputs.Mouse.LastError))
        {
            items.Add(NativeMenuItem.Separator);
            items.Add(NativeMenuItem.Disabled(AppText.Error("Raw input", status.Inputs.Mouse.LastError)));
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
            CreateBoolStatus("Controller Emulation", status.Forwarding.ControllerOutputEnabled),
            CreateBoolStatus("Motion", status.Forwarding.PhysicalMotionEnabled),
            CreateBoolStatus("Mouse Emulation", status.MouseForwarding.MouseOutputEnabled),
            CreateBoolStatus("Pointer", status.MouseForwarding.PointerOutputEnabled),
        ];

        items.Add(NativeMenuItem.Separator);
        items.Add(CreateVirtualMouseMenu(status.MouseForwarding));
        items.Add(CreateControllerSlotsMenu(status.Forwarding.Slots));

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

    private static NativeMenuItem CreatePhysicalControllersMenu(ControllerInputPumpStatus status)
    {
        List<NativeMenuItem> items =
        [
            CreateBoolStatus("Pump", status.Running),
            NativeMenuItem.Status("Sources", AppText.Sources(status.SourceCount), status.SourceCount != 0),
        ];

        if (!string.IsNullOrWhiteSpace(status.LastError))
        {
            items.Add(NativeMenuItem.Separator);
            items.Add(NativeMenuItem.Disabled(AppText.Error("Physical controllers", status.LastError)));
        }

        return NativeMenuItem.MenuStatus(
            "Physical controllers",
            AppText.ControllerInput(status),
            items,
            isChecked: status.Running && status.SourceCount != 0);
    }

    private static NativeMenuItem CreateClientStreamsMenu(IReadOnlyList<ControllerPipeStatus> pipes)
    {
        int controllerCount = CountClientControllers(pipes);
        if (pipes.Count == 0)
        {
            return NativeMenuItem.MenuStatus(
                "Client streams",
                AppText.None,
                [NativeMenuItem.Disabled(AppText.None)]);
        }

        List<NativeMenuItem> items = [];
        foreach (ControllerPipeStatus pipe in pipes)
        {
            items.Add(CreateClientPipeMenu(pipe));
        }

        return NativeMenuItem.MenuStatus(
            "Client streams",
            AppText.Count(controllerCount),
            items,
            isChecked: controllerCount != 0);
    }

    private static NativeMenuItem CreateClientPipeMenu(ControllerPipeStatus pipe)
    {
        List<NativeMenuItem> items =
        [
            CreateBoolStatus("Connected", pipe.Connected),
            NativeMenuItem.Status("Client", AppText.ShortId(pipe.ClientId)),
            NativeMenuItem.Status("Pipe", pipe.PipeName),
            NativeMenuItem.Status("Streams", AppText.Count(pipe.Controllers.Count), pipe.Controllers.Count != 0),
        ];

        if (pipe.Controllers.Count == 0)
        {
            return NativeMenuItem.MenuStatus(
                AppText.ShortId(pipe.ClientId),
                AppText.None,
                items,
                isChecked: pipe.Connected);
        }

        items.Add(NativeMenuItem.Separator);
        foreach (ClientControllerStatus controller in pipe.Controllers)
        {
            items.Add(CreateClientControllerMenu(controller));
        }

        return NativeMenuItem.MenuStatus(
            AppText.ShortId(pipe.ClientId),
            AppText.Count(pipe.Controllers.Count),
            items,
            isChecked: pipe.Connected);
    }

    private static NativeMenuItem CreateClientControllerMenu(ClientControllerStatus controller)
    {
        string label = $"#{controller.ControllerIndex.ToString(CultureInfo.InvariantCulture)} {controller.Label}";
        List<NativeMenuItem> items =
        [
            NativeMenuItem.Status("Physical slot", AppText.ControllerRouteId(controller.PhysicalControllerId)),
            NativeMenuItem.Status("Features", AppText.Features(controller.Features), controller.Features != 0),
            NativeMenuItem.Status("Frames", controller.InputFrameCount.ToString(CultureInfo.InvariantCulture), controller.InputFrameCount != 0),
        ];

        if (!string.IsNullOrWhiteSpace(controller.PhysicalDeviceId))
        {
            items.Add(NativeMenuItem.Status("Source device", AppText.ControllerRouteId(controller.PhysicalDeviceId)));
        }

        return NativeMenuItem.MenuStatus(
            label,
            $"{controller.InputFrameCount.ToString(CultureInfo.InvariantCulture)} frames",
            items,
            isChecked: controller.InputFrameCount != 0);
    }

    private static NativeMenuItem CreateVirtualMouseMenu(MouseBrokerStatus status)
    {
        return NativeMenuItem.MenuStatus(
            "Virtual mouse",
            AppText.FormatMouseOutput(status),
            [
                NativeMenuItem.Status("Output", AppText.Output(status.Output), status.OutputConnected),
                CreateBoolStatus("Connected", status.OutputConnected),
                NativeMenuItem.Status("Client endpoints", status.Clients.Count.ToString(CultureInfo.InvariantCulture), status.Clients.Count != 0),
                CreateBoolStatus("Active client", status.ActiveClientId is not null),
            ],
            isChecked: status.OutputConnected);
    }

    private static NativeMenuItem CreateControllerSlotsMenu(IReadOnlyList<ControllerSlotStatus> slots)
    {
        if (slots.Count == 0)
        {
            return NativeMenuItem.MenuStatus(
                "Controller slots",
                AppText.None,
                [NativeMenuItem.Disabled(AppText.None)]);
        }

        List<NativeMenuItem> items = [];
        foreach (ControllerSlotStatus slot in slots)
        {
            items.Add(CreateControllerSlotMenu(slot));
        }

        return NativeMenuItem.MenuStatus(
            "Controller slots",
            AppText.Count(slots.Count),
            items,
            isChecked: HasConnectedControllerOutput(slots));
    }

    private static NativeMenuItem CreateControllerSlotMenu(ControllerSlotStatus slot)
    {
        return NativeMenuItem.MenuStatus(
            AppText.ControllerSlotName(slot.ControllerId),
            AppText.ControllerSlotOutput(slot),
            [
                NativeMenuItem.Status("Output", AppText.ControllerSlotOutput(slot), slot.OutputConnected),
                CreateBoolStatus("Physical input", slot.HasPhysicalEndpoint),
                NativeMenuItem.Status("Client streams", AppText.Count(slot.ClientEndpointCount), slot.ClientEndpointCount != 0),
                CreateBoolStatus("Active stream", slot.HasActiveClientEndpoint),
                NativeMenuItem.Status("Physical features", AppText.Features(slot.PhysicalFeatures), slot.PhysicalFeatures.GetValueOrDefault() != 0),
                NativeMenuItem.Status("Active features", AppText.Features(slot.ActiveClientFeatures), slot.ActiveClientFeatures.GetValueOrDefault() != 0),
                NativeMenuItem.Status("Route ID", AppText.ControllerRouteId(slot.ControllerId.Value)),
            ],
            isChecked: slot.OutputConnected);
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

    private static int CountClientControllers(IReadOnlyList<ControllerPipeStatus> pipes)
    {
        int count = 0;
        foreach (ControllerPipeStatus pipe in pipes)
        {
            count += pipe.Controllers.Count;
        }

        return count;
    }

    private static IReadOnlyList<string> GetHidHideDeviceDisplayValues(ServerHidHideStatus hidHide)
    {
        if (hidHide.HiddenDeviceLabels.Count == hidHide.HiddenDevices.Count)
        {
            return hidHide.HiddenDeviceLabels;
        }

        List<string> devices = [];
        foreach (string device in hidHide.HiddenDevices)
        {
            devices.Add(AppText.ControllerRouteId(device));
        }

        return devices;
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
