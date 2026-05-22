using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SteamInputBridge.Hosting.Server.Orchestration;
using SteamInputBridge.Runtime;

namespace SteamInputBridge.Hosting.Client.Connection;

// MARK: Models
// ============================================================================

/// <summary>Current connection state of a client.</summary>
public enum ClientConnectionState
{
    /// <summary>The client has no active server pipe.</summary>
    Disconnected,

    /// <summary>The client is opening a server pipe.</summary>
    Connecting,

    /// <summary>The client has an active server pipe.</summary>
    Connected,
}

/// <summary>Describes a client connection state change.</summary>
public sealed class ClientConnectionChangedEventArgs(ClientConnectionState state, Guid? clientId) : EventArgs
{
    /// <summary>New connection state.</summary>
    public ClientConnectionState State { get; } = state;

    /// <summary>Server-assigned client id when connected.</summary>
    public Guid? ClientId { get; } = clientId;
}

// The app-facing client: connect it, send requests through it, then dispose it.
/// <summary>App-facing client connection to the local server.</summary>
public sealed class ClientService : IDisposable, IAsyncDisposable
{
    private readonly ClientConnection _connection;
    private bool _disposed;

    // MARK: Construction
    // ========================================================================

    /// <summary>Creates a client.</summary>
    public ClientService(ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);
        _connection = new ClientConnection(loggerFactory.CreateLogger<ClientConnection>());
        _connection.Changed += OnConnectionChanged;
    }

    internal ClientService(ILoggerFactory loggerFactory, string pipeName)
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);
        _connection = new ClientConnection(loggerFactory.CreateLogger<ClientConnection>(), pipeName);
        _connection.Changed += OnConnectionChanged;
    }

    internal ClientService(ClientConnection connection)
    {
        _connection = connection;
        _connection.Changed += OnConnectionChanged;
    }

    // MARK: Publics
    // ========================================================================

    /// <summary>Raised when the server connection state changes.</summary>
    public event EventHandler<ClientConnectionChangedEventArgs>? ConnectionChanged;

    /// <summary>Server-assigned client id when connected.</summary>
    public Guid? ClientId => _connection.ClientId;

    /// <summary>Current connection state.</summary>
    public ClientConnectionState State => _connection.State;

    /// <summary>Connects to the server if not already connected.</summary>
    public Task ConnectAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        return _connection.ConnectAsync(cancellationToken);
    }

    /// <summary>Keeps the client alive until cancellation and reconnects when the server restarts.</summary>
    public Task WaitAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        return _connection.WaitAsync(cancellationToken);
    }

    /// <summary>Gets the running server status.</summary>
    public Task<ServerStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        return _connection.Server.GetStatusAsync().WaitAsync(cancellationToken);
    }

    /// <summary>Starts a profile-backed client run.</summary>
    public Task<ClientRunLaunch> StartRunAsync(
        StartRunRequest request,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        return _connection.Server
            .StartRunAsync(request)
            .WaitAsync(cancellationToken);
    }

    /// <summary>Registers controller streams this client will send over its controller pipe.</summary>
    public Task RegisterClientControllersAsync(
        IReadOnlyList<ClientControllerInfo> controllers,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        return _connection.Server
            .RegisterClientControllersAsync(controllers)
            .WaitAsync(cancellationToken);
    }

    /// <summary>Updates receiver processes observed by this client.</summary>
    public Task UpdateRunProcessesAsync(
        IReadOnlyList<ObservedGameProcess> processes,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        return _connection.Server
            .UpdateRunProcessesAsync(processes)
            .WaitAsync(cancellationToken);
    }

    /// <summary>Gets receiver processes currently owned by this client.</summary>
    public Task<IReadOnlyList<ObservedGameProcess>> GetOwnedReceiverProcessesAsync(
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        return _connection.Server
            .GetOwnedReceiverProcessesAsync()
            .WaitAsync(cancellationToken);
    }

    /// <summary>Ends this client's run.</summary>
    public Task EndRunAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        return _connection.Server.EndRunAsync().WaitAsync(cancellationToken);
    }

    /// <summary>Disconnects from the server.</summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _connection.Changed -= OnConnectionChanged;
        await _connection.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>Disconnects from the server.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _connection.Changed -= OnConnectionChanged;
        _connection.Dispose();
    }

    // MARK: Privates
    // ========================================================================

    private void OnConnectionChanged(object? sender, ClientConnectionChangedEventArgs args)
    {
        ConnectionChanged?.Invoke(this, args);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
