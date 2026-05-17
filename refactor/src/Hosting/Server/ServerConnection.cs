using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using StreamJsonRpc;
using VirtualMouse.Runtime;

namespace VirtualMouse.Hosting;

internal sealed record ConnectedClient(Guid Id, int ProcessId, DateTimeOffset ConnectedAt);

internal sealed class ConnectedClients
{
    private readonly ConcurrentDictionary<Guid, ConnectedClient> _clients = [];

    internal IReadOnlyCollection<ConnectedClient> Snapshot => [.. _clients.Values];

    internal int Count => _clients.Count;

    internal ConnectedClient Add(int processId)
    {
        ConnectedClient client = new(Guid.NewGuid(), processId, DateTimeOffset.UtcNow);
        _clients[client.Id] = client;
        return client;
    }

    internal void Remove(Guid clientId)
    {
        _ = _clients.TryRemove(clientId, out _);
    }

    internal ConnectedClient Get(Guid clientId)
    {
        return _clients.TryGetValue(clientId, out ConnectedClient? client)
            ? client
            : throw new InvalidOperationException($"Client {clientId} is not connected.");
    }
}

internal sealed class ServerConnection(
    NamedPipeServerStream pipe,
    VirtualMouseServer server) : IAsyncDisposable
{
    internal async Task RunAsync(CancellationToken cancellationToken)
    {
        await using (pipe.ConfigureAwait(false))
        {
            using CancellationTokenRegistration registration = cancellationToken.Register(static target =>
            {
                ((Stream)target!).Dispose();
            }, pipe);

            ServerConnectionTarget target = new(server);
            using JsonRpc rpc = JsonRpc.Attach(pipe, target);

            try
            {
                await rpc.Completion.ConfigureAwait(false);
            }
            catch (Exception exception) when (IsDisconnect(exception))
            {
                server.ConnectionClosed(exception);
            }
            finally
            {
                target.Dispose();
            }
        }
    }

    internal async ValueTask DisposeAsync()
    {
        await pipe.DisposeAsync().ConfigureAwait(false);
    }

    ValueTask IAsyncDisposable.DisposeAsync()
    {
        return DisposeAsync();
    }

    private static bool IsDisconnect(Exception exception)
    {
        return exception is IOException or EndOfStreamException or ObjectDisposedException or OperationCanceledException;
    }
}

internal sealed class ServerConnectionTarget(VirtualMouseServer server) : IVirtualMouseServerApi, IDisposable
{
    private Guid? _clientId;

    public Task<Guid> ConnectAsync(int processId)
    {
        if (_clientId is not null)
        {
            throw new InvalidOperationException("Client is already connected.");
        }

        _clientId = server.ConnectClient(processId);
        return Task.FromResult(_clientId.Value);
    }

    public Task AckAsync()
    {
        return Task.CompletedTask;
    }

    public Task<ServerStatus> GetStatusAsync()
    {
        return server.GetStatusAsync();
    }

    public Task<ClientRunLaunch> StartRunAsync(string profileId)
    {
        return server.StartRunAsync(GetClientId(), profileId);
    }

    public Task UpdateRunProcessesAsync(IReadOnlyList<ObservedGameProcess> processes)
    {
        return server.UpdateRunProcessesAsync(GetClientId(), processes);
    }

    public Task<IReadOnlyList<ObservedGameProcess>> GetOwnedReceiverProcessesAsync()
    {
        return server.GetOwnedReceiverProcessesAsync(GetClientId());
    }

    public Task EndRunAsync()
    {
        return server.EndRunAsync(GetClientId());
    }

    public void Dispose()
    {
        if (_clientId is Guid clientId)
        {
            server.DisconnectClient(clientId);
            _clientId = null;
        }
    }

    private Guid GetClientId()
    {
        return _clientId ?? throw new InvalidOperationException("Client is not connected.");
    }
}
