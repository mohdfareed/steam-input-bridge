using System;
using System.Threading;
using System.Threading.Tasks;
using SDL3;
using SteamInputBridge.Forwarding.Controller;

namespace SteamInputBridge.Inputs.Sdl;

/// <summary>Connected SDL gamepad source.</summary>
public sealed class SdlGamepadSource : IControllerFeedbackSink, IDisposable, IAsyncDisposable
{
    private const int SensorValueCount = 3;
    private const uint RumbleHoldDurationMilliseconds = uint.MaxValue;

    private readonly float[] _gyroData = new float[SensorValueCount];
    private readonly float[] _accelerometerData = new float[SensorValueCount];
    private readonly Lock _feedbackGate = new();
    private readonly SdlGamepadRuntime.Lease _runtimeLease;
    private nint _gamepad;
    private CancellationTokenSource? _lightFlashStop;
    private int _isConnected = 1;
    private int _motionEnabled = 1;

    private SdlGamepadSource(
        nint gamepad,
        SdlControllerInfo controller,
        SdlGamepadRuntime.Lease runtimeLease)
    {
        _gamepad = gamepad;
        Controller = controller;
        _runtimeLease = runtimeLease;

        HasGyro = EnableSensor(gamepad, SDL.SensorType.Gyro);
        HasAccelerometer = EnableSensor(gamepad, SDL.SensorType.Accel);
        HasRumble = GetGamepadBooleanProperty(gamepad, SDL.Props.GamepadCapRumbleBoolean);
        HasRgbLed = GetGamepadBooleanProperty(gamepad, SDL.Props.GamepadCapRGBLedBoolean);
        HasTouchpad = SDL.GetNumGamepadTouchpads(gamepad) > 0;
    }

    // MARK: Publics
    // ========================================================================

    /// <summary>Gets whether the controller is connected.</summary>
    public bool IsConnected => Volatile.Read(ref _isConnected) != 0;

    /// <summary>Gets the connected SDL controller.</summary>
    public SdlControllerInfo Controller { get; }

    /// <summary>Gets whether the connected controller exposes a gyro sensor.</summary>
    public bool HasGyro { get; }

    /// <summary>Gets whether the connected controller exposes an accelerometer sensor.</summary>
    public bool HasAccelerometer { get; }

    /// <summary>Gets whether the connected controller exposes basic rumble.</summary>
    public bool HasRumble { get; }

    /// <summary>Gets whether the connected controller exposes an RGB LED.</summary>
    public bool HasRgbLed { get; }

    /// <summary>Gets whether the connected controller exposes touchpad contacts.</summary>
    public bool HasTouchpad { get; }

    /// <summary>Gets controller feature groups supported by this source.</summary>
    public ControllerFeatures Features =>
        ControllerFeatures.StandardControls |
        (HasRumble ? ControllerFeatures.Rumble : ControllerFeatures.None) |
        (HasRgbLed ? ControllerFeatures.Light : ControllerFeatures.None) |
        (HasTouchpad ? ControllerFeatures.Touchpad : ControllerFeatures.None) |
        (HasGyro || HasAccelerometer ? ControllerFeatures.Motion : ControllerFeatures.None);

    /// <summary>Gets or sets whether motion data is emitted.</summary>
    public bool MotionEnabled
    {
        get => Volatile.Read(ref _motionEnabled) != 0;
        set => Volatile.Write(ref _motionEnabled, value ? 1 : 0);
    }

    /// <summary>Connects to one SDL controller.</summary>
    public static SdlGamepadSource Connect(SdlControllerInfo controller)
    {
        ArgumentNullException.ThrowIfNull(controller);
        SdlGamepadRuntime.Lease? lease = null;

        try
        {
            lease = SdlGamepadRuntime.Acquire();
            nint gamepad = SdlControllerCatalog.OpenGamepad(controller);

            if (gamepad == 0)
            {
                throw new InvalidOperationException($"Could not open SDL controller: {SDL.GetError()}");
            }

            SdlGamepadSource source = new(gamepad, controller, lease);
            lease = null;
            return source;
        }
        finally
        {
            lease?.Dispose();
        }
    }

    /// <inheritdoc />
    public bool TrySendFeedback(ControllerFeedback feedback)
    {
        nint gamepad = _gamepad;
        if (!IsConnected || gamepad == 0)
        {
            return false;
        }

        bool applied = false;
        if (feedback.Rumble is { } rumble)
        {
            applied = TrySendRumble(gamepad, rumble);
        }

        if (feedback.Light is { } light)
        {
            applied = TrySendLight(light) || applied;
        }

        return applied;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        _ = Interlocked.Exchange(ref _isConnected, 0);
        nint gamepad = Interlocked.Exchange(ref _gamepad, 0);
        if (gamepad != 0)
        {
            lock (_feedbackGate)
            {
                CancelLightFlash();
            }

            _ = SDL.RumbleGamepad(gamepad, 0, 0, 0);
            if (HasRgbLed)
            {
                _ = SDL.SetGamepadLED(gamepad, 0, 0, 0);
            }

            SDL.CloseGamepad(gamepad);
        }

        _runtimeLease.Dispose();
        return ValueTask.CompletedTask;
    }

