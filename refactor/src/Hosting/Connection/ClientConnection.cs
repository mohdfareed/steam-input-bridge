using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VirtualMouse.Settings;

namespace VirtualMouse.Hosting;

internal sealed class ClientConnection(
    IOptions<HostingSettings> options,
    ILogger<ClientConnection> logger) : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private NamedPipeClientStream? _pipe;
    private RequestResponsePipe? _messages;

    internal event EventHandler<ClientConnectionChangedEventArgs>? Changed;

    internal Guid? ClientId { get; private set; }

    internal ClientConnectionState State { get; private set; } = ClientConnectionState.Disconnected;

    internal async Task ConnectAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_messages is not null)
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
                _ = await AckAsync(cancellationToken).ConfigureAwait(false);
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

    private Task<Ack> AckAsync(CancellationToken cancellationToken)
    {
        return Messages.AckAsync(cancellationToken);
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
            RequestResponsePipe messages = new(pipe);
            ConnectResponse response = await messages
                .ConnectAsync(Environment.ProcessId, cancellationToken)
                .ConfigureAwait(false);

            _pipe = pipe;
            _messages = messages;
            ClientId = response.ClientId;
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
        RequestResponsePipe? messages = Interlocked.Exchange(ref _messages, null);
        if (messages is not null)
        {
            await messages.DisposeAsync().ConfigureAwait(false);
        }

        NamedPipeClientStream? pipe = Interlocked.Exchange(ref _pipe, null);
        if (pipe is not null)
        {
            await pipe.DisposeAsync().ConfigureAwait(false);
        }

        ClientId = null;
        SetState(ClientConnectionState.Disconnected, null);
    }

    private RequestResponsePipe Messages => _messages ?? throw new InvalidOperationException("Client is not connected.");

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
        return exception is IOException or EndOfStreamException or InvalidOperationException;
    }
}
