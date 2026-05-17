using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using VirtualMouse.Client;
using VirtualMouse.Server;

namespace Refactor.Cli;

internal static class Commands
{
    // MARK: Commands
    // ========================================================================

    public static Command CreateServerCommand()
    {
        Command server = new("server");
        Command run = new("run", "Run the server.");
        run.SetAction(RunServerAsync);
        server.Subcommands.Add(run);
        return server;
    }

    public static Command CreateClientCommand()
    {
        Command client = new("client");
        Command run = new("run", "Connect to the server.");
        run.SetAction(RunClientAsync);
        client.Subcommands.Add(run);
        return client;
    }

    // MARK: Handlers
    // ========================================================================

    private static async Task RunServerAsync(ParseResult parseResult, CancellationToken cancellationToken)
    {
        _ = parseResult;
        using IHost app = AppSetup.Create();
        await app.Services.GetRequiredService<VirtualMouseServer>().RunAsync(cancellationToken);
    }

    private static async Task RunClientAsync(ParseResult parseResult, CancellationToken cancellationToken)
    {
        _ = parseResult;
        using IHost app = AppSetup.Create();
        ILogger logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("client");
        await using VirtualMouseClient client = app.Services.GetRequiredService<VirtualMouseClient>();
        client.ConnectionChanged += (_, update) =>
        {
            logger.LogInformation(
                "Connection changed: {State} client={ClientId}",
                update.State,
                update.ClientId?.ToString() ?? "none");
        };

        await client.ConnectAsync(cancellationToken);
        await client.WaitAsync(cancellationToken);
    }
}
