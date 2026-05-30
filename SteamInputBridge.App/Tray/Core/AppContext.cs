using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SteamInputBridge.App.Tray.Menu;
using SteamInputBridge.Hosting.Server.Orchestration;
using SteamInputBridge.Hosting.Server.Orchestration.Lifetime;
using SteamInputBridge.Runtime;
using SteamInputBridge.Settings;
using SteamInputBridge.Steam;

namespace SteamInputBridge.App.Tray.Core;

internal sealed class AppContext : IDisposable
{
    private readonly IHost _app;
    private readonly ServerService _server;
    private readonly Icon _icon = LoadApplicationIcon();
    private readonly NotifyIcon _tray = new();
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly StatusOverlayWindow _overlay = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly AppMenu _menu;
    private Task? _serverTask;
    private bool _refreshing;
    private string? _serverError;
    private ServerStatus? _status;
    private TrayActivitySnapshot? _lastActivity;

    private AppContext(IHost app)
    {
        _app = app;
        _server = app.Services.GetRequiredService<ServerService>();
        string settingsPath = app.Services.GetRequiredService<SettingsFile>().Path;
        string? logDirectory = AppSetup.ResolveLogDirectory(
            settingsPath,
            app.Services.GetRequiredService<ApplicationSettingsService>().Current.Logging.LogDirectory);
        string? logPath = logDirectory is null
            ? null
            : FileLoggingExtensions.ResolveRunLogFilePath(logDirectory);
        _menu = new AppMenu(
            settingsPath,
            logPath,
            ExportSrmManifest,
            RestartApp,
            OpenDesktopSteamInputConfig,
            OpenSteamInputConfig,
            StopClient,
            ShutdownApp,
            exception => ShowBalloonError(exception.Message));
        _server.StatusChanged += OnServerStatusChanged;
    }

    public static AppContext Create()
    {
        return new AppContext(AppSetup.CreateTray());
    }

    public void Start()
    {
        _serverTask = Task.Run(RunServerAsync, CancellationToken.None);
        _tray.Icon = _icon;
        _tray.Text = AppText.TrayStarting;
        _menu.Rebuild(_status, _serverError, _lastActivity);
        _tray.ContextMenuStrip = _menu.Menu;
        _tray.Visible = true;
        _ = RefreshStatusNowAsync();
        RefreshOverlayNow();
    }

