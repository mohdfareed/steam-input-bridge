using System;
using System.Threading;
using System.Threading.Tasks;
using SteamInputBridge.Inputs.Mouse;
using SteamInputBridge.Outputs.Mouse;

namespace SteamInputBridge.Outputs.Teensy;

/// <summary>Creates Teensy mouse outputs.</summary>
public sealed class TeensyMouseOutputFactory(TeensyMouseOutputService service) : IMouseOutputFactory
{
    /// <summary>Connects a Teensy mouse output.</summary>
    public ValueTask<IMouseOutput> ConnectAsync(MouseOutput output, CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        return output == MouseOutput.Teensy
            ? ValueTask.FromResult(service.CreateOutput())
            : throw new NotSupportedException($"Teensy does not support {output} mouse output.");
    }
}

internal sealed class TeensyMouseOutput(TeensyMouseOutputService service) : IMouseOutput
{
    private const string TeensyVendorName = "VID_16C0";

    public bool IsConnected => service.IsConnected;

    public ValueTask SendAsync(in MouseInput input, CancellationToken cancellationToken = default)
    {
        return IsTeensyDeviceName(input.DeviceName)
            ? ValueTask.CompletedTask
            : service.SendAsync(in input, cancellationToken);
    }

    public ValueTask ClearAsync(CancellationToken cancellationToken = default)
    {
        return service.SendReportAsync(MouseReport.Empty, cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }

    private static bool IsTeensyDeviceName(string? deviceName)
    {
        return !string.IsNullOrWhiteSpace(deviceName) &&
            (deviceName.Contains(TeensyVendorName, StringComparison.OrdinalIgnoreCase) ||
             deviceName.Contains("TEENSY", StringComparison.OrdinalIgnoreCase));
    }
}
