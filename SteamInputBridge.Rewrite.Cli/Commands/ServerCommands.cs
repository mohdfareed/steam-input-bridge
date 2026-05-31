using System;
using System.CommandLine;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using SteamInputBridge.Hosting;
using SteamInputBridge.Hosting.Server;
using StreamJsonRpc;

namespace SteamInputBridge.Cli.Commands;

internal static class ServerCommands
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(3);

    // MARK: Commands
    // ========================================================================

    public static Command CreateCommand()
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

    // MARK: Implementation
    // ========================================================================

    private static async Task<int> RunServerAsync(CancellationToken cancellationToken)
    {
        try
        {
            using IHost host = CliHost.CreateServer();
            await host.RunAsync(cancellationToken).ConfigureAwait(false);
            return 0;
        }
        catch (ServerAlreadyRunningException exception)
        {
            await ConsoleOutput.WriteErrorAsync(exception.Message).ConfigureAwait(false);
            return 1;
        }
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
                BridgeServerStatus status = await server.GetStatusAsync().WaitAsync(ConnectTimeout, cancellationToken).ConfigureAwait(false);

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
            await ConsoleOutput.WriteErrorAsync($"server status: unavailable ({exception.Message})").ConfigureAwait(false);
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