    public void Dispose()
    {
        _server.StatusChanged -= OnServerStatusChanged;
        _stop.Cancel();
        _overlay.Close();
        _tray.ContextMenuStrip = null;
        _tray.Visible = false;
        _tray.Dispose();
        _menu.Menu.Dispose();
        _icon.Dispose();

        try
        {
            _ = _serverTask?.Wait(TimeSpan.FromSeconds(5));
        }
        catch (AggregateException exception)
            when (exception.GetBaseException() is OperationCanceledException or ObjectDisposedException)
        {
        }

        _server.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _stop.Dispose();
        _app.Dispose();
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "The tray UI stays open and reports server startup failures in the menu.")]
    private async Task RunServerAsync()
    {
        try
        {
            await _server.RunAsync(_stop.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            _serverError = exception.Message;
            _ = _dispatcher.BeginInvoke(new Action(() => AppErrorDialog.ShowException(exception)));
            QueueStatusRefresh();
        }
    }

    private void OnServerStatusChanged(object? sender, EventArgs args)
    {
        _ = sender;
        _ = args;
        QueueStatusRefresh();
        _ = _dispatcher.BeginInvoke(new Action(RefreshOverlayNow));
    }

    private void QueueStatusRefresh()
    {
        if (_stop.IsCancellationRequested)
        {
            return;
        }

        _ = _dispatcher.BeginInvoke(new Action(() => _ = RefreshStatusNowAsync()));
    }

    private async Task RefreshStatusNowAsync()
    {
        if (_refreshing)
        {
            return;
        }

        _refreshing = true;
        try
        {
            if (_serverTask?.IsFaulted == true)
            {
                _serverError = _serverTask.Exception?.GetBaseException().Message;
            }
            else if (_serverTask?.IsCompleted == false)
            {
                _status = await _server.GetStatusAsync().ConfigureAwait(true);
            }

            RefreshLastActivitySnapshot();
            _tray.Text = AppText.TrayText(_status, _serverError);
            RebuildMenuOrDefer();
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void RebuildMenuOrDefer()
    {
        if (_menu.Menu.Visible)
        {
            _menu.RefreshVisibleStatus(_status, _lastActivity);
            return;
        }

        _menu.Rebuild(_status, _serverError, _lastActivity);
    }

    private void RefreshLastActivitySnapshot()
    {
        if (_status?.Runtime.ActiveClientId is not Guid activeClientId)
        {
            return;
        }

        foreach (ClientStatus client in _status.Runtime.Clients)
        {
            if (client.ClientId == activeClientId)
            {
                _lastActivity = new TrayActivitySnapshot(client, _status.SteamInput);
                return;
            }
        }
    }

    private static void ShutdownApp()
    {
        System.Windows.Application.Current.Shutdown();
    }

    private void OpenDesktopSteamInputConfig()
    {
        _ = OpenDesktopSteamInputConfigAsync();
    }

    private async Task OpenDesktopSteamInputConfigAsync()
    {
        try
        {
            SteamInputClient steam = new();
            await steam.OpenSteamControllerDesktopConfigAsync(_stop.Token).ConfigureAwait(true);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or ArgumentException or OperationCanceledException)
        {
            ShowBalloonError($"Could not open desktop Steam Input config: {exception.Message}");
        }
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
            ShowBalloonError($"Could not open Steam Input config: {exception.Message}");
        }
    }

    private void StopClient(Guid clientId)
    {
        _ = Task.Run(() => StopClientAsync(clientId));
    }

    private async Task StopClientAsync(Guid clientId)
    {
        try
        {
            await _server.StopClientAsync(clientId).ConfigureAwait(false);
            ServerStatus status = await _server.GetStatusAsync().ConfigureAwait(false);
            _ = _dispatcher.BeginInvoke(new Action(() =>
            {
                _status = status;
                RefreshLastActivitySnapshot();
                _tray.Text = AppText.TrayText(_status, _serverError);
                RebuildMenuOrDefer();
            }));
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or OperationCanceledException or ObjectDisposedException)
        {
            _ = _dispatcher.BeginInvoke(new Action(() =>
                ShowBalloonError($"Could not stop client: {exception.Message}")));
        }
    }

    private static void RestartApp()
    {
        if (Environment.ProcessPath is not { Length: > 0 } processPath)
        {
            return;
        }

        ProcessStartInfo start = new()
        {
            FileName = processPath,
            Arguments = $"tray --wait-parent {Environment.ProcessId}",
            WorkingDirectory = System.AppContext.BaseDirectory,
            UseShellExecute = true,
        };

        using Process? process = Process.Start(start);
        if (process is not null)
        {
            ShutdownApp();
        }
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
        ShowBalloon(result.Exported
            ? $"Exported {result.ProfileCount} SRM profiles."
            : $"Could not export SRM manifest: {result.Error}",
            result.Exported ? ToolTipIcon.Info : ToolTipIcon.Error);
    }

    private void ShowBalloonError(string message)
    {
        ShowBalloon(message, ToolTipIcon.Error);
    }

    private void ShowBalloon(string message, ToolTipIcon icon)
    {
        if (_stop.IsCancellationRequested)
        {
            return;
        }

        _tray.ShowBalloonTip(5000, "Steam Input Bridge", message, icon);
    }

    private void RefreshOverlayNow()
    {
        if (_stop.IsCancellationRequested || _serverTask?.IsFaulted == true)
        {
            _overlay.Update(OverlayStatus.Hidden);
            return;
        }

        try
        {
            _overlay.Update(_server.GetOverlayStatus());
        }
        catch (ObjectDisposedException)
        {
            _overlay.Update(OverlayStatus.Hidden);
        }
    }
}
