using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SteamInputBridge.Hosting.Client;
using SteamInputBridge.Inputs.Controller;
using SteamInputBridge.Inputs.Mouse;
using SteamInputBridge.Outputs.Mouse;
using SteamInputBridge.Outputs.Teensy;
using SteamInputBridge.Outputs.Viiper.Mouse;
using SteamInputBridge.Settings;

namespace SteamInputBridge.Forwarding.Mouse;

/// <summary>Client-owned Steam virtual controller to mouse forwarding.</summary>
public sealed class ClientSteamMouseForwardingService(
    ClientRunOptions options,
    SettingsService settings,
    ViiperMouseOutputFactory viiper,
    ILoggerFactory loggerFactory) : IAsyncDisposable
{
    private static readonly TimeSpan TeensyConnectTimeout = TimeSpan.FromSeconds(5);

    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private readonly Lock _gate = new();
    private readonly SteamControllerMouseMapper _mapper = new(
        ResolveMouseSensitivity(settings.Current, options.ProfileId));
    private IMouseOutput? _output;
    private TeensyMouseOutputService? _teensy;
    private ulong? _controllerHandle;
    private bool _active;
    private bool _pointerEnabled = true;

    /// <summary>Connects or releases the configured client-owned mouse output.</summary>
    public async Task SetActiveAsync(bool active, CancellationToken cancellationToken = default)
    {
        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_active == active)
            {
                return;
            }

            if (!active)
            {
                lock (_gate)
                {
                    _active = false;
                }

                await DisconnectOutputAsync().ConfigureAwait(false);
                return;
            }

            GameProfile? profile = Profile();
            if (profile?.MouseInput == MouseInputMode.Steam && profile.MouseOutput.HasValue)
            {
                IMouseOutput? output = null;
                try
                {
                    output = await ConnectOutputAsync(profile.MouseOutput.Value, cancellationToken)
                        .ConfigureAwait(false);
                    await output.ClearAsync(cancellationToken).ConfigureAwait(false);
                    lock (_gate)
                    {
                        _mapper.SetSensitivity(ResolveMouseSensitivity(settings.Current, options.ProfileId));
                        _mapper.Reset();
                        _output = output;
                    }
                }
                catch
                {
                    if (output is not null)
                    {
                        await output.DisposeAsync().ConfigureAwait(false);
                    }

                    await DisconnectOutputAsync().ConfigureAwait(false);
                    throw;
                }
            }

            lock (_gate)
            {
                _active = true;
            }
        }
        finally
        {
            _ = _lifecycle.Release();
        }
    }

    /// <summary>Selects the resolved Steam controller used for mouse input.</summary>
    public void SelectController(ulong? controllerHandle)
    {
        IMouseOutput? clear;
        lock (_gate)
        {
            if (_controllerHandle == controllerHandle)
            {
                return;
            }

            _controllerHandle = controllerHandle;
            _mapper.Reset();
            clear = _output;
        }

        Clear(clear);
    }

    /// <summary>Sets whether mapped Steam mouse output is enabled.</summary>
    public void SetPointerEnabled(bool enabled)
    {
        IMouseOutput? clear = null;
        lock (_gate)
        {
            if (_pointerEnabled == enabled)
            {
                return;
            }

            _pointerEnabled = enabled;
            _mapper.Reset();
            if (!enabled)
            {
                clear = _output;
            }
        }

        Clear(clear);
    }

    /// <summary>Maps one resolved Steam virtual controller state to mouse output.</summary>
    public void Send(ulong controllerHandle, in ControllerState state, TimeSpan elapsed)
    {
        IMouseOutput? output;
        MouseReport report;
        lock (_gate)
        {
            output = _active && _pointerEnabled && _controllerHandle == controllerHandle ? _output : null;
            if (output is null || !_mapper.TryMap(in state, elapsed, out report))
            {
                return;
            }
        }

        MouseInput input = new(report, DeviceName: null);
        ValueTask send = output.SendAsync(in input);
        if (!send.IsCompletedSuccessfully)
        {
            _ = ObserveAsync(send);
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _lifecycle.WaitAsync().ConfigureAwait(false);
        try
        {
            lock (_gate)
            {
                _active = false;
            }

            await DisconnectOutputAsync().ConfigureAwait(false);
        }
        finally
        {
            _ = _lifecycle.Release();
            _lifecycle.Dispose();
        }
    }

    private async ValueTask<IMouseOutput> ConnectOutputAsync(
        MouseOutput output,
        CancellationToken cancellationToken)
    {
        if (output == MouseOutput.Viiper)
        {
            return await viiper.ConnectAsync(output, cancellationToken).ConfigureAwait(false);
        }

        if (output != MouseOutput.Teensy)
        {
            throw new NotSupportedException($"Unsupported Steam mouse output {output}.");
        }

        TeensyMouseOutputService teensy = new(
            settings,
            loggerFactory.CreateLogger<TeensyMouseOutputService>());
        await teensy.StartAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await WaitForTeensyAsync(teensy, cancellationToken).ConfigureAwait(false);
            _teensy = teensy;
            return teensy.CreateOutput();
        }
        catch
        {
            await teensy.StopAsync(CancellationToken.None).ConfigureAwait(false);
            teensy.Dispose();
            throw;
        }
    }

    private static async Task WaitForTeensyAsync(
        TeensyMouseOutputService teensy,
        CancellationToken cancellationToken)
    {
        if (teensy.IsConnected)
        {
            return;
        }

        TaskCompletionSource connected = new(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnStatusChanged(object? sender, EventArgs args)
        {
            _ = sender;
            _ = args;
            if (teensy.IsConnected)
            {
                _ = connected.TrySetResult();
            }
        }

        teensy.StatusChanged += OnStatusChanged;
        try
        {
            if (teensy.IsConnected)
            {
                return;
            }

            await connected.Task.WaitAsync(TeensyConnectTimeout, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            teensy.StatusChanged -= OnStatusChanged;
        }
    }

    private async ValueTask DisconnectOutputAsync()
    {
        IMouseOutput? output;
        TeensyMouseOutputService? teensy;
        lock (_gate)
        {
            output = _output;
            _output = null;
            teensy = _teensy;
            _teensy = null;
            _mapper.Reset();
        }

        if (output is not null)
        {
            await output.ClearAsync().ConfigureAwait(false);
            await output.DisposeAsync().ConfigureAwait(false);
        }

        if (teensy is not null)
        {
            await teensy.StopAsync(CancellationToken.None).ConfigureAwait(false);
            teensy.Dispose();
        }
    }

    private GameProfile? Profile()
    {
        return settings.Current.Games.TryGetValue(options.ProfileId, out GameProfile? profile)
            ? profile
            : null;
    }

    internal static double ResolveMouseSensitivity(SteamInputBridgeSettings settings, string profileId)
    {
        return settings.Games.TryGetValue(profileId, out GameProfile? profile) &&
            profile.MouseSensitivity.HasValue
            ? profile.MouseSensitivity.Value
            : settings.MouseSensitivity;
    }

    private static void Clear(IMouseOutput? output)
    {
        if (output is null)
        {
            return;
        }

        ValueTask clear = output.ClearAsync();
        if (!clear.IsCompletedSuccessfully)
        {
            _ = ObserveAsync(clear);
        }
    }

    private static async Task ObserveAsync(ValueTask operation)
    {
        try
        {
            await operation.ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is InvalidOperationException or ObjectDisposedException)
        {
        }
    }
}
