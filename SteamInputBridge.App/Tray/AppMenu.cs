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
                client.IsActive ? "active" : "idle",
                [
                    NativeMenuItem.Status("pid", client.ClientProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                    NativeMenuItem.Status("steam app", AppText.AppId(client.SteamAppId)),
                    NativeMenuItem.Status("receivers", AppText.Processes(client.ObservedProcesses)),
                    NativeMenuItem.Status("blocked", AppText.Processes(client.BlockedProcesses)),
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
            return NativeMenuItem.Menu("HidHide", [NativeMenuItem.Disabled("waiting for status")]);
        }

        ServerHidHideStatus hidHide = status.HidHide;
        List<NativeMenuItem> items =
        [
            CreateBoolStatus("cloak", hidHide.CloakEnabled),
            CreateBoolStatus("inverse", hidHide.InverseEnabled),
            CreateBoolStatus("active scope", hidHide.Active),
            CreateValueMenu("Hidden devices", hidHide.HiddenDevices),
            CreateValueMenu("Allowed apps", hidHide.RegisteredApplications),
        ];

        if (!string.IsNullOrWhiteSpace(hidHide.LastError))
        {
            items.Add(NativeMenuItem.Disabled($"error: {hidHide.LastError}"));
        }

        return NativeMenuItem.Menu("HidHide", items);
    }

    private static NativeMenuItem CreateBoolStatus(string label, bool value)
    {
        return NativeMenuItem.Status(label, value ? "enabled" : "disabled", isChecked: value);
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

    private static NativeMenuItem CreateInputsMenu(ServerStatus? status)
    {
        if (status is null)
        {
            return NativeMenuItem.Menu("Inputs", [NativeMenuItem.Disabled("waiting for status")]);
        }

        PhysicalControllerPumpStatus physical = status.Inputs.PhysicalControllers;
        List<NativeMenuItem> items =
        [
            NativeMenuItem.Status("controllers", AppText.PhysicalSdl(physical)),
            NativeMenuItem.Status("mouse", AppText.MouseInput(status.Inputs.Mouse), status.Inputs.Mouse.Running),
        ];

        if (!string.IsNullOrWhiteSpace(physical.LastError))
        {
            items.Add(NativeMenuItem.Disabled($"controller error: {physical.LastError}"));
        }

        if (!string.IsNullOrWhiteSpace(status.Inputs.Mouse.LastError))
        {
            items.Add(NativeMenuItem.Disabled($"raw input error: {status.Inputs.Mouse.LastError}"));
        }

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
            return NativeMenuItem.Menu("Outputs", [NativeMenuItem.Disabled("waiting for status")]);
        }

        List<NativeMenuItem> items =
        [
            CreateBoolStatus("controller gate", status.Forwarding.ControllerOutputEnabled),
            CreateBoolStatus("motion", status.Forwarding.PhysicalMotionEnabled),
            NativeMenuItem.Status(
                "mouse device",
                AppText.FormatMouseOutput(status.MouseForwarding),
                status.MouseForwarding.OutputConnected),
            CreateBoolStatus("mouse gate", status.MouseForwarding.MouseOutputEnabled),
            CreateBoolStatus("pointer", status.MouseForwarding.PointerOutputEnabled),
        ];

        if (status.Forwarding.Slots.Count == 0)
        {
            items.Add(NativeMenuItem.Disabled("controller slots: none"));
            return NativeMenuItem.Menu("Outputs", items);
        }

        foreach (ControllerSlotStatus slot in status.Forwarding.Slots)
        {
            items.Add(NativeMenuItem.Menu(
                AppText.ControllerSlot(slot),
                [
                    NativeMenuItem.Status("output", AppText.Output(slot.Output), slot.OutputConnected),
                    CreateBoolStatus("physical", slot.HasPhysicalEndpoint),
                    NativeMenuItem.Status("client endpoints", slot.ClientEndpointCount.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                    CreateBoolStatus("active client", slot.HasActiveClientEndpoint),
                ]));
        }

        return NativeMenuItem.Menu("Outputs", items);
    }

    private static NativeMenuItem CreateSteamInputMenu(ServerStatus? status)
    {
        if (status is null)
        {
            return NativeMenuItem.Menu("Steam Input", [NativeMenuItem.Disabled("waiting for status")]);
        }

        List<NativeMenuItem> items =
        [
            CreateBoolStatus("forced", status.SteamInput.Forced),
            NativeMenuItem.Status("app id", AppText.AppId(status.SteamInput.AppId)),
        ];

        if (!string.IsNullOrWhiteSpace(status.SteamInput.LastError))
        {
            items.Add(NativeMenuItem.Disabled($"error: {status.SteamInput.LastError}"));
        }

        return NativeMenuItem.Menu("Steam Input", items);
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
