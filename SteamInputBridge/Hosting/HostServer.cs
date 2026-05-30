using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using SteamInputBridge.Hosting.Server.Orchestration;
using SteamInputBridge.Runtime;
using StreamJsonRpc;

namespace SteamInputBridge.Hosting;

internal sealed class HostServer(
    NamedPipeServerStream pipe,
    ServerSessions sessions) : IHostServerApi, IDisposable, IAsyncDisposable
{
    private Guid? _clientId;

    internal async Task RunAsync(CancellationToken cancellationToken)
    {
        await using (pipe.ConfigureAwait(false))
        {
            using CancellationTokenRegistration registration = cancellationToken.Register(static target =>
            {
                ((Stream)target!).Dispose();
            }, pipe);

            using JsonRpc rpc = JsonRpc.Attach(pipe, this);

            try
            {
                await rpc.Completion.ConfigureAwait(false);
            }
            catch (Exception exception) when (IsDisconnect(exception))
            {
                sessions.ConnectionClosed(exception);
            }
            finally
            {
                await DisconnectClientAsync().ConfigureAwait(false);
            }
        }
    }

    internal async ValueTask DisposeAsync()
    {
        await pipe.DisposeAsync().ConfigureAwait(false);
    }

    public void Dispose()
    {
        pipe.Dispose();
    }

    ValueTask IAsyncDisposable.DisposeAsync()
    {
        return DisposeAsync();
    }

    private static bool IsDisconnect(Exception exception)
    {
        return exception is IOException or EndOfStreamException or ObjectDisposedException or OperationCanceledException;
    }

    public Task<Guid> ConnectAsync(int processId)
    {
        if (_clientId is not null)
        {
            throw new InvalidOperationException("Client is already connected.");
        }

        _clientId = sessions.ConnectClient(processId);
        return Task.FromResult(_clientId.Value);
    }

    public Task AckAsync()
    {
        return Task.CompletedTask;
    }

    public Task<ServerStatus> GetStatusAsync()
    {
        return sessions.GetStatusAsync();
    }

    public Task<ClientRunLaunch> RegisterRunAsync(RegisterRunRequest request)
    {
        return sessions.RegisterRunAsync(GetClientId(), request);
    }

    public Task RegisterClientControllersAsync(IReadOnlyList<ClientControllerInfo> controllers)
    {
        return sessions.RegisterClientControllersAsync(GetClientId(), controllers);
    }

    public Task UpdateRunProcessesAsync(IReadOnlyList<ObservedGameProcess> processes)
    {
        return sessions.UpdateRunProcessesAsync(GetClientId(), processes);
    }

    public Task EndRunAsync()
    {
        return sessions.EndRunAsync(GetClientId());
    }

    private async Task DisconnectClientAsync()
    {
        if (_clientId is Guid clientId)
        {
            await sessions.DisconnectClientAsync(clientId).ConfigureAwait(false);
            _clientId = null;
        }
    }

    private Guid GetClientId()
    {
        return _clientId ?? throw new InvalidOperationException("Client is not connected.");
    }
}

internal sealed class ServerConnectionHandle : IAsyncDisposable
{
    private readonly HostServer _connection;

    private ServerConnectionHandle(HostServer connection, CancellationToken cancellationToken)
    {
        _connection = connection;
        Completion = RunAsync(cancellationToken);
    }

    public Task Completion { get; }

    public static ServerConnectionHandle Start(
        NamedPipeServerStream pipe,
        ServerSessions sessions,
        CancellationToken cancellationToken)
    {
        HostServer? connection = new(pipe, sessions);
        try
        {
            ServerConnectionHandle handle = new(connection, cancellationToken);
            connection = null;
            return handle;
        }
        finally
        {
            connection?.Dispose();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _connection.DisposeAsync().ConfigureAwait(false);
        await IgnoreExpectedStopAsync(Completion).ConfigureAwait(false);
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _connection.RunAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await _connection.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static async Task IgnoreExpectedStopAsync(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (Exception exception) when (IsExpectedStop(exception))
        {
        }
    }

    private static bool IsExpectedStop(Exception exception)
    {
        return exception is OperationCanceledException or ObjectDisposedException or IOException;
    }
}
