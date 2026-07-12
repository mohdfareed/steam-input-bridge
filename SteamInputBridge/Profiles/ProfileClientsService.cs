using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SteamInputBridge.Hosting;
using SteamInputBridge.Settings;

namespace SteamInputBridge.Profiles;

/// <summary>Owns connected profile clients.</summary>
public sealed class ProfileClientsService : IDisposable
{
    private readonly ProfileCatalogService _profiles;
    private readonly ILogger<ProfileClientsService> _logger;
    private readonly Func<int, WindowActivationResult> _activateReceiver;
    private readonly Lock _gate = new();
    private readonly Dictionary<Guid, ConnectedProfileClient> _clients = [];
    private readonly Dictionary<Guid, ProfileReceiverSession> _sessions = [];
    private bool _disposed;

    /// <summary>Creates profile client session ownership.</summary>
    public ProfileClientsService(ProfileCatalogService profiles, ILogger<ProfileClientsService> logger)
        : this(profiles, logger, ReceiverWindowActivator.TryActivate)
    {
    }

    internal ProfileClientsService(
        ProfileCatalogService profiles,
        ILogger<ProfileClientsService> logger,
        Func<int, WindowActivationResult> activateReceiver)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(activateReceiver);

        _profiles = profiles;
        _logger = logger;
        _activateReceiver = activateReceiver;
    }

    // MARK: Publics
    // ========================================================================

    /// <summary>Raised when connected clients change.</summary>
    public event EventHandler? ClientsChanged;

    /// <summary>Connected profile clients.</summary>
    internal IReadOnlyList<ProfileClientStatus> Clients
    {
        get
        {
            lock (_gate)
            {
                List<ProfileClientStatus> clients = new(_clients.Count);
                foreach (ConnectedProfileClient client in _clients.Values)
                {
                    clients.Add(ToStatus(client, _sessions.GetValueOrDefault(client.ConnectionId)));
                }

                return clients;
            }
        }
    }

    internal IReadOnlyList<BridgeClientConnection> Connections
    {
        get
        {
            lock (_gate)
            {
                List<BridgeClientConnection> clients = new(_clients.Count);
                foreach (ConnectedProfileClient client in _clients.Values)
                {
                    clients.Add(new(client.ConnectionId, client.ProfileId, client.Control));
                }

                return clients;
            }
        }
    }

    internal async Task<ProfileClientStatus> ConnectClientAsync(
        Guid connectionId,
        int processId,
        string profileId,
        uint? steamAppId,
        IBridgeClientApi control)
    {
        ConnectedProfileClient client;
        ProfileReceiverSession session;
        lock (_gate)
        {
            if (!_profiles.TryGetProfile(profileId, out GameProfile profile))
            {
                throw new InvalidOperationException($"Profile '{profileId}' is not configured.");
            }

            if (_clients.ContainsKey(connectionId))
            {
                throw new InvalidOperationException($"Control connection '{connectionId}' is already registered.");
            }

            foreach (ConnectedProfileClient connectedClient in _clients.Values)
            {
                if (string.Equals(connectedClient.ProfileId, profileId, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"Profile '{profileId}' already has a connected client.");
                }
            }

            client = new(connectionId, processId, profileId, steamAppId, control);
            _clients[connectionId] = client;
#pragma warning disable CA2000 // ProfileReceiverSession ownership transfers to _sessions.
            session = new(
                profileId,
                profile,
                control,
                _activateReceiver,
                _logger,
                () => ClientsChanged?.Invoke(this, EventArgs.Empty));
#pragma warning restore CA2000
            _sessions[connectionId] = session;
        }

        session.Start();
        ClientsChanged?.Invoke(this, EventArgs.Empty);
        await control.SetActiveAsync(active: false).ConfigureAwait(false);
        return ToStatus(client, session);
    }

    internal ProfileClientStatus? DisconnectClient(Guid connectionId, bool stopTrackedReceivers = true)
    {
        ConnectedProfileClient? client;
        ProfileReceiverSession? session;
        lock (_gate)
        {
            _ = _clients.Remove(connectionId, out client);
#pragma warning disable CA2000 // Removed session is disposed immediately below.
            _ = _sessions.Remove(connectionId, out session);
#pragma warning restore CA2000
        }

        if (session is not null)
        {
            if (stopTrackedReceivers && session.StopReceiversWhenPipeCloses)
            {
                _ = session.StopReceivers();
            }

            session.Dispose();
        }

        if (client is not null)
        {
            ClientsChanged?.Invoke(this, EventArgs.Empty);
        }

        return client is null ? null : ToStatus(client, session);
    }

    internal async Task StopClientAsync(Guid connectionId)
    {
        ProfileReceiverSession session = GetSession(connectionId);
        session.StopReceiversWhenPipeCloses = false;
        await session.StopClientAsync().ConfigureAwait(false);
    }

    internal void StopReceivers(Guid connectionId)
    {
        ProfileReceiverSession session = GetSession(connectionId);
        session.StopReceiversWhenPipeCloses = false;
        _ = session.StopReceivers();
    }

    /// <summary>Releases active sessions without stopping receiver processes again.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        lock (_gate)
        {
            foreach (ProfileReceiverSession session in _sessions.Values)
            {
                session.Dispose();
            }

            _sessions.Clear();
            _clients.Clear();
        }
    }

    // MARK: State
    // ========================================================================

    private ProfileReceiverSession GetSession(Guid connectionId)
    {
        lock (_gate)
        {
            if (_sessions.TryGetValue(connectionId, out ProfileReceiverSession? session))
            {
                return session;
            }
        }

        throw new InvalidOperationException($"Profile session '{connectionId}' is not connected.");
    }

    private static ProfileClientStatus ToStatus(ConnectedProfileClient client, ProfileReceiverSession? session)
    {
        return new(
            client.ConnectionId,
            client.ProcessId,
            client.ProfileId,
            client.SteamAppId,
            session?.ReceiverProcessIds ?? []);
    }

    private sealed record ConnectedProfileClient(
        Guid ConnectionId,
        int ProcessId,
        string ProfileId,
        uint? SteamAppId,
        IBridgeClientApi Control);

    internal sealed record BridgeClientConnection(Guid ConnectionId, string ProfileId, IBridgeClientApi Control);
}
