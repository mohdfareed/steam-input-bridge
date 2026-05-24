using System;
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
using SteamInputBridge.Settings;

namespace SteamInputBridge.App.Tray.Core;

internal sealed partial class AppContext : IDisposable
{
    private readonly IHost _app;
    private readonly ServerService _server;
    private readonly Icon _icon = LoadApplicationIcon();
    private readonly NotifyIcon _tray = new();
    private readonly NativeWindow _window = new();
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly CancellationTokenSource _stop = new();
    private readonly AppMenu _menu;
    private Task? _serverTask;
    private bool _refreshing;
    private string? _serverError;
    private ServerStatus? _status;

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
            ShutdownApp);

        _server.StatusChanged += OnServerStatusChanged;
    }

    public static AppContext Create()
    {
        IHost? app = AppSetup.CreateTray();
        try
        {
            AppContext context = new(app);
            app = null;
            return context;
        }
        finally
        {
            app?.Dispose();
        }
    }

    public void Start()
    {
        _serverTask = Task.Run(RunServerAsync, CancellationToken.None);
        _window.CreateHandle(new CreateParams());
        WindowsThemeSupport.ApplyToWindow(_window.Handle);
        _tray.Icon = _icon;
        _tray.Text = AppText.TrayStarting;
        _tray.Visible = true;
        _tray.MouseUp += ShowMenu;
        _ = RefreshStatusNowAsync();
    }

    public void Dispose()
    {
        _server.StatusChanged -= OnServerStatusChanged;
        _stop.Cancel();
        _tray.MouseUp -= ShowMenu;
        _tray.Visible = false;
        _tray.Dispose();
        _icon.Dispose();

        try
        {
            _ = _serverTask?.Wait(TimeSpan.FromSeconds(5));
        }
        catch (AggregateException exception) when (IsExpectedStop(exception))
        {
        }

        _window.DestroyHandle();
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
            SrmExport.ExportOnServerStartup(_app.Services);
            await _server.RunAsync(_stop.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            _serverError = exception.Message;
            QueueStatusRefresh();
        }
    }

    private void OnServerStatusChanged(object? sender, EventArgs args)
    {
        _ = sender;
        _ = args;
        QueueStatusRefresh();
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

            _tray.Text = AppText.TrayText(_status, _serverError);
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void ShowMenu(object? sender, MouseEventArgs args)
    {
        _ = sender;
        if (args.Button == MouseButtons.Right)
        {
            _ = ShowMenuAsync();
        }
    }

    private async Task ShowMenuAsync()
    {
        await RefreshStatusNowAsync().ConfigureAwait(true);
        _menu.Show(Cursor.Position, _window.Handle, _status, _serverError);
    }

    private static bool IsExpectedStop(AggregateException exception)
    {
        return exception.GetBaseException() is OperationCanceledException or ObjectDisposedException;
    }

}
