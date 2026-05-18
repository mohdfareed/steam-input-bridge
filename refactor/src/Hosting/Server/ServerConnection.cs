using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using StreamJsonRpc;
using VirtualMouse.Runtime;

namespace VirtualMouse.Hosting;

internal sealed class ServerConnection(
    NamedPipeServerStream pipe,
    ServerSessions sessions) : IVirtualMouseServerApi, IAsyncDisposable
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
                DisconnectClient();
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

    public Task<ClientRunLaunch> StartRunAsync(StartRunRequest request)
    {
        return sessions.StartRunAsync(GetClientId(), request);
    }

    public Task UpdateRunProcessesAsync(IReadOnlyList<ObservedGameProcess> processes)
    {
        return sessions.UpdateRunProcessesAsync(GetClientId(), processes);
    }

    public Task<IReadOnlyList<ObservedGameProcess>> GetOwnedReceiverProcessesAsync()
    {
        return sessions.GetOwnedReceiverProcessesAsync(GetClientId());
    }

    public Task EndRunAsync()
    {
        return sessions.EndRunAsync(GetClientId());
    }

    private void DisconnectClient()
    {
        if (_clientId is Guid clientId)
        {
            sessions.DisconnectClient(clientId);
            _clientId = null;
        }
    }

    private Guid GetClientId()
    {
        return _clientId ?? throw new InvalidOperationException("Client is not connected.");
    }
}
