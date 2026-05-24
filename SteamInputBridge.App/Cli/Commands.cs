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
        Command client = new("client");
        Command run = new("run", "Connect to the server.");

        Argument<string> profile = new("profile")
        {
            Description = "Profile id to launch.",
        };
        Option<uint?> steamAppId = new("--app-id")
        {
            Description = "Steam app id to report for this client run.",
        };
        Option<bool> kill = new("--kill")
        {
            Description = "Kill matching receiver processes when the client is stopped.",
        };

        run.Arguments.Add(profile);
        run.Options.Add(steamAppId);
        run.Options.Add(kill);
        run.SetAction((parseResult, cancellationToken) =>
            RunClientAsync(parseResult, AppHostSetup.CreateCli, cancellationToken));
        client.Subcommands.Add(run);

        return client;
    }

    public static Command CreateShortcutCommand()
    {
        Command shortcut = new("shortcut", "Run a profile from a Steam shortcut.");
        Argument<string> profile = new("profile")
        {
            Description = "Profile id to launch.",
        };
        Option<uint?> steamAppId = new("--app-id")
        {
            Description = "Steam app id to report for this client run.",
        };
        Option<bool> kill = new("--kill")
        {
            Description = "Kill matching receiver processes when the client is stopped.",
        };

        shortcut.Arguments.Add(profile);
        shortcut.Options.Add(steamAppId);
        shortcut.Options.Add(kill);
        shortcut.SetAction((parseResult, cancellationToken) =>
            RunClientAsync(parseResult, AppHostSetup.CreateShortcut, cancellationToken));
        return shortcut;
    }

    public static Command CreateServerCommand()
    {
        Command server = new("server");
        Command run = new("run", "Run the server.");

        run.SetAction(RunServerAsync);
        server.Subcommands.Add(run);
        server.Subcommands.Add(ServerStatusCommand.Create());

        return server;
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
        bool kill = parseResult.GetValue<bool>("--kill");

        await ClientServerBootstrapper.EnsureServerStartedAsync(cancellationToken).ConfigureAwait(false);

        using IHost app = createHost();
        GameClient game = app.Services.GetRequiredService<GameClient>();
        await using (game.ConfigureAwait(false))
        {
            await game.RunAsync(profileId, steamAppId, kill, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task RunServerAsync(ParseResult parseResult, CancellationToken cancellationToken)
    {
        _ = parseResult;

        using IHost app = AppHostSetup.CreateCli();
        SrmExport.ExportOnServerStartup(app.Services);
        ServerService server = app.Services.GetRequiredService<ServerService>();
        await using (server.ConfigureAwait(false))
        {
            await server.RunAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
