using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

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
}

internal sealed class ServerConnection(
    NamedPipeServerStream pipe,
    ConnectedClients clients,
    ILogger logger) : IAsyncDisposable
{
    internal async Task RunAsync(CancellationToken cancellationToken)
    {
        await using (pipe.ConfigureAwait(false))
        await using (RequestResponsePipe messages = new(pipe))
        {
            Guid? clientId = null;
            try
            {
                clientId = await ConnectClientAsync(messages, cancellationToken).ConfigureAwait(false);
                while (await messages.ReadRequestAsync(cancellationToken).ConfigureAwait(false) is { } request)
                {
                    await DispatchAsync(messages, request, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception exception) when (IsDisconnect(exception))
            {
            }
            finally
            {
                if (clientId is Guid id)
                {
                    clients.Remove(id);
                    logger.LogInformation(
                        "Client disconnected: {ClientId} (clients={ClientCount})",
                        id,
                        clients.Count);
                }
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

    private async Task<Guid?> ConnectClientAsync(
        RequestResponsePipe messages,
        CancellationToken cancellationToken)
    {
        RequestMessage? request = await messages.ReadRequestAsync(cancellationToken).ConfigureAwait(false);
        if (request is null)
        {
            return null;
        }

        if (!ServerApi.IsConnect(request))
        {
            await messages
                .SendErrorAsync(request.Id, "First request must be connect.", cancellationToken)
                .ConfigureAwait(false);
            return null;
        }

        ConnectRequest connect = request.ReadPayload<ConnectRequest>();
        ConnectedClient client = clients.Add(connect.ProcessId);
        logger.LogInformation(
            "Client connected: {ClientId} process={ProcessId} (clients={ClientCount})",
            client.Id,
            client.ProcessId,
            clients.Count);

        await messages
            .SendResponseAsync(request.Id, new ConnectResponse(client.Id), cancellationToken)
            .ConfigureAwait(false);

        return client.Id;
    }

    private static Task DispatchAsync(
        RequestResponsePipe messages,
        RequestMessage request,
        CancellationToken cancellationToken)
    {
        return ServerApi.IsAck(request)
            ? messages.SendResponseAsync(request.Id, new Ack(), cancellationToken)
            : messages.SendErrorAsync(request.Id, "Unknown method.", cancellationToken);
    }

    private static bool IsDisconnect(Exception exception)
    {
        return exception is IOException or EndOfStreamException or ObjectDisposedException or OperationCanceledException;
    }
}
