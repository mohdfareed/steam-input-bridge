using System.CommandLine;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using SteamInputBridge.Cli.Host;

namespace SteamInputBridge.Cli.Commands;

internal static class ClientCommands
{
    public static Command CreateCommand()
    {
        Command run = new("run", "Run a profile from the CLI.");
        run.Arguments.Add(new Argument<string>("profile") { Description = "Profile id to run." });
        run.SetAction((parseResult, token) => RunClientAsync(parseResult.GetValue<string>("profile")!, token));

        Command client = new("client", "Run profile clients.");
        client.Subcommands.Add(run);
        return client;
    }

    private static async Task RunClientAsync(string profileId, CancellationToken cancellationToken)
    {
        using IHost host = CliHost.CreateClient(profileId);
        await host.RunAsync(cancellationToken).ConfigureAwait(false);
    }
}
