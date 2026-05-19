using System.CommandLine;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using VirtualMouse.Forwarding;
using VirtualMouse.Hosting;
using VirtualMouse.Runtime;

namespace VirtualMouse.Cli;

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
                    await PrintStatusReportText(status).ConfigureAwait(false);
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

    private static Task PrintStatusReportText(ServerStatus status)
    {
        StringBuilder sb = new();

        PrintSection(sb, "Server");
        PrintItem(sb, "running", true);
        PrintItem(sb, "connectedClients", status.ConnectedClientCount);

        PrintSection(sb, "Runtime");
        PrintItem(sb, "activeClient", status.Runtime.ActiveClientId?.ToString() ?? "none");
        PrintItem(sb, "foregroundPid", status.Runtime.ForegroundProcessId);

        PrintSection(sb, "Steam Input");
        PrintItem(sb, "forced", status.SteamInput.Forced);
        PrintItem(sb, "appId", FormatAppId(status.SteamInput.AppId));
        PrintItem(sb, "client", status.SteamInput.ClientId?.ToString() ?? "none");
        PrintItem(sb, "error", status.SteamInput.LastError ?? "none");

        PrintSection(sb, "Inputs");
        PrintItem(sb, "physicalSdl", FormatRunning(status.Inputs.PhysicalControllers.Running));
        PrintItem(sb, "physicalCount", status.Inputs.PhysicalControllers.ControllerCount);
        PrintItem(sb, "physicalError", status.Inputs.PhysicalControllers.LastError ?? "none");
        PrintItem(sb, "rawInput", FormatRunning(status.Inputs.Mouse.Running));
        PrintItem(sb, "rawInputSource", status.Inputs.Mouse.SourceConnected ? "connected" : "disconnected");
        PrintItem(sb, "rawInputError", status.Inputs.Mouse.LastError ?? "none");
        foreach (string controllerId in status.Inputs.PhysicalControllers.ControllerIds)
        {
            PrintLine(sb, $"physical {controllerId}", 4);
        }

        PrintSection(sb, "Forwarding");
        PrintItem(sb, "controllerOutput", status.Forwarding.ControllerOutputEnabled);
        PrintItem(sb, "physicalMotion", status.Forwarding.PhysicalMotionEnabled);
        PrintItem(sb, "mouseOutput", status.MouseForwarding.MouseOutputEnabled);
        PrintItem(sb, "mouseConnected", status.MouseForwarding.OutputConnected);
        PrintItem(sb, "mouseKind", status.MouseForwarding.Output);

        PrintSection(sb, "Controller Slots");
        if (status.Forwarding.Slots.Count == 0)
        {
            PrintLine(sb, "none");
        }

        foreach (ControllerSlotStatus slot in status.Forwarding.Slots)
        {
            PrintLine(sb,
                $"{FormatControllerId(slot.ControllerId)} " +
                $"output={FormatOutput(slot.OutputConnected, slot.Output)} " +
                $"steam={slot.SteamEndpointCount} " +
                $"activeSteam={slot.HasActiveSteamEndpoint} " +
                $"physical={FormatEndpoint(slot.HasPhysicalEndpoint)}");
            PrintLine(sb, $"physicalFeatures: {FormatFeatures(slot.PhysicalFeatures)}", 4);
            PrintLine(sb, $"activeSteamFeatures: {FormatFeatures(slot.ActiveSteamFeatures)}", 4);
        }

        PrintSection(sb, "Controller Pipes");
        if (status.ControllerPipes.Count == 0)
        {
            PrintLine(sb, "none");
        }

        foreach (ControllerPipeStatus pipe in status.ControllerPipes)
        {
            PrintLine(sb,
                $"{pipe.ClientId} pipe={pipe.PipeName} " +
                $"connected={pipe.Connected} controllers={pipe.Controllers.Count}");
            foreach (ClientControllerStatus controller in pipe.Controllers)
            {
                PrintLine(sb,
                    $"{controller.ControllerIndex}: {controller.Label} " +
                    $"physical={controller.PhysicalControllerId} " +
                    $"features={FormatFeatures(controller.Features)}",
                    4);
            }
        }

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

    private static string FormatRunning(bool running)
    {
        return running ? "running" : "stopped";
    }

    private static string FormatEndpoint(bool connected)
    {
        return connected ? "connected" : "missing";
    }

    private static string FormatOutput(bool connected, ControllerOutput output)
    {
        return connected ? $"{output}:connected" : $"{output}:disconnected";
    }

    private static string FormatControllerId(ControllerId controllerId)
    {
        return string.IsNullOrWhiteSpace(controllerId.DisplayName)
            ? controllerId.Value
            : $"{controllerId.DisplayName} ({controllerId.Value})";
    }

    private static string FormatFeatures(ControllerFeatures? features)
    {
        return features.HasValue && features.Value != ControllerFeatures.None
            ? features.Value.ToString()
            : "none";
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
