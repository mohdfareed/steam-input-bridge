using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SteamInputBridge.App.Host;
using SteamInputBridge.App.Tray.Menu;
using SteamInputBridge.App.Tray.Overlay;
using SteamInputBridge.Hosting.Server;
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

    private string? _lastActiveProfileId;
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
        _menu = new(_actions, () => _ = RestartAsync(), () => _ = ShutdownAsync(), AppErrorDialog.Show);
        _menu.Menu.Opening += OnMenuOpening;
        _bridgeService.StatusChanged += OnServerStatusChanged;
        _server.Services.GetRequiredService<ProfilesService>().ProfilesChanged += OnProfilesChanged;
        _server.Services.GetRequiredService<ProfilesService>().ActiveProfileChanged += OnActiveProfileChanged;
        _server.Services.GetRequiredService<ShortcutService>().StatusChanged += OnShortcutStatusChanged;
        _server.Services.GetRequiredService<SettingsService>().Changed += OnSettingsChanged;
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
        _server.Services.GetRequiredService<SettingsService>().Changed -= OnSettingsChanged;
        _server.Services.GetRequiredService<ShortcutService>().StatusChanged -= OnShortcutStatusChanged;
        _server.Services.GetRequiredService<ProfilesService>().ActiveProfileChanged -= OnActiveProfileChanged;
        _server.Services.GetRequiredService<ProfilesService>().ProfilesChanged -= OnProfilesChanged;
        _bridgeService.StatusChanged -= OnServerStatusChanged;
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
        QueueMenuStateUpdate();
    }

    private void OnProfilesChanged(object? sender, EventArgs args)
    {
        _ = sender;
        _ = args;
        QueueMenuStateUpdate();
    }

    private void OnActiveProfileChanged(object? sender, ActiveProfileChangedEventArgs args)
    {
        _ = sender;
        if (args.ActiveProfile is null)
        {
            return;
        }

        _lastActiveProfileId = args.ActiveProfile.Id;
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

    private TrayMenuState CurrentMenuState()
    {
        IReadOnlyList<ProfileStatus> profiles = _server.Services.GetRequiredService<ProfilesService>().Profiles;
        if (!HasConnectedProfile(profiles, _lastActiveProfileId))
        {
            _lastActiveProfileId = null;
        }

        return new(profiles, _lastActiveProfileId, _bridgeService.Status, TrayActions.StartupEnabled);
    }

    private static bool HasConnectedProfile(IReadOnlyList<ProfileStatus> profiles, string? profileId)
    {
        if (string.IsNullOrWhiteSpace(profileId))
        {
            return false;
        }

        foreach (ProfileStatus profile in profiles)
        {
            if (profile.ClientProcessId.HasValue && string.Equals(profile.Id, profileId, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
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
