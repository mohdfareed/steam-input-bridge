using System;
using System.Threading;
using System.Threading.Tasks;
using Viiper.Client;
using Viiper.Client.Devices.Mouse;
using Viiper.Client.Types;

namespace PhysicalMouse.Viiper;

/// <summary>VIIPER connection options.</summary>
public sealed class ViiperOptions
{
    /// <summary>Host name or IP address.</summary>
    public string Host { get; init; } = "127.0.0.1";

    /// <summary>TCP port.</summary>
    public int Port { get; init; } = 3242;

    /// <summary>Server password.</summary>
    public string Password { get; init; } = string.Empty;

    /// <summary>Bus to use, if known.</summary>
    public uint? BusId { get; init; }

    /// <summary>Device to use, if known.</summary>
    public string? DeviceId { get; init; }

    /// <summary>Gets whether a created device should be removed on dispose.</summary>
    public bool RemoveCreatedDeviceOnDispose { get; init; }
}

/// <summary>VIIPER transport.</summary>
public sealed class ViiperPhysicalMouse : IPhysicalMouse, IDisposable, IAsyncDisposable
{
    private readonly ViiperClient? _client;
    private readonly bool _removeCreatedDeviceOnDispose;
    private ViiperDevice? _device;
    private int _isConnected;

    // MARK: Construction
    // ========================================================================

