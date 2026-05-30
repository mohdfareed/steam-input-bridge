using System;
using System.CommandLine;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using SteamInputBridge.App.Hosting;
using SteamInputBridge.Hosting;
using StreamJsonRpc;

namespace SteamInputBridge.App.Cli;

internal static class Commands
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(3);

    // MARK: Commands
    // ========================================================================

    public static Command CreateServerCommand()
    {
        Command status = new("status", "Print server status.");
        status.Options.Add(new Option<bool>("--json") { Description = "Print status as JSON." });
        status.SetAction((parseResult, token) => PrintServerStatusAsync(parseResult.GetValue<bool>("--json"), token));

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
        run.Arguments.Add(new Argument<string>("profile") { Description = "Profile id to run." });
        run.SetAction((parseResult, token) => RunClientAsync(parseResult.GetValue<string>("profile")!, token));

        Command client = new("client", "Run profile clients.");
        client.Subcommands.Add(run);
        return client;
    }

    public static Command CreateShortcutCommand()
    {
        Command shortcut = new("shortcut", "Run from a Steam shortcut.");
        return shortcut;
    }

    public static Command CreateTrayCommand()
    {
        Command tray = new("tray", "Run the tray application.");
        tray.SetAction((_, _) => Task.FromResult(0));
        return tray;
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

    private static async Task<int> PrintServerStatusAsync(bool json, CancellationToken cancellationToken)
    {
        try
        {
            NamedPipeClientStream pipe = new(".", IBridgeControlApi.Name, PipeDirection.InOut, PipeOptions.Asynchronous);
            await using (pipe.ConfigureAwait(false))
            {
                await pipe.ConnectAsync((int)ConnectTimeout.TotalMilliseconds, cancellationToken).ConfigureAwait(false);
                IBridgeControlApi server = JsonRpc.Attach<IBridgeControlApi>(pipe);
                BridgeServerStatus status = await server
                    .GetStatusAsync()
                    .WaitAsync(ConnectTimeout, cancellationToken)
                    .ConfigureAwait(false);

                if (json)
                {
                    await Console.Out.WriteLineAsync(JsonSerializer.Serialize(status, JsonOptions)).ConfigureAwait(false);
                    return 0;
                }

                await Console.Out.WriteLineAsync("Server").ConfigureAwait(false);
                await Console.Out.WriteLineAsync("------").ConfigureAwait(false);
                await Console.Out.WriteLineAsync($"connectedClients  {status.ConnectedClientCount}").ConfigureAwait(false);
            }

            return 0;
        }
        catch (Exception exception) when (exception is IOException or TimeoutException or ConnectionLostException)
        {
            await Console.Error.WriteLineAsync($"server status: unavailable ({exception.Message})").ConfigureAwait(false);
            return 1;
        }
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        JsonSerializerOptions options = new() { WriteIndented = true };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
