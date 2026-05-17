using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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
            await Console.Out.WriteLineAsync(
                    $"server running=true connectedClients={status.ConnectedClientCount}")
                .ConfigureAwait(false);
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
}
