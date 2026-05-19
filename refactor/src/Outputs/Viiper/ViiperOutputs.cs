using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using VirtualMouse.Forwarding;
using ViiperMouseInput = global::Viiper.Client.Devices.Mouse.MouseInput;
using ViiperXbox360Input = global::Viiper.Client.Devices.Xbox360.Xbox360Input;
using ViiperXbox360OutputReport = global::Viiper.Client.Devices.Xbox360.Xbox360Output;

namespace VirtualMouse.Outputs.Viiper;

/// <summary>Identifies VIIPER-created devices that must not feed back into input discovery.</summary>
public static class ViiperLoopbackDevices
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
            deviceName.Contains("VID_6969", StringComparison.OrdinalIgnoreCase) &&
            deviceName.Contains("PID_5050", StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>Creates VIIPER outputs for forwarding routes.</summary>
public sealed class ViiperOutputFactory(ViiperOptions options) : IControllerOutputFactory, IMouseOutputFactory
{
    /// <inheritdoc />
    public IControllerOutput Connect(ControllerId controllerId, ControllerOutput output)
    {
        return output switch
        {
            ControllerOutput.Xbox360 => ViiperXbox360Output
                .ConnectAsync(options)
                .GetAwaiter()
                .GetResult(),
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
            MouseOutput.Viiper => ViiperMouseOutput
                .ConnectAsync(options)
                .GetAwaiter()
                .GetResult(),
            MouseOutput.None => throw new NotSupportedException("None is not a VIIPER mouse output."),
            MouseOutput.Teensy => throw new NotSupportedException("Teensy output is handled by the Teensy adapter."),
            _ => throw new NotSupportedException($"VIIPER does not support {output} mouse output."),
        };
    }

    /// <summary>Removes stale VIIPER devices created by this adapter.</summary>
    public static async Task ReclaimOwnedDevicesAsync(
        ViiperOptions options,
        CancellationToken cancellationToken = default)
    {
        await ViiperXbox360Output.ReclaimOwnedDevicesAsync(options, cancellationToken)
            .ConfigureAwait(false);
        await ViiperMouseOutput.ReclaimOwnedDevicesAsync(options, cancellationToken)
            .ConfigureAwait(false);
    }
}

/// <summary>VIIPER Xbox 360 controller output.</summary>
public sealed class ViiperXbox360Output : IControllerOutput, IDisposable
{
    /// <summary>Vendor id used by owned VIIPER Xbox 360 devices.</summary>
    public const ushort OwnedVendorId = 0x045E;

    /// <summary>Product id used by owned VIIPER Xbox 360 devices.</summary>
    public const ushort OwnedProductId = 0x028E;

    private static readonly ViiperOutputDeviceDefinition DeviceDefinition = new(
        "xbox360",
        OwnedVendorId,
        OwnedProductId,
        @"Local\VirtualMouse.Refactor.Viiper.Xbox360",
        "Xbox 360");

    private readonly ViiperOutputDevice _device;

    private ViiperXbox360Output(ViiperOutputDevice device)
    {
        _device = device;
    }

    /// <summary>Gets whether the output device is connected.</summary>
    public bool IsConnected => _device.IsConnected;

    /// <summary>Creates and connects a VIIPER Xbox 360 device.</summary>
    public static Task<ViiperXbox360Output> ConnectAsync(
        ViiperOptions options,
        CancellationToken cancellationToken = default)
    {
        return ViiperOutputConnector.ConnectAsync(
            options,
            DeviceDefinition,
            device => new ViiperXbox360Output(device),
            cancellationToken);
    }

    /// <summary>Removes stale VIIPER Xbox 360 devices created by this adapter.</summary>
    public static Task ReclaimOwnedDevicesAsync(
        ViiperOptions options,
        CancellationToken cancellationToken = default)
    {
        return ViiperOutputConnector.ReclaimOwnedDevicesAsync(options, DeviceDefinition, cancellationToken);
    }

    /// <inheritdoc />
    public void Send(in ControllerState state)
    {
        Xbox360Report report = ControllerOutputMapping.ToXbox360Report(in state);
        _device
            .GetDeviceOrThrow("Xbox 360 output is not connected.")
            .SendAsync(MapReport(report))
            .GetAwaiter()
            .GetResult();
    }

    /// <inheritdoc />
    public IDisposable ListenFeedback(Action<ControllerFeedback> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        return _device.ListenOutput(
            stream =>
            {
                handler(ControllerOutputMapping.ToControllerFeedback(ReadRumble(stream)));
                return Task.CompletedTask;
            },
            "Xbox 360 output is not connected.");
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

    internal static ViiperXbox360Input MapReport(Xbox360Report report)
    {
        return new ViiperXbox360Input
        {
            Buttons = (uint)report.Buttons,
            Lt = report.LeftTrigger,
            Rt = report.RightTrigger,
            Lx = report.LeftX,
            Ly = report.LeftY,
            Rx = report.RightX,
            Ry = report.RightY,
        };
    }

    internal static Xbox360Rumble ReadRumble(Stream stream)
    {
        using BinaryReader reader = new(stream, Encoding.UTF8, leaveOpen: true);
        ViiperXbox360OutputReport report = ViiperXbox360OutputReport.Read(reader);
        return new Xbox360Rumble(report.Left, report.Right);
    }
}

/// <summary>VIIPER mouse output.</summary>
public sealed class ViiperMouseOutput : IMouseOutput, IDisposable
{
    internal const ushort OwnedVendorId = 0x6969;
    internal const ushort OwnedProductId = 0x5050;

    private static readonly ViiperOutputDeviceDefinition DeviceDefinition = new(
        "mouse",
        OwnedVendorId,
        OwnedProductId,
        @"Local\VirtualMouse.Refactor.Viiper.Mouse",
        "mouse");

    private readonly ViiperOutputDevice _device;

    private ViiperMouseOutput(ViiperOutputDevice device)
    {
        _device = device;
    }

    /// <inheritdoc />
    public bool IsConnected => _device.IsConnected;

    /// <inheritdoc />
    public bool FilterInput(in MouseInput input)
    {
        return ViiperLoopbackDevices.IsMouseDeviceName(input.DeviceName);
    }

    /// <summary>Creates and connects a VIIPER mouse device.</summary>
    public static Task<ViiperMouseOutput> ConnectAsync(
        ViiperOptions options,
        CancellationToken cancellationToken = default)
    {
        return ViiperOutputConnector.ConnectAsync(
            options,
            DeviceDefinition,
            device => new ViiperMouseOutput(device),
            cancellationToken);
    }

    /// <summary>Removes stale VIIPER mouse devices created by this adapter.</summary>
    public static Task ReclaimOwnedDevicesAsync(
        ViiperOptions options,
        CancellationToken cancellationToken = default)
    {
        return ViiperOutputConnector.ReclaimOwnedDevicesAsync(options, DeviceDefinition, cancellationToken);
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
