using System;
using System.Threading;
using System.Threading.Tasks;
using Viiper.Client;
using Viiper.Client.Devices.Mouse;
using Viiper.Client.Types;

namespace PhysicalMouse.Viiper;

/// <summary>
/// VIIPER transport.
/// </summary>
public sealed class ViiperPhysicalMouse : IPhysicalMouse, IDisposable, IAsyncDisposable
{
    private readonly ViiperClient? _client;
    private readonly uint _busId;
    private readonly string _deviceId;
    private readonly bool _ownsDevice;
    private ViiperDevice? _device;
    private int _isConnected;

    // MARK: Construction
    // ========================================================================

    /// <summary>
    /// Wraps an existing VIIPER device.
    /// </summary>
    /// <param name="device">Connected device stream.</param>
    public ViiperPhysicalMouse(ViiperDevice device)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
        _deviceId = string.Empty;
        _isConnected = 1;
        HookDisconnect(device);
    }

    private ViiperPhysicalMouse(ViiperClient client, ViiperDevice device, uint busId, string deviceId)
    {
        _client = client;
        _device = device;
        _busId = busId;
        _deviceId = deviceId;
        _ownsDevice = true;
        _isConnected = 1;
        HookDisconnect(device);
    }

    // MARK: Implementation
    // ========================================================================

    /// <inheritdoc />
    public bool IsConnected => Volatile.Read(ref _isConnected) != 0;

    /// <summary>
    /// Creates and connects a VIIPER mouse device.
    /// </summary>
    /// <param name="host">Host name or IP address.</param>
    /// <param name="port">TCP port.</param>
    /// <param name="password">Server password.</param>
    /// <param name="busId">Bus to use, if known.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Connected transport.</returns>
    public static async Task<ViiperPhysicalMouse> ConnectAsync(
        string host = "127.0.0.1",
        int port = 3242,
        string password = "",
        uint? busId = null,
        CancellationToken cancellationToken = default)
    {
        ViiperClient client = new(host, port, password);

        try
        {
            // create or pick a bus, then create the mouse device
            uint resolvedBusId = await ResolveBusIdAsync(client, busId, cancellationToken).ConfigureAwait(false);
            Device createdDevice = await client.BusDeviceAddAsync(
                resolvedBusId,
                new DeviceCreateRequest
                {
                    Type = "mouse",
                },
                cancellationToken).ConfigureAwait(false);

            try
            {
                // connect the device stream and hand it straight back
                ViiperDevice device = await client.ConnectDeviceAsync(
                    createdDevice.BusID,
                    createdDevice.DevId,
                    cancellationToken).ConfigureAwait(false);

                return new ViiperPhysicalMouse(client, device, createdDevice.BusID, createdDevice.DevId);
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
            if (_ownsDevice && _client is not null)
            {
                _ = await _client.BusDeviceRemoveAsync(_busId, _deviceId, CancellationToken.None).ConfigureAwait(false);
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
