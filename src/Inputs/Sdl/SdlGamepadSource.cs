using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SDL3;

namespace Inputs.Sdl;

/// <summary>SDL gamepad input source.</summary>
public sealed partial class SdlGamepadSource : IGamepadInputSource, IGamepadRumbleSink, IDisposable
{
    private const int SensorValueCount = 3;
    private const uint RumbleHoldDurationMilliseconds = uint.MaxValue;

    private readonly float[] _gyroData = new float[SensorValueCount];
    private readonly float[] _accelerometerData = new float[SensorValueCount];
    private nint _gamepad;
    private nint _motionGamepad;
    private int _isConnected = 1;
    private int _motionEnabled;

    private SdlGamepadSource(
        nint gamepad,
        SdlGamepadInfo primary,
        nint motionGamepad,
        SdlGamepadInfo? motionInfo,
        bool hasGyro,
        bool hasAccelerometer)
    {
        _gamepad = gamepad;
        _motionGamepad = motionGamepad;
        _motionEnabled = 1;
        DeviceIndex = primary.Index;
        InstanceId = primary.InstanceId;
        DeviceName = primary.Name;
        SteamHandle = primary.SteamHandle;
        VendorId = primary.VendorId;
        ProductId = primary.ProductId;
        Path = primary.Path;
        UsesPhysicalMotion = motionGamepad != 0;
        MotionDeviceName = motionInfo?.Name;
        MotionInstanceId = motionInfo?.InstanceId;
        MotionVendorId = motionInfo?.VendorId;
        MotionProductId = motionInfo?.ProductId;
        MotionPath = motionInfo?.Path;
        HasGyro = hasGyro;
        HasAccelerometer = hasAccelerometer;
    }

    /// <inheritdoc />
    public bool IsConnected => Volatile.Read(ref _isConnected) != 0;

    /// <summary>Gets the zero-based SDL gamepad index.</summary>
    public int DeviceIndex { get; }

    /// <summary>Gets the SDL joystick instance id.</summary>
    public uint InstanceId { get; }

    /// <summary>Gets the SDL device name.</summary>
    public string DeviceName { get; }

    /// <summary>Gets whether motion and rumble use a secondary physical SDL gamepad.</summary>
    public bool UsesPhysicalMotion { get; }

    /// <summary>Gets or sets whether motion data is emitted.</summary>
    public bool MotionEnabled
    {
        get => Volatile.Read(ref _motionEnabled) != 0;
        set => Volatile.Write(ref _motionEnabled, value ? 1 : 0);
    }

    /// <summary>Gets the SDL Steam handle; zero means not Steam-routed.</summary>
    public ulong SteamHandle { get; }

    /// <summary>Gets whether SDL reports this gamepad through Steam Input.</summary>
    public bool IsSteamInput => SteamHandle != 0;

    /// <summary>Gets the USB vendor id when known.</summary>
    public ushort VendorId { get; }

    /// <summary>Gets the USB product id when known.</summary>
    public ushort ProductId { get; }

    /// <summary>Gets the SDL device path when known.</summary>
    public string? Path { get; }

    /// <summary>Gets the SDL motion device name in mixed mode.</summary>
    public string? MotionDeviceName { get; }

    /// <summary>Gets the SDL motion device instance id in mixed mode.</summary>
    public uint? MotionInstanceId { get; }

    /// <summary>Gets the motion device USB vendor id when known.</summary>
    public ushort? MotionVendorId { get; }

    /// <summary>Gets the motion device USB product id when known.</summary>
    public ushort? MotionProductId { get; }

    /// <summary>Gets the motion device SDL path when known.</summary>
    public string? MotionPath { get; }

    /// <summary>Gets whether the connected gamepad exposes a gyro sensor.</summary>
    public bool HasGyro { get; }

    /// <summary>Gets whether the connected gamepad exposes an accelerometer sensor.</summary>
    public bool HasAccelerometer { get; }

    /// <summary>Lists SDL gamepads.</summary>
    public static IReadOnlyList<SdlGamepadInfo> GetGamepads()
    {
        return SdlGamepadDiscovery.GetGamepads();
    }

    /// <summary>Creates an SDL gamepad source.</summary>
    public static Task<SdlGamepadSource> ConnectAsync(
        SdlGamepadOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        options ??= new SdlGamepadOptions();
        ValidateOptions(options);

        try
        {
#pragma warning disable CA2000 // Ownership transfers to the caller.
            return Task.FromResult(Connect(options));
#pragma warning restore CA2000
        }
        catch (DllNotFoundException exception)
        {
            throw SdlGamepadDiscovery.CreateSdlUnavailableException(exception);
        }
        catch (EntryPointNotFoundException exception)
        {
            throw SdlGamepadDiscovery.CreateSdlUnavailableException(exception);
        }
    }

    /// <inheritdoc />
    public void Run(GamepadInputHandler handler, CancellationToken cancellationToken = default)
    {
        Run(handler, timingHandler: null, cancellationToken);
    }

    /// <inheritdoc />
    public bool TryRumble(GamepadRumble rumble)
    {
        nint gamepad = GetRumbleGamepad();
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
        nint motionGamepad = Interlocked.Exchange(ref _motionGamepad, 0);
        if (gamepad != 0)
        {
            if (motionGamepad == 0)
            {
                _ = SDL.RumbleGamepad(gamepad, 0, 0, 0);
            }

            SDL.CloseGamepad(gamepad);
        }

        if (motionGamepad != 0)
        {
            _ = SDL.RumbleGamepad(motionGamepad, 0, 0, 0);
            SDL.CloseGamepad(motionGamepad);
        }

        if (gamepad != 0 || motionGamepad != 0)
        {
            SDL.QuitSubSystem(SDL.InitFlags.Gamepad);
        }

        return ValueTask.CompletedTask;
    }

    private nint GetRumbleGamepad()
    {
        nint motionGamepad = _motionGamepad;
        return motionGamepad != 0 ? motionGamepad : _gamepad;
    }
}
