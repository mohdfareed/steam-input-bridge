using System;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SteamInputBridge.App.Host;
using SteamInputBridge.Hosting.Server;
using SteamInputBridge.Settings;
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
    private const string EnvironmentLogMessage =
        "App environment: version={Version}, executable={ExecutablePath}, baseDirectory={BaseDirectory}, " +
        "settings={SettingsPath}, log={LogPath}";

    private static readonly Action<ILogger, string, string, string, string, string, Exception?> LogEnvironment =
        LoggerMessage.Define<string, string, string, string, string>(
            LogLevel.Information,
            new EventId(1, nameof(LogEnvironment)),
            EnvironmentLogMessage);

    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(5);
    private static string AppName => "Steam Input Bridge";

    private readonly IHost _server = AppHost.CreateServer();
    private readonly BridgeService _bridgeService;
    private readonly SettingsFile _settingsFile;
    private readonly AppEnvironment _environment;
    private readonly ILogger<TrayContext> _logger;
    private readonly StatusOverlayController _overlay;
    private readonly NotifyIcon _tray = new();
    private readonly Icon _icon = LoadApplicationIcon();
    private readonly CancellationTokenSource _stop = new();
    private readonly TrayActions _actions;
    private readonly TrayMenu _menu;

    private Task _serverTask = Task.CompletedTask;
    private bool _disposed;
    private bool _shutdownStarted;

    // MARK: Lifecycle
    // ========================================================================

    public TrayContext()
    {
        _bridgeService = _server.Services.GetRequiredService<BridgeService>();
        _settingsFile = _server.Services.GetRequiredService<SettingsFile>();
        _environment = _server.Services.GetRequiredService<AppEnvironment>();
        _logger = _server.Services.GetRequiredService<ILogger<TrayContext>>();
        _overlay = new(_server.Services);
        _actions = new(_server, _environment, _settingsFile, _bridgeService, _tray, _stop.Token);
        _menu = new(
            () => _ = RunActionAsync(_actions.OpenDesktopSteamInputConfigAsync),
            () => RunAction(_actions.ExportSrmManifest),
            () => RunAction(_actions.OpenSettings),
            () => RunAction(_actions.OpenLogs),
            () => TrayActions.StartupEnabled,
            () => RunAction(TrayActions.ToggleStartup),
            () => _ = RestartAsync(),
            connectionId => _ = RunActionAsync(() => _actions.StopClientAsync(connectionId)),
            () => _ = ShutdownAsync(),
            AppErrorDialog.Show);
        _menu.Menu.Opening += OnMenuOpening;
    }

    public async Task StartAsync()
    {
        LogAppEnvironment();
        await _server.StartAsync(_stop.Token).ConfigureAwait(true);
        _serverTask = _server.WaitForShutdownAsync(CancellationToken.None);

        _tray.Icon = _icon;
        _tray.Text = AppName;
        _menu.Rebuild(_bridgeService.Status, TrayActions.StartupEnabled);
        _tray.ContextMenuStrip = _menu.Menu;
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
            _ = StopServerAsync().ConfigureAwait(true).GetAwaiter().GetResult();
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
        _menu.Menu.Opening -= OnMenuOpening;
        _menu.Menu.Dispose();
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
        if (await StopServerAsync().ConfigureAwait(true))
        {
            WpfApplication.Current.Shutdown();
        }
    }

    private async Task RestartAsync()
    {
        if (_shutdownStarted)
        {
            return;
        }

        _shutdownStarted = true;
        _tray.Visible = false;
        if (!await StopServerAsync().ConfigureAwait(true))
        {
            return;
        }

        ProcessStartInfo start = new()
        {
            FileName = _environment.ExecutablePath,
            WorkingDirectory = _environment.BaseDirectory,
            UseShellExecute = true,
        };
        _ = Process.Start(start) ?? throw new InvalidOperationException("Could not restart Steam Input Bridge.");
        WpfApplication.Current.Shutdown();
    }

    private async Task<bool> StopServerAsync()
    {
        using CancellationTokenSource stopTimeout = new(ShutdownTimeout);

        try
        {
            await _server.StopAsync(stopTimeout.Token).ConfigureAwait(true);
            await _serverTask.ConfigureAwait(true);
            return true;
        }
        catch (OperationCanceledException) when (stopTimeout.IsCancellationRequested)
        {
            AppErrorDialog.Show(new TimeoutException("The server did not stop within 5 seconds."));
            return false;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
        {
            AppErrorDialog.Show(exception);
            return false;
        }
    }

    // MARK: Actions
    // ========================================================================

    private static void RunAction(Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
        {
            AppErrorDialog.Show(exception);
        }
    }

    private static async Task RunActionAsync(Func<Task> action)
    {
        try
        {
            await action().ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
        {
            AppErrorDialog.Show(exception);
        }
    }

    // MARK: Implementation
    // ========================================================================

    private void OnMenuOpening(object? sender, System.ComponentModel.CancelEventArgs args)
    {
        _ = sender;
        _ = args;
        _menu.Rebuild(_bridgeService.Status, TrayActions.StartupEnabled);
    }

    private void LogAppEnvironment()
    {
        LogEnvironment(
            _logger,
            _environment.Version,
            _environment.ExecutablePath,
            _environment.BaseDirectory,
            _environment.SettingsPath,
            _environment.LogPath,
            null);
    }

    private static Icon LoadApplicationIcon()
    {
        return Environment.ProcessPath is { Length: > 0 } processPath
            ? Icon.ExtractAssociatedIcon(processPath) ?? (Icon)SystemIcons.Application.Clone()
            : (Icon)SystemIcons.Application.Clone();
    }
}
