using System.Collections.Generic;
using System.Globalization;
using SteamInputBridge.Forwarding.Controller;
using SteamInputBridge.Forwarding.Mouse;
using SteamInputBridge.Hosting.Server;
using SteamInputBridge.Runtime;

namespace SteamInputBridge.App.Tray;

internal static class AppText
{
    public static string None => "none";

    public static string TrayStarting => "Steam Input Bridge Server starting";

    public static string Header(string? serverError)
    {
        return $"Server stopped: {serverError}";
    }

    public static string TrayText(ServerStatus? status, string? serverError)
    {
        return serverError is not null
            ? "Steam Input Bridge Server stopped"
            : status is null
            ? TrayStarting
            : $"Steam Input Bridge Server ({status.ConnectedClientCount} clients)";
    }

    public static string AppId(uint? appId)
    {
        return appId.HasValue ? appId.Value.ToString(CultureInfo.InvariantCulture) : None;
    }

    public static string Connected(bool connected)
    {
        return connected ? "connected" : "disconnected";
    }

    public static string Enabled(bool enabled)
    {
        return enabled ? "enabled" : "disabled";
    }

    public static string YesNo(bool value)
    {
        return value ? "yes" : "no";
    }

    public static string Running(bool running)
    {
        return running ? "running" : "stopped";
    }

    public static string Output(MouseOutput output)
    {
        return output switch
        {
            SteamInputBridge.Forwarding.Mouse.MouseOutput.None => None,
            SteamInputBridge.Forwarding.Mouse.MouseOutput.Viiper => "viiper",
            SteamInputBridge.Forwarding.Mouse.MouseOutput.Teensy => "teensy",
            _ => output.ToString(),
        };
    }

    public static string Output(ControllerOutput output)
    {
        return output switch
        {
            ControllerOutput.None => None,
            ControllerOutput.Xbox360 => "xbox360",
            ControllerOutput.Ds4 => "ds4",
            _ => output.ToString(),
        };
    }

    public static string PhysicalSdl(PhysicalControllerPumpStatus status)
    {
        return !string.IsNullOrWhiteSpace(status.LastError)
            ? status.ControllerCount == 0
                ? "retrying"
                : $"retrying ({status.ControllerCount})"
            : status.ControllerCount == 0
            ? None
            : $"{status.ControllerCount}";
    }

    public static string MouseInput(MouseInputPumpStatus status)
    {
        return !string.IsNullOrWhiteSpace(status.LastError)
            ? "retrying"
            : status.Running && status.SourceConnected
            ? "running"
            : "stopped";
    }

    public static string FormatMouseOutput(MouseBrokerStatus status)
    {
        return status.Output == SteamInputBridge.Forwarding.Mouse.MouseOutput.None
            ? "disabled"
            : $"{Output(status.Output)} {Connected(status.OutputConnected)}";
    }

    public static string Processes(IReadOnlyList<ObservedGameProcess> processes)
    {
        if (processes.Count == 0)
        {
            return None;
        }

        List<string> values = [];
        foreach (ObservedGameProcess process in processes)
        {
            values.Add($"{process.ProcessName}:{process.ProcessId}");
        }

        return string.Join(", ", values);
    }

    public static string ControllerSlot(ControllerSlotStatus slot)
    {
        string name = string.IsNullOrWhiteSpace(slot.ControllerId.DisplayName)
            ? "Unknown Controller"
            : slot.ControllerId.DisplayName;
        return $"{name}: {Output(slot.Output)} {Connected(slot.OutputConnected)}";
    }
}
