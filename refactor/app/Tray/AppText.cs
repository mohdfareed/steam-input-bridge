using System.Globalization;
using VirtualMouse.Forwarding;
using VirtualMouse.Hosting;
using VirtualMouse.Runtime;

namespace VirtualMouse.Tray;

internal static class AppText
{
    public static string TrayStarting => "Virtual Mouse Server starting";

    public static string Header(string? serverError)
    {
        return serverError is null
            ? "Server running"
            : $"Server stopped: {serverError}";
    }

    public static string TrayText(ServerStatus? status, string? serverError)
    {
        return serverError is not null
            ? "Virtual Mouse Server stopped"
            : status is null
            ? TrayStarting
            : $"Virtual Mouse Server ({status.ConnectedClientCount} clients)";
    }

    public static string Active(bool active)
    {
        return active ? "[active]" : "[idle]";
    }

    public static string AppId(uint? appId)
    {
        return appId.HasValue ? appId.Value.ToString(CultureInfo.InvariantCulture) : "none";
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
            VirtualMouse.Forwarding.MouseOutput.None => "none",
            VirtualMouse.Forwarding.MouseOutput.Viiper => "viiper",
            VirtualMouse.Forwarding.MouseOutput.Teensy => "teensy",
            _ => output.ToString(),
        };
    }

    public static string Output(ControllerOutput output)
    {
        return output switch
        {
            ControllerOutput.None => "none",
            ControllerOutput.Xbox360 => "xbox360",
            ControllerOutput.Ds4 => "ds4",
            _ => output.ToString(),
        };
    }

    public static string PhysicalSdl(PhysicalControllerPumpStatus status)
    {
        return !string.IsNullOrWhiteSpace(status.LastError)
            ? status.ControllerCount == 0
                ? "physical SDL: retrying"
                : $"physical SDL: retrying ({status.ControllerCount})"
            : status.ControllerCount == 0
            ? "physical SDL: no controllers"
            : $"physical SDL: running ({status.ControllerCount})";
    }

    public static string FormatMouseOutput(MouseBrokerStatus status)
    {
        return status.Output == VirtualMouse.Forwarding.MouseOutput.None
            ? "mouse output: disabled"
            : $"mouse output: {Output(status.Output)} {Connected(status.OutputConnected)}";
    }

    public static string Processes(IReadOnlyList<ObservedGameProcess> processes)
    {
        if (processes.Count == 0)
        {
            return "none";
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
