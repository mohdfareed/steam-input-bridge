using System;
using System.Collections.Generic;
using System.Globalization;
using SteamInputBridge.Forwarding.Controller;
using SteamInputBridge.Forwarding.Controller.Routing;
using SteamInputBridge.Forwarding.Mouse;
using SteamInputBridge.Hosting.Server.Orchestration;
using SteamInputBridge.Runtime;

namespace SteamInputBridge.App.Tray.Menu;

internal static class AppText
{
    public static string None => "None";

    public static string WaitingForStatus => "Waiting for status";

    public static string TrayStarting => "Steam Input Bridge starting";

    public static string Header(string? serverError)
    {
        return $"Server stopped: {serverError}";
    }

    public static string TrayText(ServerStatus? status, string? serverError)
    {
        return serverError is not null
            ? "Steam Input Bridge stopped"
            : status is null
            ? TrayStarting
            : $"Steam Input Bridge ({status.ConnectedClientCount} clients)";
    }

    public static string AppId(uint? appId)
    {
        return appId.HasValue ? appId.Value.ToString(CultureInfo.InvariantCulture) : None;
    }

    public static string Connected(bool connected)
    {
        return connected ? "Connected" : "Disconnected";
    }

    public static string Enabled(bool enabled)
    {
        return enabled ? "Enabled" : "Disabled";
    }

    public static string YesNo(bool value)
    {
        return value ? "Yes" : "No";
    }

    public static string Running(bool running)
    {
        return running ? "Running" : "Stopped";
    }

    public static string Active(bool active)
    {
        return active ? "Active" : "Idle";
    }

    public static string Count(int count)
    {
        return count == 0 ? None : count.ToString(CultureInfo.InvariantCulture);
    }

    public static string Error(string message)
    {
        return $"Error: {message}";
    }

    public static string Error(string label, string message)
    {
        return $"{label} error: {message}";
    }

    public static string ShortId(Guid id)
    {
        return id.ToString("N")[..8];
    }

    public static string Output(MouseOutput output)
    {
        return output switch
        {
            SteamInputBridge.Forwarding.Mouse.MouseOutput.None => None,
            SteamInputBridge.Forwarding.Mouse.MouseOutput.Viiper => "VIIPER",
            SteamInputBridge.Forwarding.Mouse.MouseOutput.Teensy => "Teensy",
            _ => output.ToString(),
        };
    }

    public static string Output(ControllerOutput output)
    {
        return output switch
        {
            ControllerOutput.None => None,
            ControllerOutput.Xbox360 => "Xbox 360",
            ControllerOutput.Ds4 => "DualShock 4",
            _ => output.ToString(),
        };
    }

    public static string MouseInput(MouseInputPumpStatus status)
    {
        return !string.IsNullOrWhiteSpace(status.LastError)
            ? "Retrying"
            : status.Running && status.SourceConnected
            ? Running(true)
            : Running(false);
    }

    public static string FormatMouseOutput(MouseBrokerStatus status)
    {
        return status.Output == SteamInputBridge.Forwarding.Mouse.MouseOutput.None
            ? Enabled(false)
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
