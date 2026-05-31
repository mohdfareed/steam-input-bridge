using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SteamInputBridge.Profiles;

namespace SteamInputBridge.Steam;

/// <summary>Applies Steam Input config for the active profile.</summary>
public sealed class SteamInputConfigService(ActiveProfileService profiles, ILogger<SteamInputConfigService> logger)
    : IHostedService
{
    private readonly SteamInputClient _steam = new();
    private uint? _forcedSteamAppId;

    // MARK: Lifecycle
    // ========================================================================

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        profiles.ActiveProfileChanged += OnActiveProfileChanged;
        return ApplySteamConfigAsync(profiles.ActiveProfile, CancellationToken.None);
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        profiles.ActiveProfileChanged -= OnActiveProfileChanged;
        await ApplySteamConfigAsync(null, cancellationToken).ConfigureAwait(false);
    }

    // MARK: Events
    // ========================================================================

    private void OnActiveProfileChanged(object? sender, ActiveProfileChangedEventArgs args)
    {
        _ = sender;
        _ = ApplySteamConfigAsync(args.ActiveProfile, CancellationToken.None);
    }

    private async Task ApplySteamConfigAsync(ProfileStatus? activeProfile, CancellationToken cancellationToken)
    {
        uint? appId = activeProfile?.EffectiveSteamAppId;
        if (appId == _forcedSteamAppId)
        {
            return;
        }

        try
        {
            await _steam.ForceConfigAsync(appId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
        {
            LogSteamConfigFailed(logger, appId, exception.Message, null);
        }
        finally
        {
            _forcedSteamAppId = appId;
        }
    }

    // MARK: Logging
    // ========================================================================

    private static readonly Action<ILogger, uint?, string, Exception?> LogSteamConfigFailed =
        LoggerMessage.Define<uint?, string>(
            LogLevel.Warning,
            new EventId(1, nameof(LogSteamConfigFailed)),
            "Steam Input config update failed for app id {SteamAppId}: {Message}");
}
