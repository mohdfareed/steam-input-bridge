using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using SteamInputBridge.Settings;

namespace SteamInputBridge.Profiles;

/// <summary>Owns resolved profiles from settings.</summary>
public sealed class ProfileCatalogService(SettingsService settings) : IHostedService, IDisposable
{
    private readonly Lock _gate = new();
    private Dictionary<string, ResolvedProfile> _profiles = ResolveProfiles(settings.Current);
    private bool _disposed;

    // MARK: Publics
    // ========================================================================

    /// <summary>Raised when resolved profiles change.</summary>
    internal event EventHandler? ProfilesChanged;

    /// <summary>Resolved profiles.</summary>
    internal IReadOnlyList<ResolvedProfile> Profiles
    {
        get
        {
            lock (_gate)
            {
                return [.. _profiles.Values];
            }
        }
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        settings.Changed += OnSettingsChanged;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        settings.Changed -= OnSettingsChanged;
        return Task.CompletedTask;
    }

    /// <summary>Stops watching settings changes.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        settings.Changed -= OnSettingsChanged;
    }

    internal bool TryGetProfile(string profileId, out ResolvedProfile profile)
    {
        lock (_gate)
        {
            return _profiles.TryGetValue(profileId, out profile!);
        }
    }

    internal bool ContainsProfile(string profileId)
    {
        lock (_gate)
        {
            return _profiles.ContainsKey(profileId);
        }
    }

    // MARK: Settings
    // ========================================================================

    private void OnSettingsChanged(object? sender, ApplicationSettingsChangedEventArgs args)
    {
        _ = sender;
        lock (_gate)
        {
            _profiles = ResolveProfiles(args.Settings);
        }

        ProfilesChanged?.Invoke(this, EventArgs.Empty);
    }

    private static Dictionary<string, ResolvedProfile> ResolveProfiles(SteamInputBridgeSettings settings)
    {
        Dictionary<string, ResolvedProfile> profiles = new(StringComparer.OrdinalIgnoreCase);
        foreach ((string profileId, GameProfile profile) in settings.Games)
        {
            profiles[profileId] = new(profileId, profile);
        }

        return profiles;
    }
}
