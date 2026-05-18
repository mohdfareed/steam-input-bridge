using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using VirtualMouse.Forwarding;

namespace VirtualMouse.Hosting;

internal sealed class ControllerPipeSessions(ControllerBroker broker, ILogger logger) : IAsyncDisposable
{
    private readonly Dictionary<Guid, ClientControllerPipe> _pipes = [];

    public string Start(Guid clientId)
    {
        if (_pipes.TryGetValue(clientId, out ClientControllerPipe? existing))
        {
            return existing.PipeName;
        }

        string pipeName = $"VirtualMouse.Refactor.Controller.{clientId:N}";
        ClientControllerPipe pipe = new(clientId, pipeName, broker, logger);
        _pipes[clientId] = pipe;
        pipe.Start();
        return pipeName;
    }

    public void RegisterControllers(
        Guid clientId,
        IReadOnlyList<ClientControllerInfo> controllers)
    {
        Get(clientId).RegisterControllers(controllers);
    }

    public async Task RemoveAsync(Guid clientId)
    {
        if (_pipes.Remove(clientId, out ClientControllerPipe? pipe))
        {
            await pipe.DisposeAsync().ConfigureAwait(false);
        }
    }

    public IReadOnlyList<ControllerPipeStatus> GetStatus()
    {
        List<ControllerPipeStatus> status = [];
        foreach (KeyValuePair<Guid, ClientControllerPipe> pipe in _pipes)
        {
            status.Add(pipe.Value.GetStatus(pipe.Key));
        }

        return status;
    }

    public async ValueTask DisposeAsync()
    {
        ClientControllerPipe[] pipes = [.. _pipes.Values];
        _pipes.Clear();
        foreach (ClientControllerPipe pipe in pipes)
        {
            await pipe.DisposeAsync().ConfigureAwait(false);
        }
    }

    private ClientControllerPipe Get(Guid clientId)
    {
        return _pipes.TryGetValue(clientId, out ClientControllerPipe? pipe)
            ? pipe
            : throw new InvalidOperationException($"Controller pipe for client {clientId} is not active.");
    }
}
