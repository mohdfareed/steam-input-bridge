using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using VirtualMouse.Forwarding;
using VirtualMouse.Hosting;

namespace Refactor.Cli;

internal static class Commands
{
    // MARK: Commands
    // ========================================================================

    public static Command CreateClientCommand()
    {
        Command client = new("client");
        Command run = new("run", "Connect to the server.");
        Argument<string> profile = new("profile")
        {
            Description = "Profile id to launch.",
        };

        run.Arguments.Add(profile);
        run.SetAction(RunClientAsync);
        client.Subcommands.Add(run);
        return client;
    }

    public static Command CreateServerCommand()
    {
        Command server = new("server");
        Command run = new("run", "Run the server.");
        run.SetAction(RunServerAsync);
        Command status = new("status", "Print server status.");
        status.SetAction(RunServerStatusAsync);
        server.Subcommands.Add(run);
        server.Subcommands.Add(status);
        return server;
    }

    // MARK: Handlers
    // ========================================================================

    private static async Task RunClientAsync(ParseResult parseResult, CancellationToken cancellationToken)
    {
        string profileId = parseResult.GetValue<string>("profile") ??
            throw new InvalidOperationException("Profile id is required.");
        using IHost app = AppSetup.Create();
        await using GameClient game = app.Services.GetRequiredService<GameClient>();
        await game.RunAsync(profileId, cancellationToken).ConfigureAwait(false);
    }

    private static async Task RunServerAsync(ParseResult parseResult, CancellationToken cancellationToken)
    {
        _ = parseResult;
        using IHost app = AppSetup.Create();
        await app.Services.GetRequiredService<VirtualMouseServer>().RunAsync(cancellationToken);
    }

    private static async Task RunServerStatusAsync(ParseResult parseResult, CancellationToken cancellationToken)
    {
        _ = parseResult;
        using IHost app = AppSetup.Create();
        await using VirtualMouseClient client = app.Services.GetRequiredService<VirtualMouseClient>();
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));

        try
        {
            await client.ConnectAsync(timeout.Token);
            ServerStatus status = await client.GetStatusAsync(timeout.Token);
            await PrintStatusAsync(status).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            await Console.Out.WriteLineAsync("server running=false").ConfigureAwait(false);
        }
        catch (IOException exception)
        {
            await Console.Error.WriteLineAsync($"server status: unavailable ({exception.Message})")
                .ConfigureAwait(false);
            Environment.ExitCode = 1;
        }
    }

    private static async Task PrintStatusAsync(ServerStatus status)
    {
        await Console.Out.WriteLineAsync(
                $"server running=true connectedClients={status.ConnectedClientCount}")
            .ConfigureAwait(false);
        await Console.Out.WriteLineAsync(
                $"activeClient={status.Runtime.ActiveClientId?.ToString() ?? "none"} foregroundPid={status.Runtime.ForegroundProcessId}")
            .ConfigureAwait(false);
        await Console.Out.WriteLineAsync(
                $"physicalSdl running={status.Inputs.PhysicalControllers.Running} controllers={status.Inputs.PhysicalControllers.ControllerCount} error={status.Inputs.PhysicalControllers.LastError ?? "none"}")
            .ConfigureAwait(false);
        foreach (string controllerId in status.Inputs.PhysicalControllers.ControllerIds)
        {
            await Console.Out.WriteLineAsync($"  physical {controllerId}").ConfigureAwait(false);
        }

        await Console.Out.WriteLineAsync(
                $"rawInput running={status.Inputs.Mouse.Running} connected={status.Inputs.Mouse.SourceConnected} error={status.Inputs.Mouse.LastError ?? "none"}")
            .ConfigureAwait(false);
        await Console.Out.WriteLineAsync(
                $"controllerOutput enabled={status.Forwarding.ControllerOutputEnabled} physicalMotion={status.Forwarding.PhysicalMotionEnabled} slots={status.Forwarding.Slots.Count}")
            .ConfigureAwait(false);
        foreach (ControllerSlotStatus slot in status.Forwarding.Slots)
        {
            await Console.Out.WriteLineAsync(
                    $"  slot {slot.ControllerId} output={slot.Output} connected={slot.OutputConnected} steam={slot.SteamEndpointCount} activeSteam={slot.HasActiveSteamEndpoint} physical={slot.HasPhysicalEndpoint} physicalFeatures={slot.PhysicalFeatures?.ToString() ?? "none"} activeSteamFeatures={slot.ActiveSteamFeatures?.ToString() ?? "none"}")
                .ConfigureAwait(false);
        }

        await Console.Out.WriteLineAsync(
                $"controllerPipes count={status.ControllerPipes.Count}")
            .ConfigureAwait(false);
        foreach (ControllerPipeStatus pipe in status.ControllerPipes)
        {
            await Console.Out.WriteLineAsync(
                    $"  pipe client={pipe.ClientId} connected={pipe.Connected} controllers={pipe.Controllers.Count}")
                .ConfigureAwait(false);
            foreach (ClientControllerStatus controller in pipe.Controllers)
            {
                await Console.Out.WriteLineAsync(
                        $"    controller index={controller.ControllerIndex} physical={controller.PhysicalControllerId} features={controller.Features}")
                    .ConfigureAwait(false);
            }
        }
    }
}
