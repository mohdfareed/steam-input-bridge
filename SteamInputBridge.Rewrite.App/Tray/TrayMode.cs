using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Extensions.Hosting;
using FormsApplication = System.Windows.Forms.Application;
using WpfApplication = System.Windows.Application;
using WpfShutdownMode = System.Windows.ShutdownMode;

namespace SteamInputBridge.App.Tray;

internal static class TrayMode
{
    // MARK: Publics
    // ========================================================================

    public static int Run()
    {
        FormsApplication.EnableVisualStyles();
        FormsApplication.SetColorMode(SystemColorMode.System);
        WpfApplication app = new()
        {
            ShutdownMode = WpfShutdownMode.OnExplicitShutdown,
        };

        using TrayContext context = new();
        context.StartAsync().ConfigureAwait(true).GetAwaiter().GetResult();
        return app.Run();
    }
}

internal sealed class TrayContext : IDisposable
{
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(5);
    private static string AppName => "Steam Input Bridge";

    private readonly IHost _server = AppHost.CreateServer();
    private readonly StatusOverlayController _overlay;
    private readonly NotifyIcon _tray = new();
    private readonly Icon _icon = LoadApplicationIcon();
    private readonly CancellationTokenSource _stop = new();
    private readonly ContextMenuStrip _menu;

    private Task _serverTask = Task.CompletedTask;
    private bool _disposed;
    private bool _shutdownStarted;

    // MARK: Lifecycle
    // ========================================================================

    public TrayContext()
    {
        _overlay = new(_server.Services);
        _menu = TrayMenu.Create(() => _ = ShutdownAsync());
    }

    public async Task StartAsync()
    {
        await _server.StartAsync(_stop.Token).ConfigureAwait(true);
        _serverTask = _server.WaitForShutdownAsync(CancellationToken.None);

        _tray.Icon = _icon;
        _tray.Text = AppName;
        _tray.ContextMenuStrip = _menu;
        _tray.Visible = true;
        _overlay.Start();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (!_shutdownStarted)
        {
            _shutdownStarted = true;
            StopServerAsync().ConfigureAwait(true).GetAwaiter().GetResult();
        }

        try
        {
            _stop.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        _tray.ContextMenuStrip = null;
        _tray.Visible = false;
        _tray.Dispose();
        _overlay.Dispose();
        _menu.Dispose();
        _icon.Dispose();

        _server.Dispose();
        _stop.Dispose();
    }

    // MARK: Shutdown
    // ========================================================================

    private async Task ShutdownAsync()
    {
        if (_shutdownStarted)
        {
            return;
        }

        _shutdownStarted = true;
        _tray.Visible = false;
        await StopServerAsync().ConfigureAwait(true);
        WpfApplication.Current.Shutdown();
    }

    private async Task StopServerAsync()
    {
        using CancellationTokenSource stopTimeout = new(ShutdownTimeout);

        try
        {
            await _server.StopAsync(stopTimeout.Token).ConfigureAwait(true);
            await _serverTask.ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (stopTimeout.IsCancellationRequested)
        {
            AppErrorDialog.Show(new TimeoutException("The server did not stop within 5 seconds."));
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
        {
            AppErrorDialog.Show(exception);
        }
    }

    // MARK: Implementation
    // ========================================================================

    private static Icon LoadApplicationIcon()
    {
        return Environment.ProcessPath is { Length: > 0 } processPath
            ? Icon.ExtractAssociatedIcon(processPath) ?? (Icon)SystemIcons.Application.Clone()
            : (Icon)SystemIcons.Application.Clone();
    }
}
