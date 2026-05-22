using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using SteamInputBridge.Forwarding.Controller;
using SteamInputBridge.Hosting.Server;
using SteamInputBridge.Runtime;

namespace SteamInputBridge.App.Tray;

internal sealed class AppMenu(
    string settingsPath,
    string? logPath,
    Action exportSrm,
    Action restart,
    Action<Guid> stopClient,
    Action exit)
{
    public void Show(Point location, IntPtr owner, ServerStatus? status, string? serverError)
    {
        NativeMenu.Show(location, owner, BuildItems(status, serverError));
    }

    private List<NativeMenuItem> BuildItems(ServerStatus? status, string? serverError)
    {
        List<NativeMenuItem> items = [];
        if (!string.IsNullOrWhiteSpace(serverError))
        {
            items.Add(NativeMenuItem.Disabled(AppText.Header(serverError)));
            items.Add(NativeMenuItem.Separator);
        }

        items.AddRange(
        [
            CreateClientsMenu(status, stopClient),
            CreateDiagnosticsMenu(status),
            NativeMenuItem.Separator,
            NativeMenuItem.Action("Open settings", () => OpenFile(settingsPath)),
            CreateOpenLogsItem(),
            NativeMenuItem.Action("Export SRM manifest", exportSrm),
            NativeMenuItem.Separator,
            CreateStartupItem(),
            NativeMenuItem.Action("Restart", restart),
            NativeMenuItem.Action("Exit", exit),
        ]);

        return items;
    }

    private static NativeMenuItem CreateStartupItem()
    {
        bool startupEnabled = StartupRegistration.IsEnabled();
        return NativeMenuItem.Action(
            "Start on startup",
            () => StartupRegistration.SetEnabled(!startupEnabled),
            isChecked: startupEnabled);
    }

    private NativeMenuItem CreateOpenLogsItem()
    {
        return string.IsNullOrWhiteSpace(logPath)
            ? NativeMenuItem.Disabled("Open logs (not configured)")
            : NativeMenuItem.Action("Open logs", () => OpenLogFile(logPath));
    }

    private static NativeMenuItem CreateClientsMenu(ServerStatus? status, Action<Guid> stopClient)
    {
        if (status is null || status.Runtime.Clients.Count == 0)
        {
            return NativeMenuItem.Menu("Clients", [NativeMenuItem.Disabled("none")]);
        }

        List<NativeMenuItem> items = [];
        foreach (ClientStatus client in status.Runtime.Clients)
        {
            items.Add(NativeMenuItem.MenuStatus(
                client.ProfileId,
                string.Empty,
                [
                    NativeMenuItem.Status("State", client.IsActive ? "Active" : "Idle"),
                    NativeMenuItem.Status("PID", client.ClientProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                    NativeMenuItem.Status("Steam App", AppText.AppId(client.SteamAppId)),
                    NativeMenuItem.Status("Receivers", AppText.Processes(client.ObservedProcesses)),
                    NativeMenuItem.Status("Blocked", AppText.Processes(client.BlockedProcesses)),
                    NativeMenuItem.Separator,
                    NativeMenuItem.Action("Stop client", () => stopClient(client.ClientId)),
                ],
                isChecked: client.IsActive));
        }

        return NativeMenuItem.Menu("Clients", items);
    }

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
            return NativeMenuItem.Menu("HidHide", [NativeMenuItem.Disabled("Waiting for status")]);
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
            items.Add(NativeMenuItem.Disabled($"Error: {hidHide.LastError}"));
        }

        return NativeMenuItem.Menu("HidHide", items);
    }

    private static NativeMenuItem CreateBoolStatus(string label, bool value)
    {
        return NativeMenuItem.Status(label, value ? "Enabled" : "Disabled", isChecked: value);
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

    private static NativeMenuItem CreateInputsMenu(ServerStatus? status)
    {
        if (status is null)
        {
            return NativeMenuItem.Menu("Inputs", [NativeMenuItem.Disabled("Waiting for status")]);
        }

        PhysicalControllerPumpStatus physical = status.Inputs.PhysicalControllers;
        List<NativeMenuItem> items =
        [
            NativeMenuItem.Status("Controllers", AppText.PhysicalSdl(physical)),
            NativeMenuItem.Status("Mouse", AppText.MouseInput(status.Inputs.Mouse), status.Inputs.Mouse.Running),
        ];


        if (!string.IsNullOrWhiteSpace(physical.LastError))
        {
            items.Add(NativeMenuItem.Separator);
            items.Add(NativeMenuItem.Disabled($"Controller error: {physical.LastError}"));
        }

        if (!string.IsNullOrWhiteSpace(status.Inputs.Mouse.LastError))
        {
            items.Add(NativeMenuItem.Separator);
            items.Add(NativeMenuItem.Disabled($"Raw input error: {status.Inputs.Mouse.LastError}"));
        }

        items.Add(NativeMenuItem.Separator);
        foreach (string controller in physical.ControllerIds)
        {
            items.Add(NativeMenuItem.Disabled(controller));
        }

        return NativeMenuItem.Menu("Inputs", items);
    }

    private static NativeMenuItem CreateOutputsMenu(ServerStatus? status)
    {
        if (status is null)
        {
            return NativeMenuItem.Menu("Outputs", [NativeMenuItem.Disabled("Waiting for status")]);
        }

        List<NativeMenuItem> items =
        [
            NativeMenuItem.Status(
                "Mouse connection",
                AppText.FormatMouseOutput(status.MouseForwarding),
                status.MouseForwarding.OutputConnected),
            CreateBoolStatus("Mouse output", status.MouseForwarding.MouseOutputEnabled),
            CreateBoolStatus("Pointer", status.MouseForwarding.PointerOutputEnabled),
            NativeMenuItem.Separator,
            CreateBoolStatus("Controller output", status.Forwarding.ControllerOutputEnabled),
            CreateBoolStatus("Motion", status.Forwarding.PhysicalMotionEnabled),
        ];

        if (status.Forwarding.Slots.Count == 0)
        {
            items.Add(NativeMenuItem.Status("Controller slots", "None"));
            return NativeMenuItem.Menu("Outputs", items);
        }

        items.Add(NativeMenuItem.Separator);
        foreach (ControllerSlotStatus slot in status.Forwarding.Slots)
        {
            items.Add(NativeMenuItem.Menu(
                AppText.ControllerSlot(slot),
                [
                    NativeMenuItem.Status("Output", AppText.Output(slot.Output), slot.OutputConnected),
                    CreateBoolStatus("Physical", slot.HasPhysicalEndpoint),
                    NativeMenuItem.Status("Client endpoints", slot.ClientEndpointCount.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                    CreateBoolStatus("Active client", slot.HasActiveClientEndpoint),
                ]));
        }

        return NativeMenuItem.Menu("Outputs", items);
    }

    private static NativeMenuItem CreateSteamInputMenu(ServerStatus? status)
    {
        if (status is null)
        {
            return NativeMenuItem.Menu("Steam input", [NativeMenuItem.Disabled("Waiting for status")]);
        }

        List<NativeMenuItem> items =
        [
            CreateBoolStatus("Forced", status.SteamInput.Forced),
            NativeMenuItem.Status("App ID", AppText.AppId(status.SteamInput.AppId)),
        ];

        if (!string.IsNullOrWhiteSpace(status.SteamInput.LastError))
        {
            items.Add(NativeMenuItem.Separator);
            items.Add(NativeMenuItem.Disabled($"Error: {status.SteamInput.LastError}"));
        }

        return NativeMenuItem.Menu("Steam input", items);
    }

    private static void OpenFile(string path)
    {
        _ = Process.Start(new ProcessStartInfo
        {
            FileName = Path.GetFullPath(path),
            UseShellExecute = true,
        });
    }

    private static void OpenLogFile(string path)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            _ = Directory.CreateDirectory(directory);
        }

        using (File.Open(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite))
        {
        }

        OpenFile(path);
    }
}
