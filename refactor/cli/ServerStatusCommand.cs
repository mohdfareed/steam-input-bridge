using System.CommandLine;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using VirtualMouse.Hosting;
using VirtualMouse.Runtime;

namespace Refactor.Cli;

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

        using IHost app = AppSetup.Create();
        ClientService client = app.Services.GetRequiredService<ClientService>();
        await using (client.ConfigureAwait(false))
        {
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(2));

            try
            {
                await client.ConnectAsync(timeout.Token).ConfigureAwait(false);
                ServerStatus status = await client.GetStatusAsync(timeout.Token).ConfigureAwait(false);

                if (json)
                {
                    await PrintStatusReportJson(status).ConfigureAwait(false);
                }
                else
                {
                    await PrintStatusReportText(status).ConfigureAwait(false);
                }
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

    // MARK: Console Printing
    // ========================================================================

    private static async Task PrintStatusReportJson(ServerStatus status)
    {
        await Console.Out.WriteLineAsync(JsonSerializer.Serialize(status, JsonOptions)).ConfigureAwait(false);
    }

    private static Task PrintStatusReportText(ServerStatus status)
    {
        StringBuilder sb = new();

        PrintSection(sb, "Server");
        PrintItem(sb, "running", true);
        PrintItem(sb, "connectedClients", status.ConnectedClientCount);

        PrintSection(sb, "Runtime");
        PrintItem(sb, "activeClient", status.Runtime.ActiveClientId?.ToString() ?? "none");
        PrintItem(sb, "foregroundPid", status.Runtime.ForegroundProcessId);

        PrintSection(sb, "Clients");
        if (status.Runtime.Clients.Count == 0)
        {
            PrintLine(sb, "none");
        }

        foreach (ClientStatus client in status.Runtime.Clients)
        {
            PrintLine(sb, $"{client.ClientId} profile={client.ProfileId} active={client.IsActive} process={client.ClientProcessId} steamAppId={FormatAppId(client.SteamAppId)}");
            PrintLine(sb, $"receivers: {FormatList(client.ReceiverProcesses)}", 4);
            PrintLine(sb, $"observed:  {FormatProcesses(client.ObservedProcesses)}", 4);
            PrintLine(sb, $"owned:     {FormatProcesses(client.OwnedProcesses)}", 4);
            PrintLine(sb, $"blocked:   {FormatProcesses(client.BlockedProcesses)}", 4);
        }

        return Console.Out.WriteAsync(sb.ToString());
    }

    private static void PrintSection(StringBuilder sb, string title)
    {
        _ = sb.AppendLine();
        _ = sb.AppendLine(title);
        _ = sb.AppendLine(new string('-', title.Length));
    }

    private static void PrintItem(StringBuilder sb, string name, object? value)
    {
        PrintLine(sb, $"{name,-18} {value}");
    }

    private static void PrintLine(StringBuilder sb, string value, int spaces = 2)
    {
        _ = sb.Append(' ', spaces);
        _ = sb.AppendLine(value);
    }

    // MARK: Output formatting
    // ========================================================================

    private static string FormatList(IReadOnlyList<string> values)
    {
        return values.Count == 0 ? "none" : string.Join(", ", values);
    }

    private static string FormatAppId(uint? appId)
    {
        return appId.HasValue ? appId.Value.ToString(CultureInfo.InvariantCulture) : "none";
    }

    private static string FormatProcesses(IReadOnlyList<ObservedGameProcess> processes)
    {
        return processes.Count == 0
            ? "none"
            : string.Join(", ", processes.Select(process => $"{process.ProcessName}:{process.ProcessId}"));
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
