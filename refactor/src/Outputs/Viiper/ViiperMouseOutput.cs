using System;
using System.Threading;
using System.Threading.Tasks;
using VirtualMouse.Forwarding;
using ViiperMouseInput = global::Viiper.Client.Devices.Mouse.MouseInput;

namespace VirtualMouse.Outputs.Viiper;

/// <summary>VIIPER mouse output.</summary>
public sealed class ViiperMouseOutput : IMouseOutput, IDisposable
{
    internal const ushort OwnedVendorId = 0x6969;
    internal const string OwnedVendorName = "VID_6969";

    internal const ushort OwnedProductId = 0x5050;
    internal const string OwnedProductName = "PID_5050";

    private static readonly ViiperOutputDeviceDefinition DeviceDefinition = new(
        "mouse",
        OwnedVendorId,
        OwnedProductId,
        "Virtual Mouse - Steam Input");

    private readonly ViiperOutputDevice _device;

    private ViiperMouseOutput(ViiperOutputDevice device)
    {
        _device = device;
    }

    // MARK: Publics
    // ========================================================================

    /// <inheritdoc />
    public bool IsConnected => _device.IsConnected;

    /// <inheritdoc />
    public bool FilterInput(in MouseInput input)
    {
        return ViiperDevices.IsMouseDeviceName(input.DeviceName);
    }

    /// <inheritdoc />
    public ValueTask SendAsync(MouseReport report, CancellationToken cancellationToken = default)
    {
        return new ValueTask(_device
            .GetDeviceOrThrow("Mouse output is not connected.")
            .SendAsync(MapReport(report), cancellationToken));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        return _device.DisposeAsync();
    }

    // MARK: Internals
    // ========================================================================

    internal static Task<ViiperMouseOutput> ConnectAsync(ViiperOptions options, CancellationToken cancellationToken = default)
    {
        return ViiperOutputConnector.ConnectAsync(
            options,
            DeviceDefinition,
            device => new ViiperMouseOutput(device),
            cancellationToken);
    }

    internal static Task ReclaimDevicesAsync(ViiperOptions options, CancellationToken cancellationToken = default)
    {
        return ViiperOutputConnector.ReclaimDevicesAsync(options, DeviceDefinition, cancellationToken);
    }

    internal static ViiperMouseInput MapReport(MouseReport report)
    {
        return new ViiperMouseInput
        {
            Buttons = checked((byte)report.Buttons),
            Dx = checked((short)report.DeltaX),
            Dy = checked((short)report.DeltaY),
            Wheel = checked((short)report.WheelDelta),
            Pan = 0,
        };
    }
}
