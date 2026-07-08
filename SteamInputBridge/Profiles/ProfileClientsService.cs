using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SteamInputBridge.Hosting;
using SteamInputBridge.Settings;

namespace SteamInputBridge.Profiles;

/// <summary>Owns connected profile clients and their launched receiver processes.</summary>
public sealed class ProfileClientsService(ProfileCatalogService profiles, ILogger<ProfileClientsService> logger)
    : IDisposable
{
    private static readonly TimeSpan ReceiverPollInterval = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan ReceiverCloseTimeout = TimeSpan.FromSeconds(5);

    private readonly Lock _gate = new();
    private readonly Dictionary<Guid, ConnectedProfileClient> _clients = [];
    private readonly Dictionary<Guid, ClientSession> _sessions = [];
    private bool _disposed;

    // MARK: Publics
    // ========================================================================

    /// <summary>Raised when connected clients change.</summary>
    public event EventHandler? ClientsChanged;

    /// <summary>Connected profile clients.</summary>
    internal IReadOnlyList<ProfileClientStatus> Clients => SnapshotClients();

    internal IReadOnlyList<BridgeClientConnection> Connections => SnapshotConnections();

    internal async Task<ProfileClientStatus> ConnectClientAsync(
        Guid connectionId,
        int processId,
        string profileId,
        uint? steamAppId,
        IBridgeClientApi control)
    {
        ConnectedProfileClient client;
        ClientSession session;
        lock (_gate)
        {
            if (!profiles.TryGetProfile(profileId, out ResolvedProfile profile))
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

            client = new(connectionId, processId, profileId, steamAppId, control);
            _clients[connectionId] = client;
            session = StartSession(connectionId, profile, control);
        }

        ClientsChanged?.Invoke(this, EventArgs.Empty);
        await control.SetActiveAsync(active: false).ConfigureAwait(false);
        return ToStatus(client, session);
    }

    internal ProfileClientStatus? DisconnectClient(Guid connectionId)
    {
        ConnectedProfileClient? client;
        ClientSession? session;
        lock (_gate)
        {
            _ = _clients.Remove(connectionId, out client);
            _ = _sessions.Remove(connectionId, out session);
        }

        if (session is not null)
        {
            if (session.StopReceiversOnDisconnect)
            {
                int stopped = StopSessionReceivers(session);
                LogProfileReceiversStopped(logger, session.Profile.Id, stopped, null);
            }

            session.Cancel();
        }

        if (client is not null)
        {
            ClientsChanged?.Invoke(this, EventArgs.Empty);
        }

        return client is null ? null : ToStatus(client, session);
    }

    internal async Task StopClientAsync(Guid connectionId)
    {
        ClientSession session = GetSession(connectionId);
        session.StopReceiversOnDisconnect = false;
        await session.Control.StopAsync().ConfigureAwait(false);
    }

    internal void StopReceivers(Guid connectionId)
    {
        ClientSession session = GetSession(connectionId);
        session.StopReceiversOnDisconnect = false;
        int stopped = StopSessionReceivers(session);
        LogProfileReceiversStopped(logger, session.Profile.Id, stopped, null);
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
            foreach (ClientSession session in _sessions.Values)
            {
                session.Cancel();
            }

            _sessions.Clear();
            _clients.Clear();
        }
    }

    // MARK: Sessions
    // ========================================================================

    private ClientSession StartSession(Guid connectionId, ResolvedProfile profile, IBridgeClientApi control)
    {
        StopSession(connectionId);
        HashSet<int> receiverBaseline = string.IsNullOrWhiteSpace(profile.Definition.Executable)
            ? []
            : FindReceivers(profile.Definition.ReceiverProcesses);

#pragma warning disable CA2000 // CancellationTokenSource ownership transfers to ClientSession.
        CancellationTokenSource stop = new();
        ClientSession session = new(connectionId, profile, control, receiverBaseline, stop);
#pragma warning restore CA2000

        _sessions[connectionId] = session;
        session.Task = Task.Run(() => RunSessionAsync(session, stop.Token), CancellationToken.None);
        return session;
    }

    private void StopSession(Guid connectionId)
    {
        if (_sessions.Remove(connectionId, out ClientSession? session))
        {
            session.Cancel();
        }
    }

    private async Task RunSessionAsync(ClientSession session, CancellationToken cancellationToken)
    {
        try
        {
            LaunchProfile(session.Profile);
            await WatchReceiversAsync(session, cancellationToken).ConfigureAwait(false);
            await session.Control.StopAsync().ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception)
        {
            LogProfileSessionFailed(logger, session.Profile.Id, exception.Message, null);
        }
    }

    private static void LaunchProfile(ResolvedProfile profile)
    {
        GameProfile definition = profile.Definition;
        if (string.IsNullOrWhiteSpace(definition.Executable))
        {
            return;
        }

        ProcessStartInfo start = new()
        {
            FileName = definition.Executable,
            Arguments = definition.Arguments ?? string.Empty,
            WorkingDirectory = definition.WorkingDirectory ?? AppContext.BaseDirectory,
            UseShellExecute = false,
        };

        _ = Process.Start(start) ?? throw new InvalidOperationException($"Could not launch {definition.Executable}.");
    }

    private async Task WatchReceiversAsync(ClientSession session, CancellationToken cancellationToken)
    {
        if (session.Profile.Definition.ReceiverProcesses.Count == 0)
        {
            return;
        }

        bool sawReceiver = false;
        while (!cancellationToken.IsCancellationRequested)
        {
            bool hasReceiver = RefreshSessionReceivers(session, out bool changed);
            sawReceiver |= hasReceiver;
            if (changed)
            {
                ClientsChanged?.Invoke(this, EventArgs.Empty);
            }

            if (sawReceiver && !hasReceiver)
            {
                return;
            }

            await Task.Delay(ReceiverPollInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    // MARK: Receivers
    // ========================================================================

    private static HashSet<int> FindReceivers(IReadOnlyList<string> processNames)
    {
        HashSet<int> processIds = [];
        foreach (string processName in processNames)
        {
            string normalized = Path.GetFileNameWithoutExtension(processName.Trim());
            if (string.IsNullOrWhiteSpace(normalized))
            {
                continue;
            }

            foreach (Process process in Process.GetProcessesByName(normalized))
            {
                try
                {
                    _ = processIds.Add(process.Id);
                }
                catch (InvalidOperationException)
                {
                }
                finally
                {
                    process.Dispose();
                }
            }
        }

        return processIds;
    }

    private static bool RefreshSessionReceivers(ClientSession session, out bool changed)
    {
        HashSet<int> receivers = FindReceivers(session.Profile.Definition.ReceiverProcesses);
        receivers.ExceptWith(session.ReceiverBaseline);

        lock (session.Gate)
        {
            HashSet<int> previous = [.. session.Receivers];
            _ = session.Receivers.RemoveWhere(processId => !receivers.Contains(processId));
            session.Receivers.UnionWith(receivers);
            changed = !previous.SetEquals(session.Receivers);

            return session.Receivers.Count != 0;
        }
    }

    private static int StopSessionReceivers(ClientSession session)
    {
        int stopped = 0;
        int[] receivers;
        lock (session.Gate)
        {
            receivers = [.. session.Receivers];
        }

        foreach (int processId in receivers)
        {
            stopped += StopProcess(processId);
        }

        return stopped;
    }

    private static int StopProcess(int processId)
    {
        try
        {
            using Process process = Process.GetProcessById(processId);
            if (process.CloseMainWindow() && process.WaitForExit(ReceiverCloseTimeout))
            {
                return 1;
            }

            process.Kill(entireProcessTree: true);
            process.WaitForExit();
            return 1;
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException or Win32Exception or NotSupportedException)
        {
            return 0;
        }
    }

    // MARK: Snapshots
    // ========================================================================

    private List<ProfileClientStatus> SnapshotClients()
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

    private List<BridgeClientConnection> SnapshotConnections()
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

    private ConnectedProfileClient? ConnectedClient(string profileId)
    {
        foreach (ConnectedProfileClient client in _clients.Values)
        {
            if (string.Equals(client.ProfileId, profileId, StringComparison.OrdinalIgnoreCase))
            {
                return client;
            }
        }

        return null;
    }

    private ClientSession GetSession(Guid connectionId)
    {
        lock (_gate)
        {
            if (_sessions.TryGetValue(connectionId, out ClientSession? session))
            {
                return session;
            }
        }

        throw new InvalidOperationException($"Profile session '{connectionId}' is not connected.");
    }

    private static ProfileClientStatus ToStatus(ConnectedProfileClient client, ClientSession? session)
    {
        int[] receiverProcessIds = [];
        if (session is not null)
        {
            lock (session.Gate)
            {
                receiverProcessIds = [.. session.Receivers];
            }
        }

        return new(client.ConnectionId, client.ProcessId, client.ProfileId, client.SteamAppId, receiverProcessIds);
    }

    private sealed record ConnectedProfileClient(
        Guid ConnectionId,
        int ProcessId,
        string ProfileId,
        uint? SteamAppId,
        IBridgeClientApi Control);

    internal sealed record BridgeClientConnection(Guid ConnectionId, string ProfileId, IBridgeClientApi Control);

    private sealed class ClientSession(
        Guid connectionId,
        ResolvedProfile profile,
        IBridgeClientApi control,
        HashSet<int> receiverBaseline,
        CancellationTokenSource stop)
    {
        public Lock Gate { get; } = new();

        public Guid ConnectionId { get; } = connectionId;

        public ResolvedProfile Profile { get; } = profile;

        public IBridgeClientApi Control { get; } = control;

        public HashSet<int> ReceiverBaseline { get; } = receiverBaseline;

        public HashSet<int> Receivers { get; } = [];

        public Task? Task { get; set; }

        public bool StopReceiversOnDisconnect { get; set; } = true;

        public void Cancel()
        {
            stop.Cancel();
            stop.Dispose();
        }
    }

    // MARK: Logging
    // ========================================================================

    private static readonly Action<ILogger, string, string, Exception?> LogProfileSessionFailed =
        LoggerMessage.Define<string, string>(
            LogLevel.Warning,
            new EventId(1, nameof(LogProfileSessionFailed)),
            "Profile session failed for profile {ProfileId}: {Message}");

    private static readonly Action<ILogger, string, int, Exception?> LogProfileReceiversStopped =
        LoggerMessage.Define<string, int>(
            LogLevel.Information,
            new EventId(2, nameof(LogProfileReceiversStopped)),
            "Stopped {ReceiverCount} receiver process(es) for profile {ProfileId}.");
}
