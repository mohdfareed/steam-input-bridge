using System;
using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SteamInputBridge.App.Host;
using SteamInputBridge.App.Tray.Menu;
using SteamInputBridge.App.Tray.Overlay;
using SteamInputBridge.Hosting;
using SteamInputBridge.Hosting.Server;
using SteamInputBridge.Microphone;
using SteamInputBridge.Outputs.Teensy;
using SteamInputBridge.Profiles;
using SteamInputBridge.Settings;
using SteamInputBridge.Shortcuts;
using FormsApplication = System.Windows.Forms.Application;
using WpfApplication = System.Windows.Application;
using WpfShutdownMode = System.Windows.ShutdownMode;

namespace SteamInputBridge.App.Tray;

// MARK: Mode
// ========================================================================

internal static class TrayMode
{
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

// MARK: Context
// ========================================================================

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
    private static readonly MethodInfo? ShowNotifyIconContextMenu =
        typeof(NotifyIcon).GetMethod("ShowContextMenu", BindingFlags.Instance | BindingFlags.NonPublic);
    private static string AppName => "Steam Input Bridge";

    private readonly IHost _server = AppHost.CreateServer();
    private readonly RuntimeServices _runtime;
    private readonly SettingsFile _settingsFile;
    private readonly AppEnvironment _environment;
    private readonly ILogger<TrayContext> _logger;
    private readonly StatusOverlayController _overlay;
    private readonly TeensyFirmwareUploader _firmwareUploader;
    private readonly NotifyIcon _tray = new();
    private readonly Icon _icon = LoadApplicationIcon();
    private readonly CancellationTokenSource _stop = new();
    private readonly TrayActions _actions;
    private readonly TrayMenu _menu;

    private bool _trayShortcutPressed;
    private bool _disposed;
    private bool _shutdownStarted;

    // MARK: Lifecycle
    // ========================================================================

    public TrayContext()
    {
        _runtime = RuntimeServices.Get(_server.Services);
        _settingsFile = _server.Services.GetRequiredService<SettingsFile>();
        _environment = _server.Services.GetRequiredService<AppEnvironment>();
        _logger = _server.Services.GetRequiredService<ILogger<TrayContext>>();
        _overlay = new(_server.Services);
        _firmwareUploader = new(_environment, _settingsFile, _runtime.Settings, _runtime.Teensy, _stop.Token);
        _actions = new(_server, _environment, _settingsFile, _runtime.Bridge, _firmwareUploader, _tray, _stop.Token);
        _menu = new(_actions, () => _ = RestartAsync(), () => _ = ShutdownAsync(), AppErrorDialog.Show);
        SubscribeEvents();
    }

