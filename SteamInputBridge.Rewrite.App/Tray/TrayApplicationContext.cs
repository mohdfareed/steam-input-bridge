using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Extensions.Hosting;
using SteamInputBridge.App.Hosting;

namespace SteamInputBridge.App.Tray;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private static string AppName => "Steam Input Bridge";

    private readonly IHost _server = AppHost.CreateServer();
    private readonly NotifyIcon _tray = new();
    private readonly Icon _icon = LoadApplicationIcon();
    private readonly CancellationTokenSource _stop = new();
    private readonly ContextMenuStrip _menu;
    private Task? _serverTask;

    public TrayApplicationContext()
    {
        _menu = TrayMenu.Create(ExitThread);
    }

    public void Start()
    {
        _tray.Icon = _icon;
        _tray.Text = AppName;
        _tray.ContextMenuStrip = _menu;
        _tray.Visible = true;
        _serverTask = Task.Run(RunServerAsync, CancellationToken.None);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _stop.Cancel();
            _tray.ContextMenuStrip = null;
            _tray.Visible = false;
            _tray.Dispose();
            _menu.Dispose();
            _icon.Dispose();

            try
            {
                _ = _serverTask?.Wait(TimeSpan.FromSeconds(5));
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

    private async Task RunServerAsync()
    {
        try
        {
            await _server.RunAsync(_stop.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private static Icon LoadApplicationIcon()
    {
        return Environment.ProcessPath is { Length: > 0 } processPath
            ? Icon.ExtractAssociatedIcon(processPath) ?? (Icon)SystemIcons.Application.Clone()
            : (Icon)SystemIcons.Application.Clone();
    }
}