    // MARK: Internals
    // ========================================================================

    internal bool ProcessEvent(SDL.Event sdlEvent)
    {
        SDL.EventType eventType = (SDL.EventType)sdlEvent.Type;
        if (eventType == SDL.EventType.GamepadUpdateComplete &&
            sdlEvent.GDevice.Which == Controller.InstanceId)
        {
            return true;
        }

        if (eventType == SDL.EventType.GamepadSensorUpdate &&
            sdlEvent.GSensor.Which == Controller.InstanceId)
        {
            UpdateMotion(sdlEvent.GSensor);
            return true;
        }

        if ((eventType is SDL.EventType.GamepadTouchpadDown or
            SDL.EventType.GamepadTouchpadMotion or
            SDL.EventType.GamepadTouchpadUp) &&
            sdlEvent.GTouchpad.Which == Controller.InstanceId)
        {
            return true;
        }

        if (eventType == SDL.EventType.GamepadRemoved &&
            sdlEvent.GDevice.Which == Controller.InstanceId)
        {
            _ = Interlocked.Exchange(ref _isConnected, 0);
            throw new SdlGamepadDisconnectedException($"SDL controller \"{Controller.Name}\" was disconnected.");
        }

        return false;
    }

    internal ControllerState ReadCurrentState()
    {
        nint gamepad = _gamepad;
        bool motionEnabled = MotionEnabled;
        return SdlGamepadStateReader.ReadState(
            gamepad,
            motionEnabled && HasGyro,
            motionEnabled && HasAccelerometer,
            HasTouchpad,
            _gyroData,
            _accelerometerData);
    }

    // MARK: Privates
    // ========================================================================

    private unsafe void UpdateMotion(SDL.GamepadSensorEvent sensorEvent)
    {
        ReadOnlySpan<float> data = new(sensorEvent.Data, SensorValueCount);
        SDL.SensorType sensor = (SDL.SensorType)sensorEvent.Sensor;

        if (sensor == SDL.SensorType.Gyro)
        {
            data.CopyTo(_gyroData);
        }
        else if (sensor == SDL.SensorType.Accel)
        {
            data.CopyTo(_accelerometerData);
        }
    }

    private static bool EnableSensor(nint gamepad, SDL.SensorType sensor)
    {
        return gamepad != 0 &&
            SDL.GamepadHasSensor(gamepad, sensor) &&
            (SDL.GamepadSensorEnabled(gamepad, sensor) ||
            SDL.SetGamepadSensorEnabled(gamepad, sensor, enabled: true));
    }

    private static bool GetGamepadBooleanProperty(nint gamepad, string property)
    {
        uint properties = SDL.GetGamepadProperties(gamepad);
        return properties != 0 && SDL.GetBooleanProperty(properties, property, defaultValue: false);
    }

    private static bool TrySendRumble(nint gamepad, ControllerRumble rumble)
    {
        return SDL.RumbleGamepad(
            gamepad,
            rumble.LowFrequency,
            rumble.HighFrequency,
            rumble.LowFrequency == 0 && rumble.HighFrequency == 0 ? 0 : RumbleHoldDurationMilliseconds);
    }

    private bool TrySendLight(ControllerLight light)
    {
        if (!HasRgbLed)
        {
            return false;
        }

        lock (_feedbackGate)
        {
            CancelLightFlash();
            if (light.FlashOn == 0 || light.FlashOff == 0)
            {
                nint gamepad = _gamepad;
                return IsConnected && gamepad != 0 &&
                    SDL.SetGamepadLED(gamepad, light.Red, light.Green, light.Blue);
            }

            _lightFlashStop = new CancellationTokenSource();
            _ = FlashLightAsync(light, _lightFlashStop.Token);
            return true;
        }
    }

    private async Task FlashLightAsync(ControllerLight light, CancellationToken cancellationToken)
    {
        TimeSpan onDelay = ToFlashDelay(light.FlashOn);
        TimeSpan offDelay = ToFlashDelay(light.FlashOff);

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                nint gamepad = _gamepad;
                if (!IsConnected || gamepad == 0)
                {
                    return;
                }

                _ = SDL.SetGamepadLED(gamepad, light.Red, light.Green, light.Blue);
                await Task.Delay(onDelay, cancellationToken).ConfigureAwait(false);

                gamepad = _gamepad;
                if (!IsConnected || gamepad == 0)
                {
                    return;
                }

                _ = SDL.SetGamepadLED(gamepad, 0, 0, 0);
                await Task.Delay(offDelay, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private void CancelLightFlash()
    {
        CancellationTokenSource? previous = _lightFlashStop;
        _lightFlashStop = null;
        previous?.Cancel();
        previous?.Dispose();
    }

    private static TimeSpan ToFlashDelay(byte value)
    {
        // DS4 output reports express flash on/off in 2.5ms units.
        int milliseconds = Math.Max(1, (int)Math.Round(value * 2.5, MidpointRounding.AwayFromZero));
        return TimeSpan.FromMilliseconds(milliseconds);
    }
}
