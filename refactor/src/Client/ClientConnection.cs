using System;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using VirtualMouse.Protocol;

namespace VirtualMouse.Client;

// MARK: Connection
// ============================================================================

internal sealed class ClientConnection(
    NamedPipeClientStream pipe,
    RequestResponsePipe messages,
    Guid clientId) : IAsyncDisposable
{
    public Guid ClientId { get; } = clientId;

    public static async Task<ClientConnection> ConnectAsync(
        string pipeName,
        CancellationToken cancellationToken)
    {
        NamedPipeClientStream pipe = new(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        try
        {
            await pipe.ConnectAsync(cancellationToken).ConfigureAwait(false);
            RequestResponsePipe messages = new(pipe);
            ConnectResponse response = await messages
                .ConnectAsync(Environment.ProcessId, cancellationToken)
                .ConfigureAwait(false);

            return new ClientConnection(pipe, messages, response.ClientId);
        }
        catch
        {
            await pipe.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public Task<Ack> AckAsync(CancellationToken cancellationToken)
    {
        return messages.AckAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await messages.DisposeAsync().ConfigureAwait(false);
        await pipe.DisposeAsync().ConfigureAwait(false);
    }
}