    public async Task StartAsync()
    {
        LogAppEnvironment();
        await _server.StartAsync(_stop.Token).ConfigureAwait(true);

        _tray.Icon = _icon;
        _tray.Text = AppName;

        RebuildMenu();

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
        UnsubscribeEvents();
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

    // MARK: Event Handlers
    // ========================================================================

    private void SubscribeEvents()
    {
        _menu.Menu.Opening += OnMenuOpening;
        _runtime.Bridge.StatusChanged += OnServerStatusChanged;
        _runtime.Profiles.ProfilesChanged += OnProfilesChanged;
        _runtime.Shortcuts.StatusChanged += OnShortcutStatusChanged;
        _runtime.Microphone.StatusChanged += OnDiagnosticsChanged;
        _runtime.ActionColor.ColorChanged += OnDiagnosticsChanged;
        _runtime.Settings.Changed += OnSettingsChanged;
    }

    private void UnsubscribeEvents()
    {
        _runtime.Settings.Changed -= OnSettingsChanged;
        _runtime.ActionColor.ColorChanged -= OnDiagnosticsChanged;
        _runtime.Microphone.StatusChanged -= OnDiagnosticsChanged;
        _runtime.Shortcuts.StatusChanged -= OnShortcutStatusChanged;
        _runtime.Profiles.ProfilesChanged -= OnProfilesChanged;
        _runtime.Bridge.StatusChanged -= OnServerStatusChanged;
        _menu.Menu.Opening -= OnMenuOpening;
    }

    private void OnMenuOpening(object? sender, System.ComponentModel.CancelEventArgs args)
    {
        _ = sender;
        _ = args;
        RebuildMenu();
    }

    private void OnServerStatusChanged(object? sender, EventArgs args)
    {
        _ = sender;
        _ = args;
        QueueMenuStateUpdate();
    }

    private void OnSettingsChanged(object? sender, ApplicationSettingsChangedEventArgs args)
    {
        _ = sender;
        _ = args;
        QueueMenuStateUpdate();
    }

    private void OnShortcutStatusChanged(object? sender, EventArgs args)
    {
        _ = sender;
        _ = args;
        ShowTrayMenuFromShortcut();
        QueueMenuStateUpdate();
    }

    private void OnProfilesChanged(object? sender, EventArgs args)
    {
        _ = sender;
        _ = args;
        QueueMenuStateUpdate();
    }

    private void OnDiagnosticsChanged(object? sender, EventArgs args)
    {
        _ = sender;
        _ = args;
        QueueMenuStateUpdate();
    }

    // MARK: Implementation
    // ========================================================================

    private void QueueMenuStateUpdate()
    {
        if (_disposed || _shutdownStarted)
        {
            return;
        }

        void UpdateMenuState()
        {
            if (_disposed || _shutdownStarted)
            {
                return;
            }

            _menu.SetState(CurrentMenuState());
        }

        _ = WpfApplication.Current.Dispatcher.BeginInvoke(new Action(UpdateMenuState));
    }

    private void RebuildMenu()
    {
        if (_disposed || _shutdownStarted)
        {
            return;
        }

        _menu.SetState(CurrentMenuState());
        _menu.Rebuild();
    }

    private void ShowTrayMenuFromShortcut()
    {
        bool pressed = false;
        foreach (BridgeShortcutStatus shortcut in _runtime.Shortcuts.Status)
        {
            if (shortcut.Pressed &&
                string.Equals(shortcut.Target, nameof(ShortcutTarget.Tray), StringComparison.OrdinalIgnoreCase))
            {
                pressed = true;
                break;
            }
        }

        bool toggle = pressed && !_trayShortcutPressed;
        _trayShortcutPressed = pressed;
        if (!toggle)
        {
            return;
        }

        _ = WpfApplication.Current.Dispatcher.BeginInvoke(new Action(ToggleTrayMenuFromShortcut));
    }

    private void ToggleTrayMenuFromShortcut()
    {
        if (_disposed || _shutdownStarted)
        {
            return;
        }

        if (_menu.Menu.Visible)
        {
            _menu.Menu.Close(ToolStripDropDownCloseReason.CloseCalled);
            return;
        }

        RebuildMenu();
        _ = ShowNotifyIconContextMenu?.Invoke(_tray, null);
    }

    private TrayMenuState CurrentMenuState()
    {
        return new(
            _runtime.Profiles.Profiles,
            _runtime.Bridge.Status,
            _runtime.Microphone.GetStatus(),
            _runtime.ActionColor.Color,
            TrayActions.StartupEnabled);
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

    private sealed record RuntimeServices(
        BridgeService Bridge,
        SettingsService Settings,
        TeensyMouseOutputService Teensy,
        ActiveProfileService Profiles,
        ShortcutService Shortcuts,
        MicrophoneService Microphone,
        ActionColorService ActionColor)
    {
        public static RuntimeServices Get(IServiceProvider services)
        {
            return new(
                services.GetRequiredService<BridgeService>(),
                services.GetRequiredService<SettingsService>(),
                services.GetRequiredService<TeensyMouseOutputService>(),
                services.GetRequiredService<ActiveProfileService>(),
                services.GetRequiredService<ShortcutService>(),
                services.GetRequiredService<MicrophoneService>(),
                services.GetRequiredService<ActionColorService>());
        }
    }
}
