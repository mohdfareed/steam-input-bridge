using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using SteamInputBridge.App.Tray.Menu;
using SteamInputBridge.Steam;
using Vanara.PInvoke;
using static Vanara.PInvoke.Shell32;

namespace SteamInputBridge.App.Tray.Core;

internal sealed partial class AppContext
{
    private static void ShutdownApp()
    {
        System.Windows.Application.Current.Shutdown();
    }

    private void OpenDesktopSteamInputConfig()
    {
        OpenSteamInputConfig(SteamInputClient.DesktopConfigAppId);
    }

    private void OpenSteamInputConfig(uint appId)
    {
        _ = OpenSteamInputConfigAsync(appId);
    }

    private async Task OpenSteamInputConfigAsync(uint appId)
    {
        try
        {
            SteamInputClient steam = new();
            await steam.OpenControllerConfigAsync(appId, _stop.Token).ConfigureAwait(true);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or ArgumentException or OperationCanceledException)
        {
            if (!_stop.IsCancellationRequested)
            {
                _tray.ShowBalloonTip(
                    5000,
                    "Steam Input Bridge",
                    $"Could not open Steam Input config: {exception.Message}",
                    ToolTipIcon.Error);
            }
        }
    }

    private void StopClient(Guid clientId)
    {
        _ = StopClientAsync(clientId);
    }

    private async Task StopClientAsync(Guid clientId)
    {
        await _server.StopClientAsync(clientId).ConfigureAwait(true);
        _status = await _server.GetStatusAsync().ConfigureAwait(true);
        _tray.Text = AppText.TrayText(_status, _serverError);
    }

    private static void RestartApp()
    {
        if (Environment.ProcessPath is not { Length: > 0 } processPath)
        {
            return;
        }

        IntPtr result = ShellExecute(
            default,
            "open",
            processPath,
            $"tray --wait-parent {Environment.ProcessId}",
            System.AppContext.BaseDirectory,
            ShowWindowCommand.SW_HIDE);
        if (result.ToInt64() <= 32)
        {
            return;
        }

        ShutdownApp();
    }

    private static Icon LoadApplicationIcon()
    {
        return Environment.ProcessPath is { Length: > 0 } processPath
            ? Icon.ExtractAssociatedIcon(processPath) ?? (Icon)SystemIcons.Application.Clone()
            : (Icon)SystemIcons.Application.Clone();
    }

    private void ExportSrmManifest()
    {
        SrmExportResult result = SrmExport.Export(_app.Services);
        if (result.Exported)
        {
            _tray.ShowBalloonTip(
                3000,
                "Steam Input Bridge",
                $"Exported {result.ProfileCount} SRM profiles.",
                ToolTipIcon.Info);
            return;
        }

        _tray.ShowBalloonTip(
            5000,
            "Steam Input Bridge",
            $"Could not export SRM manifest: {result.Error}",
            ToolTipIcon.Error);
    }
}
