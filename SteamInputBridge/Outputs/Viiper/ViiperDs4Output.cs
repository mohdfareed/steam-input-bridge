using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SteamInputBridge.Forwarding.Controller;
using SteamInputBridge.Outputs.Viiper.Shared;
using ViiperDs4Input = global::Viiper.Client.Devices.Dualshock4.Dualshock4Input;
using ViiperDs4OutputReport = global::Viiper.Client.Devices.Dualshock4.Dualshock4Output;

namespace SteamInputBridge.Outputs.Viiper;

/// <summary>VIIPER DualShock 4 controller output.</summary>
public sealed class ViiperDs4Output : IControllerOutput, IDisposable
{
    internal const ushort OwnedVendorId = 0x054C;
    internal const string OwnedVendorName = "VID_054C";

    internal const ushort OwnedProductId = 0x05C4;
    internal const string OwnedProductName = "PID_05C4";

    private static readonly ViiperDeviceDefinition DeviceDefinition = new(
        "dualshock4",
        OwnedVendorId,
        OwnedProductId,
        ViiperDeviceDefinition.FormatOwnedDisplayName("Virtual Controller"));

    private readonly ViiperCreatedDevice _device;

    private ViiperDs4Output(ViiperCreatedDevice device)
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
        Ds4Report report = ControllerOutputMapping.ToDs4Report(in state);
        _device.GetDeviceOrThrow("DualShock 4 output is not connected.")
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
                handler(ControllerOutputMapping.ToControllerFeedback(ReadFeedback(stream)));
                return Task.CompletedTask;
            },
            "DualShock 4 output is not connected.");
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

    internal static Task<ViiperDs4Output> ConnectAsync(
        ViiperOptions options,
        ControllerId controllerId,
        CancellationToken cancellationToken = default)
    {
        string label = ViiperDeviceDefinition.FormatOwnedDisplayName(
            string.IsNullOrWhiteSpace(controllerId.DisplayName)
                ? "Virtual Controller"
                : controllerId.DisplayName);

        return ViiperDeviceLifecycle.ConnectAsync(
            options,
            DeviceDefinition with { DisplayName = label },
            device => new ViiperDs4Output(device),
            cancellationToken);
    }

    internal static Task ReclaimDevicesAsync(
        ViiperOptions options,
        CancellationToken cancellationToken = default)
    {
        return ViiperDeviceLifecycle.ReclaimDevicesAsync(options, DeviceDefinition, cancellationToken);
    }

    internal static ViiperDs4Input MapReport(Ds4Report report)
    {
        return new ViiperDs4Input
        {
            Sticklx = report.LeftX,
            Stickly = report.LeftY,
            Stickrx = report.RightX,
            Stickry = report.RightY,
            Buttons = (ushort)report.Buttons,
            Dpad = (byte)report.DPad,
            Triggerl2 = report.LeftTrigger,
            Triggerr2 = report.RightTrigger,
            Touch1x = report.Touch1X,
            Touch1y = report.Touch1Y,
            Touch1active = report.Touch1Active ? (byte)1 : (byte)0,
            Touch2x = report.Touch2X,
            Touch2y = report.Touch2Y,
            Touch2active = report.Touch2Active ? (byte)1 : (byte)0,
            Gyrox = report.GyroX,
            Gyroy = report.GyroY,
            Gyroz = report.GyroZ,
            Accelx = report.AccelX,
            Accely = report.AccelY,
            Accelz = report.AccelZ,
        };
    }

    internal static Ds4Feedback ReadFeedback(Stream stream)
    {
        using BinaryReader reader = new(stream, Encoding.UTF8, leaveOpen: true);
        ViiperDs4OutputReport report = ViiperDs4OutputReport.Read(reader);
        return new Ds4Feedback(
            report.Rumblesmall,
            report.Rumblelarge,
            report.Ledred,
            report.Ledgreen,
            report.Ledblue,
            report.Flashon,
            report.Flashoff);
    }
}
