using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace SteamInputBridge.Hosting.Server.Orchestration.Lifetime;

internal sealed class ServerConnectionTracker : IAsyncDisposable
{
    private readonly ConcurrentDictionary<ServerConnectionHandle, byte> _connections = [];

    public void Track(ServerConnectionHandle connection)
    {
        _connections[connection] = 0;
        _ = RemoveWhenCompleteAsync(connection);
    }

    public async ValueTask DisposeAsync()
    {
        ServerConnectionHandle[] connections = [.. _connections.Keys];
        foreach (ServerConnectionHandle connection in connections)
        {
            _ = _connections.TryRemove(connection, out _);
            await connection.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task RemoveWhenCompleteAsync(ServerConnectionHandle connection)
    {
        await IgnoreCancellationAsync(connection.Completion).ConfigureAwait(false);
        _ = _connections.TryRemove(connection, out _);
    }

    private static async Task IgnoreCancellationAsync(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
    }
}
