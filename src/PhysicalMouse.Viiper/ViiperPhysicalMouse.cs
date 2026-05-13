using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Viiper.Client;
using Viiper.Client.Devices.Mouse;
using Viiper.Client.Types;

namespace PhysicalMouse.Viiper;

/// <summary>VIIPER transport.</summary>
public sealed partial class ViiperPhysicalMouse : IPhysicalMouse, IDisposable, IAsyncDisposable
{
    private readonly ViiperClient? _client;
    private readonly ILogger? _logger;
    private ViiperDevice? _device;
    private int _isConnected;

    // MARK: Construction
    // ========================================================================

    /// <summary>Wraps an existing VIIPER device.</summary>
    /// <param name="device">Connected device stream.</param>
    /// <param name="logger">Logger for lifecycle events.</param>
    public ViiperPhysicalMouse(ViiperDevice device, ILogger? logger = null)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
        _logger = logger;
        _isConnected = 1;
        HookDisconnect(device);
    }

    private ViiperPhysicalMouse(ViiperClient client, ViiperDevice device, uint busId, string deviceId, ILogger? logger)
    {
        _isConnected = 1;
        _client = client;
        _device = device;
        _logger = logger;

        BusId = busId;
        DeviceId = deviceId;

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
        ViiperClient client = new(options.Host, options.Port, options.Password);

        try
        {
            Device createdDevice = await CreateDeviceAsync(client, options.Logger, cancellationToken).ConfigureAwait(false);

            try
            {
                return await ConnectDeviceAsync(client, createdDevice, options.Logger, cancellationToken).ConfigureAwait(false);
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
        MouseInput input = MapReport(report);
        await _device.SendAsync(input, cancellationToken).ConfigureAwait(false);
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
            if (_client is not null && BusId.HasValue && !string.IsNullOrWhiteSpace(DeviceId))
            {
                _ = await _client.BusDeviceRemoveAsync(BusId.Value, DeviceId, CancellationToken.None).ConfigureAwait(false);
                if (_logger is not null)
                {
                    Log.RemovedDevice(_logger, BusId.Value, DeviceId, null);
                }
            }
        }
        finally
        {
            await device.DisposeAsync().ConfigureAwait(false);
            _client?.Dispose();
        }
    }

    // MARK: Internal
    // ========================================================================

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

    // MARK: Private
    // ========================================================================

    private static async Task<Device> CreateDeviceAsync(
        ViiperClient client, ILogger? logger, CancellationToken cancellationToken)
    {
        uint resolvedBusId;
        BusListResponse buses = await client.BusListAsync(cancellationToken).ConfigureAwait(false);

        if (buses.Buses.Length > 0)
        {
            resolvedBusId = buses.Buses[0];
        }
        else
        {
            BusCreateResponse created = await client.BusCreateAsync(null, cancellationToken).ConfigureAwait(false);
            resolvedBusId = created.BusID;
            if (logger is not null)
            {
                Log.CreatingDevice(logger, resolvedBusId, null);
            }
        }

        return await client.BusDeviceAddAsync(
            resolvedBusId,
            new DeviceCreateRequest
            {
                Type = "mouse",
            },
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task<ViiperPhysicalMouse> ConnectDeviceAsync(
        ViiperClient client, Device createdDevice, ILogger? logger, CancellationToken cancellationToken)
    {
        ViiperDevice device = await client.ConnectDeviceAsync(
            createdDevice.BusID,
            createdDevice.DevId,
            cancellationToken).ConfigureAwait(false);

        if (logger is not null)
        {
            Log.ConnectedKnownDevice(logger, createdDevice.BusID, createdDevice.DevId, null);
        }

        return new ViiperPhysicalMouse(
            client,
            device,
            createdDevice.BusID,
            createdDevice.DevId,
            logger);
    }

    private void HookDisconnect(ViiperDevice device)
    {
        Action? onDisconnect = device.OnDisconnect;
        device.OnDisconnect = () =>
        {
            _ = Interlocked.Exchange(ref _isConnected, 0);
            if (_logger is not null && BusId.HasValue && !string.IsNullOrWhiteSpace(DeviceId))
            {
                Log.DisconnectedKnownDevice(_logger, BusId.Value, DeviceId, null);
            }

            onDisconnect?.Invoke();
        };
    }
}
