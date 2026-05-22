using System;
using System.Collections.Generic;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace SteamInputBridge.Settings.Profiles;

// MARK: Dependency Injection
// ============================================================================

/// <summary>Dependency injection registration for profile settings.</summary>
public static class ProfileServices
{
    /// <summary>Adds reload-able profile settings services.</summary>
    public static IServiceCollection AddProfiles(this IServiceCollection services)
    {
        _ = services.AddSingleton<ProfilesService>();
        return services;
    }
}

/// <summary>Read-only profile lookup backed by reload-able game profile settings.</summary>
public sealed class ProfilesService : IDisposable
{
    private static readonly Action<ILogger, int, Exception?> LogProfileSettingsLoaded =
        LoggerMessage.Define<int>(
            LogLevel.Information,
            new EventId(1, nameof(LogProfileSettingsLoaded)),
            "Profile settings loaded (profiles={ProfileCount})");

    private static readonly Action<ILogger, int, Exception?> LogProfileSettingsReloaded =
        LoggerMessage.Define<int>(
            LogLevel.Information,
            new EventId(2, nameof(LogProfileSettingsReloaded)),
            "Profile settings reloaded (profiles={ProfileCount})");

    private readonly ILogger<ProfilesService> _logger;
    private readonly ApplicationSettingsService _settings;
    private readonly Lock _gate = new();
    private ProfileSnapshot _snapshot;

    // MARK: Construction
    // ========================================================================

    /// <summary>Creates a profile service from reload-able game profile settings.</summary>
    public ProfilesService(
        ApplicationSettingsService settings,
        ILogger<ProfilesService> logger)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(logger);

        _settings = settings;
        _logger = logger;
        _snapshot = ProfileSnapshot.From(settings.Current.Games);
        LogProfileSettingsLoaded(_logger, _snapshot.Profiles.Count, null);
        _settings.Changed += OnSettingsChanged;
    }

    // MARK: Publics
    // ========================================================================

    /// <summary>Lists configured profile ids.</summary>
    public IReadOnlyList<string> ListProfileIds()
    {
        lock (_gate)
        {
            return _snapshot.ProfileIds;
        }
    }

    /// <summary>Gets a profile by id, or null when it is not configured.</summary>
    public GameProfile? GetProfile(string profileId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);

        lock (_gate)
        {
            return _snapshot.Profiles.TryGetValue(profileId, out GameProfile? profile)
                ? profile
                : null;
        }
    }

    /// <summary>Stops listening for settings reloads.</summary>
    public void Dispose()
    {
        _settings.Changed -= OnSettingsChanged;
    }

    // MARK: Privates
    // ========================================================================

    private void OnSettingsChanged(object? sender, ApplicationSettingsChangedEventArgs args)
    {
        ProfileSnapshot snapshot = ProfileSnapshot.From(args.Settings.Games);
        lock (_gate)
        {
            _snapshot = snapshot;
        }

        LogProfileSettingsReloaded(_logger, snapshot.Profiles.Count, null);
    }
}
