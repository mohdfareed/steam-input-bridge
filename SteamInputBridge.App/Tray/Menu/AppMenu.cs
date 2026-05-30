using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using SteamInputBridge.Hosting.Server.Orchestration;
using SteamInputBridge.Runtime;
using Vanara.PInvoke;
using static Vanara.PInvoke.Shell32;

namespace SteamInputBridge.App.Tray.Menu;

internal sealed partial class AppMenu(
    string settingsPath,
    string? logPath,
    Action exportSrm,
    Action restart,
    Action openDesktopSteamInputConfig,
    Action<uint> openSteamInputConfig,
    Action<Guid> stopClient,
    Action exit)
{
    public void Show(Point location, ServerStatus? status, string? serverError, Action? closed)
    {
        TrayPopupMenu.Show(location, BuildItems(status, serverError), closed);
    }

    private List<TrayMenuItem> BuildItems(ServerStatus? status, string? serverError)
    {
        List<TrayMenuItem> items = [];
        if (!string.IsNullOrWhiteSpace(serverError))
        {
            items.Add(TrayMenuItem.Disabled(AppText.Header(serverError)));
            items.Add(TrayMenuItem.Separator);
        }

        items.AddRange(
        [
            CreateClientsMenu(status, openSteamInputConfig, stopClient),
            CreateDiagnosticsMenu(status),
            TrayMenuItem.Separator,
            TrayMenuItem.Action("Open desktop Steam Input config", openDesktopSteamInputConfig),
            TrayMenuItem.Action("Export SRM manifest", exportSrm),
            TrayMenuItem.Separator,
            TrayMenuItem.Action("Open settings", () => OpenFile(settingsPath)),
            CreateOpenLogsItem(),
            TrayMenuItem.Separator,
            CreateStartupItem(),
            TrayMenuItem.Action("Restart", restart),
            TrayMenuItem.Action("Exit", exit),
        ]);

        return items;
    }

    private static TrayMenuItem CreateStartupItem()
    {
        bool startupEnabled = StartupRegistration.IsEnabled();
        return TrayMenuItem.Action(
            "Start on startup",
            () => StartupRegistration.SetEnabled(!startupEnabled),
            isChecked: startupEnabled);
    }

    private TrayMenuItem CreateOpenLogsItem()
    {
        return string.IsNullOrWhiteSpace(logPath)
            ? TrayMenuItem.Disabled("Open logs (not configured)")
            : TrayMenuItem.Action("Open logs", () => OpenLogFile(logPath));
    }

    private static TrayMenuItem CreateClientsMenu(
        ServerStatus? status,
        Action<uint> openSteamInputConfig,
        Action<Guid> stopClient)
    {
        if (status is null || status.Runtime.Clients.Count == 0)
        {
            return TrayMenuItem.Menu("Clients", [TrayMenuItem.Disabled(AppText.None)]);
        }

        List<TrayMenuItem> items = [];
        foreach (ClientStatus client in status.Runtime.Clients)
        {
            items.Add(TrayMenuItem.MenuStatus(
                client.ProfileId,
                string.Empty,
                CreateClientItems(client, openSteamInputConfig, stopClient),
                isChecked: client.IsActive));
        }

        return TrayMenuItem.Menu("Clients", items);
    }

    private static List<TrayMenuItem> CreateClientItems(
        ClientStatus client,
        Action<uint> openSteamInputConfig,
        Action<Guid> stopClient)
    {
        List<TrayMenuItem> items =
        [
            TrayMenuItem.Status("State", AppText.Active(client.IsActive), client.IsActive),
            TrayMenuItem.Status("PID", client.ClientProcessId.ToString(CultureInfo.InvariantCulture)),
            TrayMenuItem.Status("Steam App", AppText.AppId(client.SteamAppId)),
            TrayMenuItem.Status("Receivers", AppText.Processes(client.ObservedProcesses)),
            TrayMenuItem.Separator,
        ];

        if (client.SteamAppId is uint appId)
        {
            items.Add(TrayMenuItem.Action("Open Steam Input config", () => openSteamInputConfig(appId)));
        }
        else
        {
            items.Add(TrayMenuItem.Disabled("Open Steam Input config (no app id)"));
        }

        items.Add(TrayMenuItem.Action("Stop client", () => stopClient(client.ClientId)));
        return items;
    }

    private static void OpenFile(string path)
    {
        string fullPath = Path.GetFullPath(path);
        IntPtr result = ShellExecute(default, "open", fullPath, null, null, ShowWindowCommand.SW_SHOWNORMAL);
        if (result.ToInt64() <= 32)
        {
            throw new InvalidOperationException($"Could not open {fullPath}.");
        }
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
