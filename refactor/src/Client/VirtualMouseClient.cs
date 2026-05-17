using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VirtualMouse.Protocol;

namespace VirtualMouse.Client;

// The app-facing client: connect it, send requests through it, then dispose it.
public sealed class VirtualMouseClient(
    IOptions<ConnectionOptions> options,
    ILogger<VirtualMouseClient> logger) : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private ClientConnection? _connection;
    private bool _disposed;

    // MARK: API
    // ============================================================================

    public event EventHandler<ClientConnectionChangedEventArgs>? ConnectionChanged;

    public Guid? ClientId { get; private set; }

    public ClientConnectionState State { get; private set; } = ClientConnectionState.Disconnected;

    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_connection is not null)
            {
                return;
            }

            await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _ = _gate.Release();
        }
    }

    public async Task<Ack> AckAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        return await GetConnection().AckAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task WaitAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();

        // The CLI uses this as its lifetime; other callers can issue requests directly.
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
                await ClearConnectionAsync().ConfigureAwait(false);
                await ReconnectAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await ClearConnectionAsync().ConfigureAwait(false);
        _gate.Dispose();
    }

    // MARK: Connection
    // ============================================================================

    private async Task OpenConnectionAsync(CancellationToken cancellationToken)
    {
        string pipeName = options.Value.PipeName;
        SetState(ClientConnectionState.Connecting, null);
        logger.LogInformation("Connecting to server pipe {PipeName}", pipeName);

        try
        {
            ClientConnection connection = await ClientConnection
                .ConnectAsync(pipeName, cancellationToken)
                .ConfigureAwait(false);

            _connection = connection;
            ClientId = connection.ClientId;
            SetState(ClientConnectionState.Connected, ClientId);
            logger.LogInformation("Connected to server as {ClientId}", ClientId);
        }
        catch
        {
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
                await ClearConnectionAsync().ConfigureAwait(false);
                await Task
                    .Delay(TimeSpan.FromMilliseconds(options.Value.ReconnectDelayMilliseconds), cancellationToken)
                    .ConfigureAwait(false);
            }
        }
    }

    private async Task ClearConnectionAsync()
    {
        ClientConnection? connection = Interlocked.Exchange(ref _connection, null);
        if (connection is not null)
        {
            await connection.DisposeAsync().ConfigureAwait(false);
        }

        ClientId = null;
        SetState(ClientConnectionState.Disconnected, null);
    }

    // MARK: State
    // ============================================================================

    private ClientConnection GetConnection()
    {
        return _connection ?? throw new InvalidOperationException("Client is not connected.");
    }

    private void SetState(ClientConnectionState state, Guid? clientId)
    {
        if (State == state && ClientId == clientId)
        {
            return;
        }

        State = state;
        ConnectionChanged?.Invoke(this, new ClientConnectionChangedEventArgs(state, clientId));
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private static bool IsConnectionFailure(Exception exception)
    {
        return exception is IOException or EndOfStreamException or InvalidOperationException;
    }
}
