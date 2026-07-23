using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SteamInputBridge.Inputs.Controller;
using SteamInputBridge.Outputs.Controller;
using SteamInputBridge.Outputs.Viiper.Shared;
using ViiperXbox360Input = global::Viiper.Client.Devices.Xbox360.Xbox360Input;
using ViiperXbox360OutputReport = global::Viiper.Client.Devices.Xbox360.Xbox360Output;

namespace SteamInputBridge.Outputs.Viiper.Controller;

/// <summary>VIIPER Xbox 360 controller output.</summary>
public sealed class ViiperXbox360ControllerOutput : IControllerOutput
{
    internal const ushort OwnedVendorId = 0x045E;
    internal const string OwnedVendorName = "VID_045E";

    internal const ushort OwnedProductId = 0x028E;
    internal const string OwnedProductName = "PID_028E";

    private static readonly ViiperDeviceDefinition DeviceDefinition = new(
        "xbox360",
        OwnedVendorId,
        OwnedProductId,
        ViiperDeviceDefinition.FormatOwnedDisplayName("Virtual Controller"));

    private readonly ViiperCreatedDevice _device;
    private readonly IDisposable _feedback;

    private ViiperXbox360ControllerOutput(ViiperCreatedDevice device)
    {
        _device = device;
        _feedback = _device.ListenOutput(
                stream =>
                {
                    RumbleReceived?.Invoke(
                        this,
                        new ControllerRumbleEventArgs(ControllerOutputMapping.ToControllerRumble(ReadRumble(stream))));
                    return Task.CompletedTask;
                },
                "Xbox 360 output is not connected.");
    }

    // MARK: Publics
    // ========================================================================

    /// <inheritdoc />
    public event EventHandler<ControllerRumbleEventArgs>? RumbleReceived;

    /// <inheritdoc />
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
    public async ValueTask DisposeAsync()
    {
        _feedback.Dispose();
        await _device.DisposeAsync().ConfigureAwait(false);
    }

    // MARK: Internals
    // ========================================================================

    internal static Task<ViiperXbox360ControllerOutput> ConnectAsync(
        ViiperOptions options,
        string displayName,
        CancellationToken cancellationToken = default)
    {
        string label = ViiperDeviceDefinition.FormatOwnedDisplayName(displayName);
        return ViiperDeviceLifecycle.ConnectAsync(
            options,
            DeviceDefinition with { DisplayName = label },
            device => new ViiperXbox360ControllerOutput(device),
            cancellationToken);
    }

    internal static Task ReclaimDevicesAsync(
        ViiperOptions options,
        CancellationToken cancellationToken = default)
    {
        return ViiperDeviceLifecycle.ReclaimDevicesAsync(options, DeviceDefinition, cancellationToken);
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

    private static Xbox360Rumble ReadRumble(Stream stream)
    {
        using BinaryReader reader = new(stream, Encoding.UTF8, leaveOpen: true);
        ViiperXbox360OutputReport report = ViiperXbox360OutputReport.Read(reader);
        return new Xbox360Rumble(report.Left, report.Right);
    }
}
