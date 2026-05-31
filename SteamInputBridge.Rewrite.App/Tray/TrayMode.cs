using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Extensions.Hosting;

namespace SteamInputBridge.App.Tray;

internal static class TrayMode
{
    public static int Run()
    {
        using TrayContext context = new();
        context.StartAsync().ConfigureAwait(true).GetAwaiter().GetResult();
        Application.Run(context);
        return 0;
    }

}

internal sealed class TrayContext : ApplicationContext
{
    private static string AppName => "Steam Input Bridge";

    private readonly IHost _server = AppHost.CreateServer();
    private readonly StatusOverlayController _overlay;
    private readonly NotifyIcon _tray = new();
    private readonly Icon _icon = LoadApplicationIcon();
    private readonly CancellationTokenSource _stop = new();
    private readonly ContextMenuStrip _menu;
    private Task _serverTask = Task.CompletedTask;

    public TrayContext()
    {
        _overlay = new(_server.Services);
        _menu = TrayMenu.Create(ExitThread);
    }

    public async Task StartAsync()
    {
        await _server.StartAsync(_stop.Token).ConfigureAwait(true);
        _serverTask = _server.WaitForShutdownAsync(_stop.Token);

        _tray.Icon = _icon;
        _tray.Text = AppName;
        _tray.ContextMenuStrip = _menu;
        _tray.Visible = true;
        _overlay.Start();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
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

            try
            {
                _ = _serverTask.Wait(TimeSpan.FromSeconds(5));
            }
            catch (AggregateException exception)
                when (exception.GetBaseException() is OperationCanceledException or ObjectDisposedException)
            {
            }

            _server.Dispose();
            _stop.Dispose();
        }

        base.Dispose(disposing);
    }

    private static Icon LoadApplicationIcon()
    {
        return Environment.ProcessPath is { Length: > 0 } processPath
            ? Icon.ExtractAssociatedIcon(processPath) ?? (Icon)SystemIcons.Application.Clone()
            : (Icon)SystemIcons.Application.Clone();
    }
}
