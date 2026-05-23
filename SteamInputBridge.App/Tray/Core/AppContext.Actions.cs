using System;
using System.Diagnostics;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using SteamInputBridge.App.Tray.Menu;

namespace SteamInputBridge.App.Tray.Core;

internal sealed partial class AppContext
{
    private static void ShutdownApp()
    {
        System.Windows.Application.Current.Shutdown();
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

        _ = Process.Start(new ProcessStartInfo
        {
            FileName = processPath,
            Arguments = $"tray --wait-parent {Environment.ProcessId}",
            WorkingDirectory = System.AppContext.BaseDirectory,
            UseShellExecute = false,
        });

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