    /// <summary>Wraps an existing VIIPER device.</summary>
    /// <param name="device">Connected device stream.</param>
    public ViiperPhysicalMouse(ViiperDevice device)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
        _isConnected = 1;
        HookDisconnect(device);
    }

    private ViiperPhysicalMouse(
        ViiperClient client,
        ViiperDevice device,
        uint busId,
        string deviceId,
        bool removeCreatedDeviceOnDispose)
    {
        _client = client;
        _device = device;
        BusId = busId;
        DeviceId = deviceId;
        _removeCreatedDeviceOnDispose = removeCreatedDeviceOnDispose;
        _isConnected = 1;
        HookDisconnect(device);
    }

    // MARK: Implementation
    // ========================================================================

    /// <inheritdoc />
    public bool IsConnected => Volatile.Read(ref _isConnected) != 0;

    /// <summary>Gets the connected bus ID, if known.</summary>
    public uint? BusId { get; }

    /// <summary>Gets the connected device ID, if known.</summary>
    public string? DeviceId { get; }

    /// <summary>
    /// Creates and connects a VIIPER mouse device.
    /// </summary>
    /// <param name="options">Connection options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Connected transport.</returns>
    public static async Task<ViiperPhysicalMouse> ConnectAsync(
        ViiperOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ValidateOptions(options);

        ViiperClient client = new(options.Host, options.Port, options.Password);

        try
        {
            // reconnect to a known device first when sticky IDs are available
            if (options.BusId.HasValue && !string.IsNullOrWhiteSpace(options.DeviceId))
            {
                ViiperDevice device = await client.ConnectDeviceAsync(
                    options.BusId.Value,
                    options.DeviceId,
                    cancellationToken).ConfigureAwait(false);

                return new ViiperPhysicalMouse(
                    client,
                    device,
                    options.BusId.Value,
                    options.DeviceId,
                    removeCreatedDeviceOnDispose: false);
            }

            // find a reusable device or create one when needed
            uint resolvedBusId = await ResolveBusIdAsync(client, options.BusId, cancellationToken).ConfigureAwait(false);
            Device? existingDevice = await FindReusableDeviceAsync(client, resolvedBusId, cancellationToken).ConfigureAwait(false);
            if (existingDevice is not null)
            {
                ViiperDevice device = await client.ConnectDeviceAsync(
                    existingDevice.BusID,
                    existingDevice.DevId,
                    cancellationToken).ConfigureAwait(false);

                return new ViiperPhysicalMouse(
                    client,
                    device,
                    existingDevice.BusID,
                    existingDevice.DevId,
                    removeCreatedDeviceOnDispose: false);
            }

            Device createdDevice = await client.BusDeviceAddAsync(
                resolvedBusId,
                new DeviceCreateRequest
                {
                    Type = "mouse",
                },
                cancellationToken).ConfigureAwait(false);

            try
            {
                ViiperDevice device = await client.ConnectDeviceAsync(
                    createdDevice.BusID,
                    createdDevice.DevId,
                    cancellationToken).ConfigureAwait(false);

                return new ViiperPhysicalMouse(
                    client,
                    device,
                    createdDevice.BusID,
                    createdDevice.DevId,
                    options.RemoveCreatedDeviceOnDispose);
            }
            catch
            {
                _ = await client.BusDeviceRemoveAsync(createdDevice.BusID, createdDevice.DevId, cancellationToken).ConfigureAwait(false);
                throw;
            }
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    /// <inheritdoc />
    public async ValueTask SendAsync(MouseReport report, CancellationToken cancellationToken = default)
    {
        if (!IsConnected || _device is null)
        {
            throw new InvalidOperationException("Mouse is not connected.");
        }

        // map and forward without extra processing
        await _device.SendAsync(MapReport(report), cancellationToken).ConfigureAwait(false);
    }

    // MARK: Disposal
    // ========================================================================

    /// <inheritdoc />
    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        // disconnect once and release the owned VIIPER objects
        _ = Interlocked.Exchange(ref _isConnected, 0);
        ViiperDevice? device = Interlocked.Exchange(ref _device, null);
        if (device is null)
        {
            return;
        }

        try
        {
            if (_removeCreatedDeviceOnDispose && _client is not null && BusId.HasValue && !string.IsNullOrWhiteSpace(DeviceId))
            {
                _ = await _client.BusDeviceRemoveAsync(BusId.Value, DeviceId, CancellationToken.None).ConfigureAwait(false);
            }
        }
        finally
        {
            await device.DisposeAsync().ConfigureAwait(false);
            _client?.Dispose();
        }
    }

    // MARK: Helpers
    // ========================================================================

    private static void ValidateOptions(ViiperOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.DeviceId) && !options.BusId.HasValue)
        {
            throw new ArgumentException("DeviceId requires BusId.", nameof(options));
        }
    }

    private static async Task<uint> ResolveBusIdAsync(
        ViiperClient client,
        uint? busId,
        CancellationToken cancellationToken)
    {
        if (busId.HasValue)
        {
            return busId.Value;
        }

        BusListResponse buses = await client.BusListAsync(cancellationToken).ConfigureAwait(false);
        if (buses.Buses.Length > 0)
        {
            return buses.Buses[0];
        }

        BusCreateResponse created = await client.BusCreateAsync(null, cancellationToken).ConfigureAwait(false);
        return created.BusID;
    }

    private static async Task<Device?> FindReusableDeviceAsync(
        ViiperClient client,
        uint busId,
        CancellationToken cancellationToken)
    {
        DevicesListResponse devices = await client.BusDevicesListAsync(busId, cancellationToken).ConfigureAwait(false);
        return SelectReusableDevice(devices.Devices);
    }

    internal static MouseInput MapReport(MouseReport report)
    {
        // keep the mapping direct and fail on unsupported ranges
        return new MouseInput
        {
            Buttons = checked((byte)report.Buttons),
            Dx = checked((short)report.DeltaX),
            Dy = checked((short)report.DeltaY),
            Wheel = checked((short)report.WheelDelta),
            Pan = 0,
        };
    }

    internal static Device? SelectReusableDevice(Device[] devices)
    {
        Device[] mouseDevices = Array.FindAll(devices, static device => string.Equals(device.Type, "mouse", StringComparison.Ordinal));
        return mouseDevices.Length == 1 ? mouseDevices[0] : null;
    }

    private void HookDisconnect(ViiperDevice device)
    {
        Action? onDisconnect = device.OnDisconnect;
        device.OnDisconnect = () =>
        {
            _ = Interlocked.Exchange(ref _isConnected, 0);
            onDisconnect?.Invoke();
        };
    }
}
