using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using VirtualMouse.Forwarding;
using VirtualMouse.Runtime;
using VirtualMouse.Settings.Profiles;
using ForwardingControllerOutput = VirtualMouse.Forwarding.ControllerOutput;
using ForwardingMouseOutput = VirtualMouse.Forwarding.MouseOutput;
using ProfileControllerOutput = VirtualMouse.Settings.Profiles.ControllerOutput;
using ProfileMouseOutput = VirtualMouse.Settings.Profiles.MouseOutput;

namespace VirtualMouse.Hosting;

internal sealed record ConnectedClient(Guid Id, int ProcessId, DateTimeOffset ConnectedAt);

internal sealed class ServerSessions(
    ILogger logger,
    ProfilesService? profiles,
    ActiveClientRegistry runtime,
    ControllerBroker forwarding,
    MouseBroker mouseForwarding,
    ControllerPipeSessions controllerPipes,
    Func<ServerInputStatus>? getInputStatus = null)
{
    private readonly ConcurrentDictionary<Guid, ConnectedClient> _clients = [];

    internal IReadOnlyCollection<ConnectedClient> Clients => [.. _clients.Values];

    internal Guid ConnectClient(int processId)
    {
        ConnectedClient client = new(Guid.NewGuid(), processId, DateTimeOffset.UtcNow);
        _clients[client.Id] = client;
        logger.LogInformation(
            "Client connected: {ClientId} process={ProcessId} (clients={ClientCount})",
            client.Id,
            client.ProcessId,
            _clients.Count);
        return client.Id;
    }

    internal async Task DisconnectClientAsync(Guid clientId)
    {
        _ = _clients.TryRemove(clientId, out _);
        runtime.RemoveClient(clientId);
        forwarding.RemoveClient(clientId);
        mouseForwarding.RemoveClient(clientId);
        await controllerPipes.RemoveAsync(clientId).ConfigureAwait(false);

        logger.LogInformation(
            "Client disconnected: {ClientId} (clients={ClientCount})",
            clientId,
            _clients.Count);
    }

    internal Task<ServerStatus> GetStatusAsync()
    {
        return Task.FromResult(new ServerStatus(_clients.Count)
        {
            Runtime = runtime.GetStatus(),
            Forwarding = forwarding.GetStatus(),
            MouseForwarding = mouseForwarding.GetStatus(),
            Inputs = getInputStatus?.Invoke() ??
                new ServerInputStatus(
                    new PhysicalControllerPumpStatus(false, 0, [], null),
                    new MouseInputPumpStatus(false, false, null)),
            ControllerPipes = controllerPipes.GetStatus(),
        });
    }

    internal Task<ClientRunLaunch> StartRunAsync(Guid clientId, StartRunRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (profiles is null)
        {
            throw new InvalidOperationException("Profile settings are not available.");
        }

        GameProfile profile = profiles.GetProfile(request.ProfileId) ??
            throw new InvalidOperationException($"Profile \"{request.ProfileId}\" was not found.");
        ResolvedGameProfile resolved = ProfileResolver.Resolve(request.ProfileId, profile);
        ConnectedClient client = GetClient(clientId);

        runtime.RegisterClient(
            clientId,
            client.ProcessId,
            resolved.Id,
            request.SteamAppId,
            resolved.ReceiverProcesses);

        forwarding.RegisterClient(clientId, MapControllerOutput(resolved.ControllerOutput));
        mouseForwarding.RegisterClient(clientId, MapMouseOutput(resolved.MouseOutput));
        string controllerPipeName = controllerPipes.Start(clientId);

        return Task.FromResult(new ClientRunLaunch(
            resolved.Id,
            resolved.Title,
            resolved.Executable,
            resolved.Arguments,
            resolved.WorkingDirectory,
            resolved.ReceiverProcesses,
            resolved.ControllerOutput,
            resolved.MouseOutput,
            controllerPipeName));
    }

    internal Task RegisterClientControllersAsync(
        Guid clientId,
        IReadOnlyList<ClientControllerInfo> controllers)
    {
        _ = GetClient(clientId);
        controllerPipes.RegisterControllers(clientId, controllers);
        return Task.CompletedTask;
    }

    internal Task UpdateRunProcessesAsync(
        Guid clientId,
        IReadOnlyList<ObservedGameProcess> processes)
    {
        runtime.UpdateClientProcesses(clientId, processes);
        return Task.CompletedTask;
    }

    internal Task<IReadOnlyList<ObservedGameProcess>> GetOwnedReceiverProcessesAsync(Guid clientId)
    {
        return Task.FromResult(runtime.GetOwnedProcesses(clientId));
    }

    internal async Task EndRunAsync(Guid clientId)
    {
        runtime.RemoveClient(clientId);
        forwarding.RemoveClient(clientId);
        mouseForwarding.RemoveClient(clientId);
        await controllerPipes.RemoveAsync(clientId).ConfigureAwait(false);
    }

    internal void ConnectionClosed(Exception exception)
    {
        if (exception is not OperationCanceledException)
        {
            logger.LogInformation("Client pipe closed: {Message}", exception.Message);
        }
    }

    private ConnectedClient GetClient(Guid clientId)
    {
        return _clients.TryGetValue(clientId, out ConnectedClient? client)
            ? client
            : throw new InvalidOperationException($"Client {clientId} is not connected.");
    }

    private static ForwardingControllerOutput MapControllerOutput(ProfileControllerOutput output)
    {
        return output switch
        {
            ProfileControllerOutput.None => ForwardingControllerOutput.None,
            ProfileControllerOutput.Xbox360 => ForwardingControllerOutput.Xbox360,
            ProfileControllerOutput.Ds4 => ForwardingControllerOutput.Ds4,
            _ => throw new ArgumentOutOfRangeException(nameof(output), output, "Unknown controller output."),
        };
    }

    private static ForwardingMouseOutput MapMouseOutput(ProfileMouseOutput output)
    {
        return output switch
        {
            ProfileMouseOutput.None => ForwardingMouseOutput.None,
            ProfileMouseOutput.Viiper => ForwardingMouseOutput.Viiper,
            ProfileMouseOutput.Teensy => ForwardingMouseOutput.Teensy,
            _ => throw new ArgumentOutOfRangeException(nameof(output), output, "Unknown mouse output."),
        };
    }
}
