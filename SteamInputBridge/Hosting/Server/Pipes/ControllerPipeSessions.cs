using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SteamInputBridge.Forwarding.Controller.Routing;
using SteamInputBridge.Hosting.Server.Orchestration;
using SteamInputBridge.Hosting.Server.Orchestration.Lifetime;

namespace SteamInputBridge.Hosting.Server.Pipes;

internal sealed class ControllerPipeSessions(
    ControllerBroker broker,
    ILogger logger,
    IPhysicalControllerResolver? physicalControllers = null) : IAsyncDisposable
{
    private readonly Dictionary<Guid, ClientControllerPipe> _pipes = [];

    public string Start(Guid clientId)
    {
        if (_pipes.TryGetValue(clientId, out ClientControllerPipe? existing))
        {
            return existing.PipeName;
        }

        string pipeName = $"SteamInputBridge.Controller.{clientId:N}";
        ClientControllerPipe pipe = new(clientId, pipeName, broker, logger, physicalControllers);
        _pipes[clientId] = pipe;
        pipe.Start();
        return pipeName;
    }

    public ControllerRegistrationResult RegisterControllers(
        Guid clientId,
        IReadOnlyList<ClientControllerInfo> controllers)
    {
        return Get(clientId).RegisterControllers(controllers);
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
        foreach (ClientControllerPipe pipe in _pipes.Values)
        {
            status.Add(pipe.GetStatus());
        }

        return status;
    }

    public bool RefreshControllerRoutes()
    {
        bool changed = false;
        foreach (ClientControllerPipe pipe in _pipes.Values)
        {
            changed |= pipe.RefreshResolvedControllers();
        }

        return changed;
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
