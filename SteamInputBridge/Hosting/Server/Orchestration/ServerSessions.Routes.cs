using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SteamInputBridge.Runtime;
using SteamInputBridge.Settings.Profiles;
using ForwardingControllerOutput = SteamInputBridge.Forwarding.Controller.ControllerOutput;
using ForwardingMouseOutput = SteamInputBridge.Forwarding.Mouse.MouseOutput;
using ProfileControllerOutput = SteamInputBridge.Settings.Profiles.ControllerOutput;
using ProfileMouseOutput = SteamInputBridge.Settings.Profiles.MouseOutput;

namespace SteamInputBridge.Hosting.Server.Orchestration;

internal sealed partial class ServerSessions
{
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
        string controllerPipeName = resolved.ControllerOutput == ProfileControllerOutput.None
            ? string.Empty
            : controllerPipes.Start(clientId);
        routeStateChanged?.Invoke();

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
        IReadOnlyList<ClientControllerInfo> registered = controllerPipes.RegisterControllers(clientId, controllers);
        if (logger.IsEnabled(LogLevel.Information))
        {
            string routes = FormatControllerRegistrations(registered);
            HostingLog.ClientControllersRegistered(
                logger,
                clientId,
                registered.Count,
                routes);
        }

        routeStateChanged?.Invoke();
        return Task.CompletedTask;
    }

    internal Task UpdateRunProcessesAsync(
        Guid clientId,
        IReadOnlyList<ObservedGameProcess> processes)
    {
        runtime.UpdateClient(clientId, processes);
        routeStateChanged?.Invoke();
        return Task.CompletedTask;
    }

    internal Task<IReadOnlyList<ObservedGameProcess>> GetOwnedReceiverProcessesAsync(Guid clientId)
    {
        return Task.FromResult(runtime.GetClientProcesses(clientId));
    }

    private static string FormatControllerRegistrations(IReadOnlyList<ClientControllerInfo> controllers)
    {
        if (controllers.Count == 0)
        {
            return "none";
        }

        List<string> values = [];
        foreach (ClientControllerInfo controller in controllers)
        {
            values.Add(
                $"idx={controller.ControllerIndex} route=\"{controller.PhysicalControllerId}\" physical=\"{controller.PhysicalDeviceId ?? "none"}\" label=\"{controller.Label}\" vidpid={controller.VendorId:x4}:{controller.ProductId:x4} features={controller.Features}");
        }

        return string.Join("; ", values);
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
