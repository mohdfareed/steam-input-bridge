using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SteamInputBridge.App.Host;
using SteamInputBridge.Hosting.Server;
using SteamInputBridge.Settings;
using SteamInputBridge.Steam;

namespace SteamInputBridge.App.Tray.Menu;

internal sealed class TrayActions(
    IHost server,
    AppEnvironment environment,
    SettingsFile settingsFile,
    BridgeService bridgeService,
    NotifyIcon tray,
    CancellationToken cancellationToken)
{
    private const string AppName = "Steam Input Bridge";

    // MARK: Publics
    // ========================================================================

    public static bool StartupEnabled => StartupRegistration.IsEnabled();

    public async Task OpenDesktopSteamInputConfigAsync()
    {
        SteamInputClient steam = new();
        await steam.OpenSteamConfigAsync(SteamInputClient.DesktopConfigAppId, cancellationToken).ConfigureAwait(true);
    }

    public async Task OpenSteamInputConfigAsync(uint appId)
    {
        SteamInputClient steam = new();
        await steam.OpenSteamConfigAsync(appId, cancellationToken).ConfigureAwait(true);
    }

    public void ExportSrmManifest()
    {
        SettingsService settings = server.Services.GetRequiredService<SettingsService>();
        string manifestPath = SteamRomManagerExport.WriteManifest(settings, settingsFile, environment.ExecutablePath);
        tray.ShowBalloonTip(5000, AppName, $"Exported SRM manifest to {manifestPath}.", ToolTipIcon.Info);
    }

    public void OpenSettings()
    {
        OpenFile(settingsFile.Path);
    }

    public void OpenLogs()
    {
        string? directory = Path.GetDirectoryName(environment.LogPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            _ = Directory.CreateDirectory(directory);
        }

        using (File.Open(environment.LogPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite))
        {
        }

        OpenFile(environment.LogPath);
    }

    public static void ToggleStartup()
    {
        StartupRegistration.SetEnabled(!StartupRegistration.IsEnabled());
    }

    public Task StopClientAsync(Guid connectionId)
    {
        return bridgeService.StopClientAsync(connectionId);
    }

    // MARK: Implementation
    // ========================================================================

    private static void OpenFile(string path)
    {
        string fullPath = Path.GetFullPath(path);
        ProcessStartInfo start = new()
        {
            FileName = fullPath,
            UseShellExecute = true,
        };
        _ = Process.Start(start) ?? throw new InvalidOperationException($"Could not open {fullPath}.");
    }
}
