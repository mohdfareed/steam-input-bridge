using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SteamInputBridge.Hosting;
using SteamInputBridge.Settings;

namespace SteamInputBridge.Profiles;

/// <summary>Owns resolved profiles, connected profile clients, and active profile state.</summary>
public sealed partial class ProfilesService(SettingsService settings, ILogger<ProfilesService> logger) : IHostedService, IDisposable
{
    private readonly Lock _gate = new();
    private readonly CancellationTokenSource _stop = new();

    private Dictionary<string, ResolvedProfile> _profiles = ResolveProfiles(settings.Current);
    private readonly Dictionary<Guid, ConnectedProfileClient> _clients = [];
    private readonly Dictionary<Guid, ProfileSession> _sessions = [];
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

    /// <summary>Connected profile clients.</summary>
    internal IReadOnlyList<ProfileClientStatus> Clients => SnapshotClients();

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
        StopSessions();

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
        StopSessions();
        _stop.Cancel();
        _stop.Dispose();
    }

    // MARK: Clients
    // ========================================================================

    internal ProfileClientStatus ConnectClient(Guid connectionId, int processId, string profileId, uint? steamAppId, IBridgeClientApi control)
    {
        ResolvedProfile profile;
        ConnectedProfileClient client;
        lock (_gate)
        {
            if (!_profiles.TryGetValue(profileId, out profile!))
            {
                throw new InvalidOperationException($"Profile '{profileId}' is not configured.");
            }

            if (_clients.ContainsKey(connectionId))
            {
                throw new InvalidOperationException($"Control connection '{connectionId}' is already registered.");
            }

            if (ConnectedClient(profileId) is not null)
            {
                throw new InvalidOperationException($"Profile '{profileId}' already has a connected client.");
            }

            client = new(connectionId, processId, profileId, steamAppId);
            _clients[connectionId] = client;
            StartSession(connectionId, profile, control);
        }

        ProfilesChanged?.Invoke(this, new(Profiles));
        return ToStatus(client);
    }

    internal ProfileClientStatus? DisconnectClient(Guid connectionId)
    {
        ConnectedProfileClient? client;
        ProfileSession? session;
        bool changed;
        lock (_gate)
        {
            changed = _clients.Remove(connectionId, out client);
            _ = _sessions.Remove(connectionId, out session);
        }

        if (session is not null)
        {
            if (session.ReceiversOnDisconnect == ReceiverDisconnectAction.Stop)
            {
                int stopped = StopSessionReceivers(session);
                LogProfileReceiversStopped(logger, session.Profile.Id, stopped, null);
            }

            session.Cancel();
        }

        if (changed)
        {
            ProfilesChanged?.Invoke(this, new(Profiles));
        }

        return client is null ? null : ToStatus(client);
    }

    internal async Task StopClientAsync(Guid connectionId)
    {
        ProfileSession session;
        lock (_gate)
        {
            if (!_sessions.TryGetValue(connectionId, out ProfileSession? existing))
            {
                throw new InvalidOperationException($"Profile session '{connectionId}' is not connected.");
            }

            session = existing;
            session.ReceiversOnDisconnect = ReceiverDisconnectAction.Keep;
        }

        await session.Control.StopAsync().ConfigureAwait(false);
    }

    internal void StopReceivers(Guid connectionId)
    {
        ProfileSession session;
        lock (_gate)
        {
            if (!_sessions.TryGetValue(connectionId, out ProfileSession? existing))
            {
                throw new InvalidOperationException($"Profile session '{connectionId}' is not connected.");
            }

            session = existing;
            session.ReceiversOnDisconnect = ReceiverDisconnectAction.Keep;
        }

        int stopped = StopSessionReceivers(session);
        LogProfileReceiversStopped(logger, session.Profile.Id, stopped, null);
    }

    // MARK: Settings
    // ========================================================================

    private void OnSettingsChanged(object? sender, ApplicationSettingsChangedEventArgs args)
    {
        _ = sender;
        lock (_gate)
        {
            _profiles = ResolveProfiles(args.Settings);
            RemoveUnknownClients();
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

    private List<ProfileClientStatus> SnapshotClients()
    {
        lock (_gate)
        {
            List<ProfileClientStatus> clients = new(_clients.Count);
            foreach (ConnectedProfileClient client in _clients.Values)
            {
                clients.Add(ToStatus(client));
            }

            return clients;
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
            ClientProcessId: client?.ProcessId,
            ClientConnectionId: client?.ConnectionId);
    }

    private static ProfileClientStatus ToStatus(ConnectedProfileClient client)
    {
        return new(client.ConnectionId, client.ProcessId, client.ProfileId, client.SteamAppId);
    }
}
