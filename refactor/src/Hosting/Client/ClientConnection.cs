using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StreamJsonRpc;
using VirtualMouse.Settings;

namespace VirtualMouse.Hosting;

internal sealed class ClientConnection(
    IOptions<HostingSettings> options,
    ILogger<ClientConnection> logger) : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private NamedPipeClientStream? _pipe;
    private IVirtualMouseServerApi? _server;

    internal event EventHandler<ClientConnectionChangedEventArgs>? Changed;

    internal Guid? ClientId { get; private set; }

    internal ClientConnectionState State { get; private set; } = ClientConnectionState.Disconnected;

    internal IVirtualMouseServerApi Server => _server ?? throw new InvalidOperationException("Client is not connected.");

    internal async Task ConnectAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_server is not null)
            {
                return;
            }

            await OpenAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _ = _gate.Release();
        }
    }

    internal async Task WaitAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await Task
                .Delay(TimeSpan.FromMilliseconds(options.Value.KeepAliveMilliseconds), cancellationToken)
                .ConfigureAwait(false);

            try
            {
                await Server.AckAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (IsConnectionFailure(exception))
            {
                logger.LogWarning("Server connection lost: {Message}", exception.Message);
                await ClearAsync().ConfigureAwait(false);
                await ReconnectAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }

    internal async ValueTask DisposeAsync()
    {
        await ClearAsync().ConfigureAwait(false);
        _gate.Dispose();
    }

    ValueTask IAsyncDisposable.DisposeAsync()
    {
        return DisposeAsync();
    }

    private async Task OpenAsync(CancellationToken cancellationToken)
    {
        string pipeName = options.Value.PipeName;
        SetState(ClientConnectionState.Connecting, null);
        logger.LogInformation("Connecting to server pipe {PipeName}", pipeName);

        NamedPipeClientStream pipe = new(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        try
        {
            await pipe.ConnectAsync(cancellationToken).ConfigureAwait(false);
            IVirtualMouseServerApi server = JsonRpc.Attach<IVirtualMouseServerApi>(pipe);
            Guid clientId = await server
                .ConnectAsync(Environment.ProcessId)
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);

            _pipe = pipe;
            _server = server;
            ClientId = clientId;
            SetState(ClientConnectionState.Connected, ClientId);
            logger.LogInformation("Connected to server as {ClientId}", ClientId);
        }
        catch
        {
            await pipe.DisposeAsync().ConfigureAwait(false);
            SetState(ClientConnectionState.Disconnected, null);
            throw;
        }
    }

    private async Task ReconnectAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await ConnectAsync(cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (Exception exception) when (IsConnectionFailure(exception))
            {
                logger.LogWarning("Reconnect failed: {Message}", exception.Message);
                await ClearAsync().ConfigureAwait(false);
                await Task
                    .Delay(TimeSpan.FromMilliseconds(options.Value.ReconnectDelayMilliseconds), cancellationToken)
                    .ConfigureAwait(false);
            }
        }
    }

    private async Task ClearAsync()
    {
        IVirtualMouseServerApi? server = Interlocked.Exchange(ref _server, null);
        (server as IDisposable)?.Dispose();

        NamedPipeClientStream? pipe = Interlocked.Exchange(ref _pipe, null);
        if (pipe is not null)
        {
            await pipe.DisposeAsync().ConfigureAwait(false);
        }

        ClientId = null;
        SetState(ClientConnectionState.Disconnected, null);
    }

    private void SetState(ClientConnectionState state, Guid? clientId)
    {
        if (State == state && ClientId == clientId)
        {
            return;
        }

        State = state;
        Changed?.Invoke(this, new ClientConnectionChangedEventArgs(state, clientId));
    }

    private static bool IsConnectionFailure(Exception exception)
    {
        return exception is IOException
            or EndOfStreamException
            or InvalidOperationException
            or ConnectionLostException
            or ObjectDisposedException;
    }
}
