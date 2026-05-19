using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using VirtualMouse.Forwarding;

namespace VirtualMouse.Outputs.Viiper;

/// <summary>VIIPER connection options.</summary>
public sealed class ViiperOptions
{
    /// <summary>Host name or IP address.</summary>
    public string Host { get; init; } = "127.0.0.1";

    /// <summary>TCP port.</summary>
    public int Port { get; init; } = 3242;

    /// <summary>Server password.</summary>
    public string Password { get; init; } = string.Empty;

    /// <summary>Logger for transport lifecycle events.</summary>
    public ILogger? Logger { get; init; }
}

/// <summary>Identifies VIIPER-created devices that must not feed back into input discovery.</summary>
public static class ViiperDevices
{
    /// <summary>Returns whether the USB id is a VIIPER virtual controller owned by this app.</summary>
    public static bool IsController(ushort vendorId, ushort productId)
    {
        return vendorId == ViiperXbox360Output.OwnedVendorId &&
            productId == ViiperXbox360Output.OwnedProductId;
    }

    /// <summary>Returns whether a Raw Input device name is the owned VIIPER mouse.</summary>
    public static bool IsMouseDeviceName(string? deviceName)
    {
        return !string.IsNullOrWhiteSpace(deviceName) &&
            deviceName.Contains(ViiperMouseOutput.OwnedVendorName, StringComparison.OrdinalIgnoreCase) &&
            deviceName.Contains(ViiperMouseOutput.OwnedProductName, StringComparison.OrdinalIgnoreCase);
    }
}

// MARK: Output Factory
// ============================================================================

/// <summary>Creates VIIPER outputs for forwarding routes.</summary>
public sealed class ViiperOutputFactory(ViiperOptions options) : IControllerOutputFactory, IMouseOutputFactory
{
    /// <inheritdoc />
    public IControllerOutput Connect(ControllerId controllerId, ControllerOutput output)
    {
        return output switch
        {
            ControllerOutput.Xbox360 => ViiperXbox360Output.ConnectAsync(options, controllerId).GetAwaiter().GetResult(),
            ControllerOutput.None => throw new NotSupportedException("None is not a VIIPER controller output."),
            ControllerOutput.Ds4 => throw new NotSupportedException("VIIPER DS4 output is not implemented yet."),
            _ => throw new NotSupportedException($"VIIPER does not support {output} controller output yet."),
        };
    }

    /// <inheritdoc />
    public IMouseOutput Connect(MouseOutput output)
    {
        return output switch
        {
            MouseOutput.Viiper => ViiperMouseOutput.ConnectAsync(options).GetAwaiter().GetResult(),
            MouseOutput.None => throw new NotSupportedException("None is not a VIIPER mouse output."),
            MouseOutput.Teensy => throw new NotSupportedException("Teensy output is handled by the Teensy adapter."),
            _ => throw new NotSupportedException($"VIIPER does not support {output} mouse output."),
        };
    }

    /// <summary>Removes stale VIIPER devices created by this adapter.</summary>
    public Task ReclaimDevicesAsync(CancellationToken cancellationToken = default)
    {
        return ReclaimDevicesAsync(options, cancellationToken);
    }

    /// <summary>Removes stale VIIPER devices created by this adapter.</summary>
    public static async Task ReclaimDevicesAsync(ViiperOptions options, CancellationToken cancellationToken = default)
    {
        await ViiperXbox360Output.ReclaimDevicesAsync(options, cancellationToken)
            .ConfigureAwait(false);

        await ViiperMouseOutput.ReclaimDevicesAsync(options, cancellationToken)
            .ConfigureAwait(false);
    }
}
