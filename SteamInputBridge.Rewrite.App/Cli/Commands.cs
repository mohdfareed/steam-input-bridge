using System.CommandLine;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using SteamInputBridge.App.Hosting;

namespace SteamInputBridge.App.Cli;

internal static class Commands
{
    // MARK: Commands
    // ========================================================================

    public static Command CreateServerCommand()
    {
        Command status = new("status", "Print server status.");

        Command run = new("run", "Run the local server.");
        run.SetAction((_, token) => RunServerAsync(token));

        Command server = new("server", "Run or inspect the local server.");
        server.Subcommands.Add(status);
        server.Subcommands.Add(run);

        return server;
    }

    public static Command CreateClientCommand()
    {
        Command run = new("run", "Run a profile from the CLI.");
        run.Arguments.Add(new Argument<string>("profile")
        {
            Description = "Profile id to run.",
        });
        run.SetAction((parseResult, token) => RunClientAsync(parseResult.GetValue<string>("profile")!, token));

        Command client = new("client", "Run profile clients.");
        client.Subcommands.Add(run);

        return client;
    }

    public static Command CreateSteamCommand()
    {
        Command steam = new("steam", "Manage Steam integration.");
        return steam;
    }

    public static Command CreateShortcutCommand()
    {
        Command shortcut = new("shortcut", "Run from a Steam shortcut.");
        return shortcut;
    }

    // MARK: Command Handlers
    // ========================================================================

    private static async Task RunServerAsync(CancellationToken cancellationToken)
    {
        using IHost host = AppHost.CreateServer();
        await host.RunAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task RunClientAsync(string profileId, CancellationToken cancellationToken)
    {
        using IHost host = AppHost.CreateClient(profileId);
        await host.RunAsync(cancellationToken).ConfigureAwait(false);
    }
}
