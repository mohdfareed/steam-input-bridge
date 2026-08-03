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
    TeensyFirmwareUploader firmwareUploader,
    NotifyIcon tray,
    CancellationToken cancellationToken)
{
    private const string UninstallScriptName = "Uninstall-App.ps1";

    // MARK: Publics
    // ========================================================================

    public static bool StartupEnabled => StartupRegistration.IsEnabled();

    public string Version => environment.Version;

    public bool CanUninstall => File.Exists(Path.Combine(environment.BaseDirectory, UninstallScriptName));

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
        tray.ShowBalloonTip(
            5000,
            ProductMetadata.DisplayName,
            $"Exported SRM manifest to {manifestPath}.",
            ToolTipIcon.Info);
    }

    public void OpenSettings()
    {
        if (!File.Exists(settingsFile.Path))
        {
            _ = Directory.CreateDirectory(settingsFile.DirectoryPath);
            File.WriteAllText(
                settingsFile.Path,
                $$"""
                {
                  "{{SteamInputBridgeSettings.SectionName}}": {}
                }
                """);
        }

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

    public Task UploadTeensyFirmwareAsync()
    {
        return firmwareUploader.UploadAsync();
    }

    public static void ToggleStartup()
    {
        StartupRegistration.SetEnabled(!StartupRegistration.IsEnabled());
    }

    public Task StopClientAsync(Guid connectionId)
    {
        return bridgeService.StopClientAsync(connectionId);
    }

    public void Uninstall()
    {
        string scriptPath = Path.Combine(environment.BaseDirectory, UninstallScriptName);
        if (!File.Exists(scriptPath))
        {
            throw new FileNotFoundException("The uninstall script does not exist beside the application.", scriptPath);
        }

        DialogResult confirmation = MessageBox.Show(
            "Uninstall Steam Input Bridge? Settings and logs will be preserved.",
            ProductMetadata.DisplayName,
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);
        if (confirmation != DialogResult.Yes)
        {
            return;
        }

        ProcessStartInfo start = new()
        {
            FileName = "powershell.exe",
            WorkingDirectory = environment.BaseDirectory,
            UseShellExecute = true,
        };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NoExit");
        start.ArgumentList.Add("-ExecutionPolicy");
        start.ArgumentList.Add("Bypass");
        start.ArgumentList.Add("-File");
        start.ArgumentList.Add(scriptPath);
        _ = Process.Start(start) ?? throw new InvalidOperationException("Could not start the uninstaller.");
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
