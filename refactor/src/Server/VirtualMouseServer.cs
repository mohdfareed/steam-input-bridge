using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VirtualMouse.Protocol;

namespace VirtualMouse.Server;

// The app-facing server owns connected clients and accepts client pipes.
public sealed class VirtualMouseServer(
    IOptions<ConnectionOptions> options,
    ILogger<VirtualMouseServer> logger)
{
    private readonly ConnectedClients _clients = new();
    private readonly ConcurrentDictionary<ServerConnection, byte> _connections = [];

    // MARK: API
    // ============================================================================

    public IReadOnlyCollection<ConnectedClient> Clients => _clients.Snapshot;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        string pipeName = options.Value.PipeName;
        logger.LogInformation("Listening on server pipe {PipeName}", pipeName);

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                NamedPipeServerStream? pipe = new(
                    pipeName,
                    PipeDirection.InOut,
                    maxNumberOfServerInstances: 254,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                try
                {
                    await pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                    NamedPipeServerStream connectedPipe = pipe;
                    ServerConnection connection = new(connectedPipe, _clients, logger);
                    _connections[connection] = 0;
                    _ = Task.Run(() => RunConnectionAsync(connection, cancellationToken), CancellationToken.None);
                    pipe = null;
                }
                finally
                {
                    if (pipe is not null)
                    {
                        await pipe.DisposeAsync().ConfigureAwait(false);
                    }
                }
            }
        }
        finally
        {
            await DisposeConnectionsAsync().ConfigureAwait(false);
        }
    }

    private async Task RunConnectionAsync(ServerConnection connection, CancellationToken cancellationToken)
    {
        try
        {
            await connection.RunAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _ = _connections.TryRemove(connection, out _);
        }
    }

    private async Task DisposeConnectionsAsync()
    {
        foreach (ServerConnection connection in _connections.Keys)
        {
            await connection.DisposeAsync().ConfigureAwait(false);
        }
    }
}
