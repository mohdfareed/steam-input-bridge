using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SteamInputBridge.Settings;

namespace SteamInputBridge.Profiles;

/// <summary>Owns resolved profiles, connected profile clients, and active profile state.</summary>
public sealed partial class ProfilesService(SettingsService settings, ILogger<ProfilesService> logger) : IHostedService, IDisposable
{
    private readonly Lock _gate = new();
    private readonly CancellationTokenSource _stop = new();

    private Dictionary<string, ResolvedProfile> _profiles = ResolveProfiles(settings.Current);
    private Dictionary<Guid, ConnectedProfileClient> _clients = [];
    private ProfileStatus? _activeProfile;
    private bool _disposed;

    // MARK: Publics
    // ========================================================================

    /// <summary>Raised when resolved profile availability changes.</summary>
    public event EventHandler<ProfilesChangedEventArgs>? ProfilesChanged;

    /// <summary>Raised when the active profile changes.</summary>
    public event EventHandler<ActiveProfileChangedEventArgs>? ActiveProfileChanged;

    /// <summary>All resolved profile statuses.</summary>
    public IReadOnlyList<ProfileStatus> Profiles => SnapshotProfiles();

    /// <summary>Profiles with connected clients.</summary>
    public IReadOnlyList<ProfileStatus> MonitoredProfiles => SnapshotProfiles(static profile => profile.ClientProcessId.HasValue);

    /// <summary>Current active profile, or null when no monitored profile is active.</summary>
    public ProfileStatus? ActiveProfile
    {
        get
        {
            lock (_gate)
            {
                return _activeProfile;
            }
        }
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        settings.Changed += OnSettingsChanged;
        _monitor = Task.Run(() => MonitorForegroundAsync(_stop.Token), cancellationToken);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        settings.Changed -= OnSettingsChanged;
        await _stop.CancelAsync().ConfigureAwait(false);

        if (_monitor is not null)
        {
            try
            {
                await _monitor.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested)
            {
            }
        }

        await ApplySteamConfigAsync(null, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Stops foreground monitoring.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        settings.Changed -= OnSettingsChanged;
        _stop.Cancel();
        _stop.Dispose();
    }

    // MARK: Clients
    // ========================================================================

    internal void ConnectClient(Guid connectionId, int processId, string profileId, uint? steamAppId)
    {
        lock (_gate)
        {
            _clients[connectionId] = new(connectionId, processId, profileId, steamAppId);
        }

        ProfilesChanged?.Invoke(this, new(Profiles));
    }

    internal void DisconnectClient(Guid connectionId)
    {
        bool changed;
        lock (_gate)
        {
            changed = _clients.Remove(connectionId);
        }

        if (changed)
        {
            ProfilesChanged?.Invoke(this, new(Profiles));
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
            _clients = ConnectedClientsForKnownProfiles(_clients, _profiles);
        }

        ProfilesChanged?.Invoke(this, new(Profiles));
    }

    // MARK: Snapshots
    // ========================================================================

    private List<ProfileStatus> SnapshotProfiles(Func<ProfileStatus, bool>? filter = null)
    {
        lock (_gate)
        {
            List<ProfileStatus> profiles = new(_profiles.Count);
            foreach (ResolvedProfile profile in _profiles.Values)
            {
                ProfileStatus status = ToStatus(profile, ActiveProfileIdLocked());
                if (filter is null || filter(status))
                {
                    profiles.Add(status);
                }
            }

            return profiles;
        }
    }

    private ProfileStatus ToStatus(ResolvedProfile profile, string? activeProfileId)
    {
        ConnectedProfileClient? client = ConnectedClient(profile.Id);
        return new(
            profile.Id,
            profile.Title,
            profile.SteamAppId,
            client?.SteamAppId ?? profile.SteamAppId,
            profile.MouseOutput,
            profile.ControllerOutput,
            profile.ReceiverProcesses,
            Active: string.Equals(activeProfileId, profile.Id, StringComparison.OrdinalIgnoreCase),
            ClientProcessId: client?.ProcessId);
    }
}
