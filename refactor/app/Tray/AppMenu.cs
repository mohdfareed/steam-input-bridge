using System.Diagnostics;
using System.IO;
using VirtualMouse.Forwarding;
using VirtualMouse.Hosting;
using VirtualMouse.Runtime;

namespace VirtualMouse.Tray;

internal sealed class AppMenu(
    string settingsPath,
    string? logPath,
    Action exportSrm,
    Action exit)
{
    public void Show(Point location, IntPtr owner, ServerStatus? status, string? serverError)
    {
        NativeMenu.Show(location, owner, BuildItems(status, serverError));
    }

    private IReadOnlyList<NativeMenuItem> BuildItems(ServerStatus? status, string? serverError)
    {
        return
        [
            NativeMenuItem.Disabled(AppText.Header(serverError)),
            NativeMenuItem.Separator,
            CreateStartupItem(),
            NativeMenuItem.Action("Open settings", () => OpenFile(settingsPath)),
            CreateOpenLogsItem(),
            NativeMenuItem.Action("Export SRM manifest", exportSrm),
            NativeMenuItem.Separator,
            CreateClientsMenu(status),
            CreateInputsMenu(status),
            CreateOutputsMenu(status),
            CreateSteamInputMenu(status),
            NativeMenuItem.Separator,
            NativeMenuItem.Action("Exit", exit),
        ];
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

    private static NativeMenuItem CreateClientsMenu(ServerStatus? status)
    {
        if (status is null || status.Runtime.Clients.Count == 0)
        {
            return NativeMenuItem.Menu("Clients", [NativeMenuItem.Disabled("none")]);
        }

        List<NativeMenuItem> items =
        [
            NativeMenuItem.Disabled($"connected: {status.ConnectedClientCount}"),
        ];
        foreach (ClientStatus client in status.Runtime.Clients)
        {
            items.Add(NativeMenuItem.Menu(
                $"{AppText.Active(client.IsActive)} {client.ProfileId}",
                [
                    NativeMenuItem.Disabled($"client: {client.ClientId}"),
                    NativeMenuItem.Disabled($"process: {client.ClientProcessId}"),
                    NativeMenuItem.Disabled($"steam app: {AppText.AppId(client.SteamAppId)}"),
                    NativeMenuItem.Disabled($"observed: {AppText.Processes(client.ObservedProcesses)}"),
                    NativeMenuItem.Disabled($"owned: {AppText.Processes(client.OwnedProcesses)}"),
                    NativeMenuItem.Disabled($"blocked: {AppText.Processes(client.BlockedProcesses)}"),
                ]));
        }

        return NativeMenuItem.Menu("Clients", items);
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
            NativeMenuItem.Disabled(AppText.PhysicalSdl(physical)),
            NativeMenuItem.Disabled($"raw input: {AppText.Running(status.Inputs.Mouse.Running)}"),
            NativeMenuItem.Disabled(
                $"raw input source: {AppText.Connected(status.Inputs.Mouse.SourceConnected)}"),
        ];

        if (!string.IsNullOrWhiteSpace(physical.LastError))
        {
            items.Add(NativeMenuItem.Disabled($"SDL error: {physical.LastError}"));
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
            NativeMenuItem.Disabled(AppText.FormatMouseOutput(status.MouseForwarding)),
            NativeMenuItem.Disabled(
                $"controller output: {AppText.Enabled(status.Forwarding.ControllerOutputEnabled)}"),
            NativeMenuItem.Disabled(
                $"physical motion: {AppText.Enabled(status.Forwarding.PhysicalMotionEnabled)}"),
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
                    NativeMenuItem.Disabled($"output: {AppText.Output(slot.Output)}"),
                    NativeMenuItem.Disabled($"physical: {AppText.Connected(slot.HasPhysicalEndpoint)}"),
                    NativeMenuItem.Disabled($"steam endpoints: {slot.SteamEndpointCount}"),
                    NativeMenuItem.Disabled($"active steam: {AppText.YesNo(slot.HasActiveSteamEndpoint)}"),
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
            NativeMenuItem.Disabled($"forced: {AppText.YesNo(status.SteamInput.Forced)}"),
            NativeMenuItem.Disabled($"app id: {AppText.AppId(status.SteamInput.AppId)}"),
            NativeMenuItem.Disabled($"client: {status.SteamInput.ClientId?.ToString() ?? "none"}"),
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
