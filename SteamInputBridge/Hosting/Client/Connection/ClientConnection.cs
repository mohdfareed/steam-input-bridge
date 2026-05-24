using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using StreamJsonRpc;

namespace SteamInputBridge.Hosting.Client.Connection;

internal sealed class ClientConnection(
    ILogger<ClientConnection> logger,
    string? pipeName = null) : IDisposable, IAsyncDisposable
{
    private const string DefaultPipeName = "SteamInputBridge";

    private static readonly TimeSpan PipeConnectTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan KeepAliveRequestTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan KeepAliveDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(1);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private NamedPipeClientStream? _pipe;
    private IHostServerApi? _server;
    private bool _disposed;

    internal event EventHandler<ClientConnectionChangedEventArgs>? Changed;

    internal Guid? ClientId { get; private set; }

    internal ClientConnectionState State { get; private set; } = ClientConnectionState.Disconnected;

    internal IHostServerApi Server => _server ?? throw new InvalidOperationException("Client is not connected.");

    internal async Task ConnectAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
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
        ThrowIfDisposed();
        while (!cancellationToken.IsCancellationRequested)
        {
            await Task
                .Delay(KeepAliveDelay, cancellationToken)
                .ConfigureAwait(false);

            try
            {
                await Server
                    .AckAsync()
                    .WaitAsync(KeepAliveRequestTimeout, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (IsConnectionFailure(exception))
            {
                HostingLog.ServerConnectionLost(logger, exception.Message);
                await ClearAsync().ConfigureAwait(false);
                await ReconnectAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }

    internal async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await ClearAsync().ConfigureAwait(false);
        _gate.Dispose();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        IHostServerApi? server = _server;
        _server = null;
        (server as IDisposable)?.Dispose();

        NamedPipeClientStream? pipe = _pipe;
        _pipe = null;
        pipe?.Dispose();

        ClientId = null;
        SetState(ClientConnectionState.Disconnected, null);
        _gate.Dispose();
    }

    ValueTask IAsyncDisposable.DisposeAsync()
    {
        return DisposeAsync();
    }

    private async Task OpenAsync(CancellationToken cancellationToken)
    {
        string name = string.IsNullOrWhiteSpace(pipeName)
            ? DefaultPipeName
            : pipeName;
        SetState(ClientConnectionState.Connecting, null);
        HostingLog.ConnectingToServerPipe(logger, name);

        NamedPipeClientStream pipe = new(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
        try
        {
            await pipe
                .ConnectAsync((int)PipeConnectTimeout.TotalMilliseconds, cancellationToken)
                .ConfigureAwait(false);
            IHostServerApi server = JsonRpc.Attach<IHostServerApi>(pipe);
            Guid clientId = await server
                .ConnectAsync(Environment.ProcessId)
                .WaitAsync(PipeConnectTimeout, cancellationToken)
                .ConfigureAwait(false);

            _pipe = pipe;
            _server = server;
            ClientId = clientId;
            SetState(ClientConnectionState.Connected, ClientId);
            HostingLog.ConnectedToServer(logger, ClientId);
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
                HostingLog.ReconnectFailed(logger, exception.Message);
                await ClearAsync().ConfigureAwait(false);
                await Task
                    .Delay(ReconnectDelay, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
    }

    private async Task ClearAsync()
    {
        IHostServerApi? server = Interlocked.Exchange(ref _server, null);
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
            or TimeoutException
            or ConnectionLostException
            or ObjectDisposedException;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
