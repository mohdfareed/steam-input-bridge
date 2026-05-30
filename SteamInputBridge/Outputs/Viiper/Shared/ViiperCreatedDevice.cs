using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using global::Viiper.Client;

namespace SteamInputBridge.Outputs.Viiper.Shared;

internal sealed class ViiperCreatedDevice : IDisposable, IAsyncDisposable
{
    private static readonly TimeSpan DisposeStreamTimeout = TimeSpan.FromSeconds(1);

    private readonly ViiperClient _client;
    private readonly Action<uint, string>? _removed;
    private readonly Action<uint, string>? _disconnected;
    private ViiperDevice? _device;
    private int _isConnected;

    private sealed class OutputSubscription(Action dispose) : IDisposable
    {
        private Action? _dispose = dispose;

        public void Dispose()
        {
            Interlocked.Exchange(ref _dispose, null)?.Invoke();
        }
    }

    // MARK: Publics
    // ========================================================================

    public ViiperCreatedDevice(
        ViiperClient client,
        ViiperDevice device,
        uint busId,
        string deviceId,
        Action<uint, string>? removed = null,
        Action<uint, string>? disconnected = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);

        _client = client ?? throw new ArgumentNullException(nameof(client));
        _device = device ?? throw new ArgumentNullException(nameof(device));
        _removed = removed;
        _disconnected = disconnected;
        _isConnected = 1;

        BusId = busId;
        DeviceId = deviceId;
        HookDisconnect(device);
    }

    public bool IsConnected => Volatile.Read(ref _isConnected) != 0;

    public uint BusId { get; }

    public string DeviceId { get; }

    public ViiperDevice GetDeviceOrThrow(string message)
    {
        ViiperDevice? device = _device;
        return !IsConnected || device is null
            ? throw new InvalidOperationException(message)
            : device;
    }

    public IDisposable ListenOutput(Func<Stream, Task> handler, string message)
    {
        ArgumentNullException.ThrowIfNull(handler);

        ViiperDevice device = GetDeviceOrThrow(message);
        if (device.OnOutput is not null)
        {
            throw new InvalidOperationException("VIIPER output feedback is already being handled.");
        }

        device.OnOutput = handler;
        return new OutputSubscription(() =>
        {
            if (ReferenceEquals(device.OnOutput, handler))
            {
                device.OnOutput = null;
            }
        });
    }

    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    public async ValueTask DisposeAsync()
    {
        _ = Interlocked.Exchange(ref _isConnected, 0);
        ViiperDevice? device = Interlocked.Exchange(ref _device, null);
        if (device is null)
        {
            return;
        }

        try
        {
            try
            {
                _ = await _client
                    .BusDeviceRemoveAsync(BusId, DeviceId, CancellationToken.None)
                    .ConfigureAwait(false);
                _removed?.Invoke(BusId, DeviceId);
            }
            catch (IOException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
            catch (InvalidOperationException)
            {
            }
            finally
            {
                await RemoveBusAsync().ConfigureAwait(false);
            }

            _ = ObserveDeviceStreamDisposeAsync(device);
        }
        finally
        {
            _client.Dispose();
        }
    }

    // MARK: Privates
    // ========================================================================

    private async Task RemoveBusAsync()
    {
        try
        {
            _ = await _client.BusRemoveAsync(BusId, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (IOException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static async Task ObserveDeviceStreamDisposeAsync(ViiperDevice device)
    {
        try
        {
            await device.DisposeAsync().AsTask().WaitAsync(DisposeStreamTimeout).ConfigureAwait(false);
        }
        catch (IOException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        catch (TimeoutException)
        {
        }
    }

    private void HookDisconnect(ViiperDevice device)
    {
        Action? onDisconnect = device.OnDisconnect;
        device.OnDisconnect = () =>
        {
            _ = Interlocked.Exchange(ref _isConnected, 0);
            _disconnected?.Invoke(BusId, DeviceId);
            onDisconnect?.Invoke();
        };
    }
}
