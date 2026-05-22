using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using SteamInputBridge.Hosting.Server.Orchestration;
using SteamInputBridge.Runtime;

namespace SteamInputBridge.App.Tray.Menu;

internal sealed partial class AppMenu(
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
            return NativeMenuItem.Menu("Clients", [NativeMenuItem.Disabled(AppText.None)]);
        }

        List<NativeMenuItem> items = [];
        foreach (ClientStatus client in status.Runtime.Clients)
        {
            items.Add(NativeMenuItem.MenuStatus(
                client.ProfileId,
                string.Empty,
                [
                    NativeMenuItem.Status("State", AppText.Active(client.IsActive), client.IsActive),
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
