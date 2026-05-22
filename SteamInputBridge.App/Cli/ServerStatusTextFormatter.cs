using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using SteamInputBridge.Forwarding.Controller;
using SteamInputBridge.Forwarding.Controller.Routing;
using SteamInputBridge.Hosting.Server.Orchestration;
using SteamInputBridge.Runtime;

namespace SteamInputBridge.App.Cli;

internal static class ServerStatusTextFormatter
{
    public static string Format(ServerStatus status)
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
        PrintItem(sb, "rawInput", FormatRunning(status.Inputs.Mouse.Running));
        PrintItem(sb, "rawInputSource", status.Inputs.Mouse.SourceConnected ? "connected" : "disconnected");
        PrintItem(sb, "rawInputError", status.Inputs.Mouse.LastError ?? "none");

        PrintSection(sb, "Forwarding");
        PrintItem(sb, "controllerOutput", status.Forwarding.ControllerOutputEnabled);
        PrintItem(sb, "motion", status.Forwarding.PhysicalMotionEnabled);
        PrintItem(sb, "mouseOutput", status.MouseForwarding.MouseOutputEnabled);
        PrintItem(sb, "pointer", status.MouseForwarding.PointerOutputEnabled);
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
                $"clients={slot.ClientEndpointCount} " +
                $"activeClient={slot.HasActiveClientEndpoint} " +
                $"physical={FormatEndpoint(slot.HasPhysicalEndpoint)}");
            PrintLine(sb, $"physicalFeatures: {FormatFeatures(slot.PhysicalFeatures)}", 4);
            PrintLine(sb, $"activeClientFeatures: {FormatFeatures(slot.ActiveClientFeatures)}", 4);
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
                    $"features={FormatFeatures(controller.Features)} " +
                    $"frames={controller.InputFrameCount}",
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

        return sb.ToString();
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
}
