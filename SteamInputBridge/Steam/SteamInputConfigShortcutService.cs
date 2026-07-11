using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SteamInputBridge.Profiles;
using SteamInputBridge.Shortcuts;

namespace SteamInputBridge.Steam;

/// <summary>Opens Steam Input controller configuration from shortcut events.</summary>
public sealed class SteamInputConfigShortcutService : IHostedService, IDisposable
{
    private readonly IShortcutSource _shortcuts;
    private readonly ActiveProfileService _profiles;
    private readonly SteamInputClient _steam;
    private readonly ILogger<SteamInputConfigShortcutService> _logger;
    private bool _disposed;

    /// <summary>Creates the Steam Input config shortcut service.</summary>
    public SteamInputConfigShortcutService(
        ShortcutService shortcuts,
        ActiveProfileService profiles,
        ILogger<SteamInputConfigShortcutService> logger)
        : this(
            (IShortcutSource)(shortcuts ?? throw new ArgumentNullException(nameof(shortcuts))),
            profiles,
            logger)
    {
    }

    internal SteamInputConfigShortcutService(
        IShortcutSource shortcuts,
        ActiveProfileService profiles,
        ILogger<SteamInputConfigShortcutService> logger,
        SteamInputClient? steam = null)
    {
        ArgumentNullException.ThrowIfNull(shortcuts);
        ArgumentNullException.ThrowIfNull(profiles);
        ArgumentNullException.ThrowIfNull(logger);

        _shortcuts = shortcuts;
        _profiles = profiles;
        _logger = logger;
        _steam = steam ?? new();
    }

    // MARK: Lifecycle
    // ============================================================================

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        _shortcuts.Shortcut += OnShortcut;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        _shortcuts.Shortcut -= OnShortcut;
        return Task.CompletedTask;
    }

    /// <summary>Stops listening for Steam shortcut events.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _shortcuts.Shortcut -= OnShortcut;
    }

    // MARK: Events
    // ============================================================================

    private void OnShortcut(object? sender, ShortcutEventArgs args)
    {
        _ = sender;
        if (args.Target.Target != ShortcutTarget.Steam || args.Phase != ShortcutPhase.Pressed)
        {
            return;
        }

        uint appId = _profiles.ActiveProfile?.EffectiveSteamAppId ?? SteamInputClient.DesktopConfigAppId;
        _ = OpenSteamConfigAsync(appId, CancellationToken.None);
    }

    private async Task OpenSteamConfigAsync(uint appId, CancellationToken cancellationToken)
    {
        try
        {
            await _steam.OpenSteamConfigAsync(appId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
        {
            LogSteamConfigOpenFailed(_logger, appId, exception.Message, null);
        }
    }

    // MARK: Logging
    // ============================================================================

    private static readonly Action<ILogger, uint, string, Exception?> LogSteamConfigOpenFailed =
        LoggerMessage.Define<uint, string>(
            LogLevel.Warning,
            new EventId(1, nameof(LogSteamConfigOpenFailed)),
            "Steam Input config open failed for app id {SteamAppId}: {Message}");
}
