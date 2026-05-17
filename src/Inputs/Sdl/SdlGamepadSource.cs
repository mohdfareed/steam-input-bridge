using System;
using System.Threading;
using System.Threading.Tasks;
using SDL3;

namespace Inputs.Sdl;

/// <summary>SDL gamepad input source.</summary>
public sealed class SdlGamepadSource : IGamepadInputSource, IGamepadRumbleSink, IDisposable
{
    private const int SensorValueCount = 3;
    private const uint RumbleHoldDurationMilliseconds = uint.MaxValue;

    private readonly float[] _gyroData = new float[SensorValueCount];
    private readonly float[] _accelerometerData = new float[SensorValueCount];
    private readonly SdlGamepadRuntime.Lease _runtimeLease;
    private nint _gamepad;
    private int _isConnected = 1;
    private int _motionEnabled = 1;

    private SdlGamepadSource(
        nint gamepad,
        SdlControllerInfo controller,
        SdlGamepadRuntime.Lease runtimeLease)
    {
        _gamepad = gamepad;
        _runtimeLease = runtimeLease;
        Controller = controller;
        HasGyro = EnableSensor(gamepad, SDL.SensorType.Gyro);
        HasAccelerometer = EnableSensor(gamepad, SDL.SensorType.Accel);
    }

    /// <inheritdoc />
    public bool IsConnected => Volatile.Read(ref _isConnected) != 0;

    /// <summary>Gets the connected SDL controller.</summary>
    public SdlControllerInfo Controller { get; }

    /// <summary>Gets or sets whether motion data is emitted.</summary>
    public bool MotionEnabled
    {
        get => Volatile.Read(ref _motionEnabled) != 0;
        set => Volatile.Write(ref _motionEnabled, value ? 1 : 0);
    }

    /// <summary>Gets whether the connected controller exposes a gyro sensor.</summary>
    public bool HasGyro { get; }

    /// <summary>Gets whether the connected controller exposes an accelerometer sensor.</summary>
    public bool HasAccelerometer { get; }

    /// <summary>Connects to one SDL controller.</summary>
    public static Task<SdlGamepadSource> ConnectAsync(
        SdlControllerId controllerId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            return Task.FromResult(Connect(controllerId));
        }
        catch (DllNotFoundException exception)
        {
            throw SdlControllerCatalog.CreateSdlUnavailableException(exception);
        }
        catch (EntryPointNotFoundException exception)
        {
            throw SdlControllerCatalog.CreateSdlUnavailableException(exception);
        }
    }

    /// <summary>Connects to one SDL controller.</summary>
    public static Task<SdlGamepadSource> ConnectAsync(
        SdlControllerInfo controller,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(controller);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            return Task.FromResult(Connect(controller));
        }
        catch (DllNotFoundException exception)
        {
            throw SdlControllerCatalog.CreateSdlUnavailableException(exception);
        }
        catch (EntryPointNotFoundException exception)
        {
            throw SdlControllerCatalog.CreateSdlUnavailableException(exception);
        }
    }

    /// <inheritdoc />
    public void Run(GamepadInputHandler handler, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(handler);
        SdlControllerInputLoop.Run([this], HandleInput, cancellationToken);

        void HandleInput(SdlGamepadSource _, GamepadInput input)
        {
            handler(in input);
        }
    }

    /// <inheritdoc />
    public bool TryRumble(GamepadRumble rumble)
    {
        nint gamepad = _gamepad;
        return IsConnected &&
            gamepad != 0 &&
            SDL.RumbleGamepad(
                gamepad,
                rumble.LowFrequency,
                rumble.HighFrequency,
                rumble.IsEmpty ? 0 : RumbleHoldDurationMilliseconds);
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
            _ = SDL.RumbleGamepad(gamepad, 0, 0, 0);
            SDL.CloseGamepad(gamepad);
        }

        _runtimeLease.Dispose();
        return ValueTask.CompletedTask;
    }

    private static SdlGamepadSource Connect(SdlControllerId controllerId)
    {
        SdlGamepadRuntime.Lease? lease = null;
        try
        {
            lease = SdlGamepadRuntime.Acquire();

            uint[] gamepadIds = SDL.GetGamepads(out int count) ?? [];
            if (count <= 0)
            {
                throw new InvalidOperationException("No SDL controllers were found.");
            }

            SdlControllerInfo controller = SdlControllerCatalog.ResolveController(
                SdlControllerCatalog.CreateControllerInfos(gamepadIds, count),
                controllerId);

            SdlGamepadSource source = CreateConnectedSource(controller, lease);
            lease = null;
            return source;
        }
        finally
        {
            lease?.Dispose();
        }
    }

    private static SdlGamepadSource Connect(SdlControllerInfo controller)
    {
        SdlGamepadRuntime.Lease? lease = null;
        try
        {
            lease = SdlGamepadRuntime.Acquire();
            SdlGamepadSource source = CreateConnectedSource(controller, lease);
            lease = null;
            return source;
        }
        finally
        {
            lease?.Dispose();
        }
    }

    private static SdlGamepadSource CreateConnectedSource(
        SdlControllerInfo controller,
        SdlGamepadRuntime.Lease runtimeLease)
    {
        nint gamepad = SdlControllerCatalog.OpenGamepad(controller);
        if (gamepad == 0)
        {
            throw new InvalidOperationException($"Could not open SDL controller: {SDL.GetError()}");
        }

        try
        {
            return new SdlGamepadSource(gamepad, controller, runtimeLease);
        }
        catch
        {
            SDL.CloseGamepad(gamepad);
            throw;
        }
    }

    internal static SdlGamepadSource AdoptOpenGamepad(
        nint gamepad,
        SdlControllerInfo controller,
        SdlGamepadRuntime.Lease runtimeLease)
    {
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(runtimeLease);
        if (gamepad == 0)
        {
            throw new ArgumentException("SDL gamepad handle must be open.", nameof(gamepad));
        }

        try
        {
            return new SdlGamepadSource(gamepad, controller, runtimeLease);
        }
        catch
        {
            SDL.CloseGamepad(gamepad);
            throw;
        }
    }

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

        if (eventType == SDL.EventType.GamepadRemoved &&
            sdlEvent.GDevice.Which == Controller.InstanceId)
        {
            _ = Interlocked.Exchange(ref _isConnected, 0);
            throw new SdlGamepadDisconnectedException($"SDL controller \"{Controller.Name}\" was disconnected.");
        }

        return false;
    }

    internal GamepadState ReadCurrentState()
    {
        nint gamepad = _gamepad;
        bool motionEnabled = MotionEnabled;
        return SdlGamepadStateReader.ReadState(
            gamepad,
            motionEnabled && HasGyro,
            motionEnabled && HasAccelerometer,
            _gyroData,
            _accelerometerData);
    }

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

}

/// <summary>Thrown when an SDL controller disconnects while streaming.</summary>
public sealed class SdlGamepadDisconnectedException : InvalidOperationException
{
    /// <summary>Creates a disconnect exception.</summary>
    public SdlGamepadDisconnectedException()
    {
    }

    /// <summary>Creates a disconnect exception.</summary>
    public SdlGamepadDisconnectedException(string message)
        : base(message)
    {
    }

    /// <summary>Creates a disconnect exception.</summary>
    public SdlGamepadDisconnectedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
