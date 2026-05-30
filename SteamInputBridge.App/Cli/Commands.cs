using System;
using System.CommandLine;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SteamInputBridge.Hosting.Client.Run;
using SteamInputBridge.Hosting.Server.Orchestration.Lifetime;
using AppHostSetup = SteamInputBridge.App.AppSetup;

namespace SteamInputBridge.App.Cli;

internal static class Commands
{
    // MARK: Commands
    // ========================================================================

    public static Command CreateClientCommand()
    {
        Command client = new("client", "Run profile clients.");
        Command run = CreateRunProfileCommand(
            "run",
            "Run a profile from the CLI.",
            AppHostSetup.CreateCli);
        client.Subcommands.Add(run);

        return client;
    }

    public static Command CreateShortcutCommand()
    {
        return CreateRunProfileCommand(
            "shortcut",
            "Run a profile from a Steam shortcut.",
            AppHostSetup.CreateShortcut);
    }

    public static Command CreateServerCommand()
    {
        Command server = new("server", "Run or inspect the local server.");
        Command run = new("run", "Run the local server.");

        run.SetAction(RunServerAsync);
        server.Subcommands.Add(run);
        server.Subcommands.Add(ServerStatusCommand.Create());

        return server;
    }

    private static Command CreateRunProfileCommand(
        string name,
        string description,
        Func<IHost> createHost)
    {
        Command command = new(name, description);
        Argument<string> profile = new("profile")
        {
            Description = "Profile id to run.",
        };
        Option<uint?> steamAppId = new("--app-id")
        {
            Description = "Steam app id to report for this client run.",
        };

        command.Arguments.Add(profile);
        command.Options.Add(steamAppId);
        command.SetAction((parseResult, cancellationToken) =>
            RunClientAsync(parseResult, createHost, cancellationToken));
        return command;
    }

    // MARK: Handlers
    // ========================================================================

    private static async Task RunClientAsync(
        ParseResult parseResult,
        Func<IHost> createHost,
        CancellationToken cancellationToken)
    {
        string? profileId = parseResult.GetValue<string>("profile");
        ArgumentException.ThrowIfNullOrEmpty(profileId, nameof(profileId));
        uint? steamAppId = parseResult.GetValue<uint?>("--app-id");

        await ClientServerBootstrapper.EnsureServerStartedAsync(cancellationToken).ConfigureAwait(false);

        using IHost app = createHost();
        GameClient game = app.Services.GetRequiredService<GameClient>();
        await using (game.ConfigureAwait(false))
        {
            await game.RunAsync(profileId, steamAppId, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task RunServerAsync(ParseResult parseResult, CancellationToken cancellationToken)
    {
        _ = parseResult;

        using IHost app = AppHostSetup.CreateCli();
        ServerService server = app.Services.GetRequiredService<ServerService>();
        await using (server.ConfigureAwait(false))
        {
            await server.RunAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
