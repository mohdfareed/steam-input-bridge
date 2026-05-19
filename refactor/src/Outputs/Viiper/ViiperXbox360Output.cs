using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using VirtualMouse.Forwarding;
using ViiperXbox360Input = global::Viiper.Client.Devices.Xbox360.Xbox360Input;
using ViiperXbox360OutputReport = global::Viiper.Client.Devices.Xbox360.Xbox360Output;

namespace VirtualMouse.Outputs.Viiper;

/// <summary>VIIPER Xbox 360 controller output.</summary>
public sealed class ViiperXbox360Output : IControllerOutput, IDisposable
{
    internal const ushort OwnedVendorId = 0x045E;
    internal const string OwnedVendorName = "VID_045E";

    internal const ushort OwnedProductId = 0x028E;
    internal const string OwnedProductName = "PID_028E";

    private static readonly ViiperOutputDeviceDefinition DeviceDefinition = new(
        "xbox360",
        OwnedVendorId,
        OwnedProductId,
        "Virtual Controller");

    private readonly ViiperOutputDevice _device;

    private ViiperXbox360Output(ViiperOutputDevice device)
    {
        _device = device;
    }

    // MARK: Publics
    // ========================================================================

    /// <summary>Gets whether the output device is connected.</summary>
    public bool IsConnected => _device.IsConnected;

    /// <inheritdoc />
    public void Send(in ControllerState state)
    {
        Xbox360Report report = ControllerOutputMapping.ToXbox360Report(in state);
        _device.GetDeviceOrThrow("Xbox 360 output is not connected.")
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

    // MARK: Internals
    // ========================================================================

    internal static Task<ViiperXbox360Output> ConnectAsync(
        ViiperOptions options,
        ControllerId controllerId,
        CancellationToken cancellationToken = default)
    {
        string? label = string.IsNullOrWhiteSpace(controllerId.DisplayName)
            ? DeviceDefinition.DisplayName
            : $"Virtual Mouse - {controllerId.DisplayName}";

        return ViiperOutputConnector.ConnectAsync(
            options,
            DeviceDefinition with { DisplayName = label },
            device => new ViiperXbox360Output(device),
            cancellationToken);
    }

    internal static Task ReclaimDevicesAsync(
        ViiperOptions options,
        CancellationToken cancellationToken = default)
    {
        return ViiperOutputConnector.ReclaimDevicesAsync(options, DeviceDefinition, cancellationToken);
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
