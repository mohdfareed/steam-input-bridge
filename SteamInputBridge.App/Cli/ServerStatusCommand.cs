using System;
using System.CommandLine;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SteamInputBridge.Hosting.Client.Connection;
using SteamInputBridge.Hosting.Server.Orchestration;
using AppHostSetup = SteamInputBridge.App.AppSetup;

namespace SteamInputBridge.App.Cli;

internal static class ServerStatusCommand
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    public static Command Create()
    {
        Command status = new("status", "Print server status.");
        status.Options.Add(new Option<bool>("--json")
        {
            Description = "Print status as JSON.",
        });

        status.SetAction(RunAsync);
        return status;
    }

    private static async Task RunAsync(ParseResult parseResult, CancellationToken cancellationToken)
    {
        bool json = parseResult.GetValue<bool>("--json");

        using IHost app = AppHostSetup.CreateCli();
        ClientService client = app.Services.GetRequiredService<ClientService>();
        await using (client.ConfigureAwait(false))
        {
            try
            {
                await client.ConnectAsync(cancellationToken).ConfigureAwait(false);
                ServerStatus status = await client.GetStatusAsync(cancellationToken).ConfigureAwait(false);

                if (json)
                {
                    await PrintStatusReportJson(status).ConfigureAwait(false);
                }
                else
                {
                    await Console.Out.WriteAsync(ServerStatusTextFormatter.Format(status)).ConfigureAwait(false);
                }
            }
            catch (IOException exception)
            {
                await Console.Error.WriteLineAsync($"server status: unavailable ({exception.Message})")
                    .ConfigureAwait(false);
                Environment.ExitCode = 1;
            }
        }
    }

    // MARK: Console Printing
    // ========================================================================

    private static async Task PrintStatusReportJson(ServerStatus status)
    {
        await Console.Out.WriteLineAsync(JsonSerializer.Serialize(status, JsonOptions)).ConfigureAwait(false);
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        JsonSerializerOptions options = new()
        {
            WriteIndented = true,
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
