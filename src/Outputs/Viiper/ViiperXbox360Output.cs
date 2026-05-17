using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using global::Viiper.Client;
using global::Viiper.Client.Types;
using ViiperXbox360Input = global::Viiper.Client.Devices.Xbox360.Xbox360Input;
using ViiperXbox360OutputReport = global::Viiper.Client.Devices.Xbox360.Xbox360Output;

namespace Outputs.Viiper;

/// <summary>VIIPER Xbox 360 output.</summary>
public sealed class ViiperXbox360Output : IXbox360Output, IXbox360FeedbackSource, IDisposable, IAsyncDisposable
{
    internal const ushort OwnedVendorId = 0x045E;
    internal const ushort OwnedProductId = 0x028E;
    internal const string OwnershipMutexName = @"Local\Outputs.Viiper.Xbox360";

    private static readonly ViiperOutputDeviceDefinition DeviceDefinition = new(
        "xbox360",
        OwnedVendorId,
        OwnedProductId,
        OwnershipMutexName,
        "Xbox 360");

    private readonly ViiperOutputSession _session;

    // MARK: Construction
    // ========================================================================

    /// <summary>Wraps an existing VIIPER device.</summary>
    /// <param name="device">Connected device stream.</param>
    public ViiperXbox360Output(ViiperDevice device)
    {
        _session = new ViiperOutputSession(device);
    }

    private ViiperXbox360Output(ViiperOutputSession session)
    {
        _session = session;
    }

    // MARK: Properties
    // ========================================================================

    /// <inheritdoc />
    public bool IsConnected => _session.IsConnected;

    /// <summary>Gets the connected bus ID, if known.</summary>
    public uint? BusId => _session.BusId;

    /// <summary>Gets the connected device ID, if known.</summary>
    public string? DeviceId => _session.DeviceId;

    // MARK: Connection
    // ========================================================================

    /// <summary>Creates and connects a VIIPER Xbox 360 device.</summary>
    /// <param name="options">Connection options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static Task<ViiperXbox360Output> ConnectAsync(
        ViiperOptions options,
        CancellationToken cancellationToken = default)
    {
        return ViiperOutputConnector.ConnectAsync(
            options,
            DeviceDefinition,
            session => new ViiperXbox360Output(session),
            cancellationToken);
    }

    /// <summary>Removes stale VIIPER Xbox 360 devices created by this app.</summary>
    public static Task ReclaimOwnedDevicesAsync(
        ViiperOptions options,
        CancellationToken cancellationToken = default)
    {
        return ViiperOutputConnector.ReclaimOwnedDevicesAsync(options, DeviceDefinition, cancellationToken);
    }

    // MARK: Output
    // ========================================================================

    /// <inheritdoc />
    public ValueTask SendAsync(Xbox360Report report, CancellationToken cancellationToken = default)
    {
        return new ValueTask(_session
            .GetDeviceOrThrow("Xbox 360 output is not connected.")
            .SendAsync(MapReport(report), cancellationToken));
    }

    /// <inheritdoc />
    public IDisposable ListenRumble(Xbox360RumbleHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        return _session.ListenOutput(HandleOutputAsync, "Xbox 360 output is not connected.");

        async Task HandleOutputAsync(Stream stream)
        {
            Xbox360Rumble rumble = ReadRumble(stream);
            await handler(rumble).ConfigureAwait(false);
        }
    }

    // MARK: Disposal
    // ========================================================================

    /// <inheritdoc />
    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        return _session.DisposeAsync();
    }

    // MARK: Internal
    // ========================================================================

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

    internal static Xbox360Rumble MapRumble(ViiperXbox360OutputReport output)
    {
        return new Xbox360Rumble(output.Left, output.Right);
    }

    internal static Xbox360Rumble ReadRumble(Stream stream)
    {
        using BinaryReader reader = new(stream, Encoding.UTF8, leaveOpen: true);
        return MapRumble(ViiperXbox360OutputReport.Read(reader));
    }

    internal static bool IsOwnedDevice(Device device)
    {
        return DeviceDefinition.IsOwnedDevice(device);
    }

    internal static bool IsOwnedDeviceName(string? deviceName)
    {
        return DeviceDefinition.IsOwnedViiperDeviceName(deviceName);
    }

    internal static bool IsOwnedSdlDevice(string name, string? path)
    {
        return IsOwnedDeviceName(name) || IsOwnedDeviceName(path);
    }

    internal static string FormatUsbId(ushort value)
    {
        return ViiperOutputDeviceDefinition.FormatUsbId(value);
    }

    internal static ViiperOutputOwnership? TryAcquireOwnership(string name)
    {
        return ViiperOutputOwnership.TryAcquire(name);
    }
}
