using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using SDL3;

namespace Inputs.Sdl;

/// <summary>SDL gamepad connection options.</summary>
public sealed record SdlGamepadOptions
{
    /// <summary>Zero-based SDL gamepad index.</summary>
    public int DeviceIndex { get; init; }

    /// <summary>Polling interval.</summary>
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromMilliseconds(1);
}

/// <summary>SDL gamepad discovered on the system.</summary>
/// <param name="Index">Zero-based gamepad index.</param>
/// <param name="InstanceId">SDL joystick instance id.</param>
/// <param name="Name">Device name.</param>
public sealed record SdlGamepadInfo(int Index, uint InstanceId, string Name);

/// <summary>SDL gamepad input source.</summary>
public sealed class SdlGamepadSource : IGamepadInputSource, IDisposable
{
    private readonly TimeSpan _pollInterval;
    private nint _gamepad;
    private int _isConnected = 1;

    private SdlGamepadSource(
        nint gamepad,
        uint instanceId,
        string deviceName,
        TimeSpan pollInterval)
    {
        _gamepad = gamepad;
        _pollInterval = pollInterval;
        InstanceId = instanceId;
        DeviceName = deviceName;
    }

    /// <inheritdoc />
    public bool IsConnected => Volatile.Read(ref _isConnected) != 0;

    /// <summary>Gets the SDL joystick instance id.</summary>
    public uint InstanceId { get; }

    /// <summary>Gets the SDL device name.</summary>
    public string DeviceName { get; }

    /// <summary>Lists SDL gamepads.</summary>
    public static IReadOnlyList<SdlGamepadInfo> GetGamepads()
    {
        try
        {
            return GetGamepadsCore();
        }
        catch (DllNotFoundException exception)
        {
            throw CreateSdlUnavailableException(exception);
        }
        catch (EntryPointNotFoundException exception)
        {
            throw CreateSdlUnavailableException(exception);
        }
    }

    /// <summary>Creates an SDL gamepad source.</summary>
    /// <param name="options">Connection options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
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
            throw CreateSdlUnavailableException(exception);
        }
        catch (EntryPointNotFoundException exception)
        {
            throw CreateSdlUnavailableException(exception);
        }
    }

    /// <inheritdoc />
    public void Run(GamepadInputHandler handler, CancellationToken cancellationToken = default)
    {
        Run(handler, timingHandler: null, cancellationToken);
    }

    internal void Run(
        GamepadInputHandler handler,
        Action<long, long>? timingHandler,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(handler);
        if (!IsConnected || _gamepad == 0)
        {
            throw new InvalidOperationException("SDL gamepad source is not connected.");
        }

        bool hasPreviousState = false;
        GamepadState previousState = default;
        while (!cancellationToken.IsCancellationRequested)
        {
            long startedTimestamp = Stopwatch.GetTimestamp();
            SDL.UpdateGamepads();
            GamepadState state = ReadState(_gamepad);

            if (!hasPreviousState || state != previousState)
            {
                long emittedTimestamp = Stopwatch.GetTimestamp();
                timingHandler?.Invoke(startedTimestamp, emittedTimestamp);
                GamepadInput input = new(state, DeviceName);
                handler(in input);
                previousState = state;
                hasPreviousState = true;
            }

            WaitForNextPoll(cancellationToken);
        }
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
            SDL.CloseGamepad(gamepad);
            SDL.QuitSubSystem(SDL.InitFlags.Gamepad);
        }

        return ValueTask.CompletedTask;
    }

    internal static GamepadState ReadState(nint gamepad)
    {
        GamepadButtons buttons = GamepadButtons.None;
        buttons = Apply(buttons, gamepad, SDL.GamepadButton.South, GamepadButtons.South);
        buttons = Apply(buttons, gamepad, SDL.GamepadButton.East, GamepadButtons.East);
        buttons = Apply(buttons, gamepad, SDL.GamepadButton.West, GamepadButtons.West);
        buttons = Apply(buttons, gamepad, SDL.GamepadButton.North, GamepadButtons.North);
        buttons = Apply(buttons, gamepad, SDL.GamepadButton.Back, GamepadButtons.Back);
        buttons = Apply(buttons, gamepad, SDL.GamepadButton.Guide, GamepadButtons.Guide);
        buttons = Apply(buttons, gamepad, SDL.GamepadButton.Start, GamepadButtons.Start);
        buttons = Apply(buttons, gamepad, SDL.GamepadButton.LeftStick, GamepadButtons.LeftStick);
        buttons = Apply(buttons, gamepad, SDL.GamepadButton.RightStick, GamepadButtons.RightStick);
        buttons = Apply(buttons, gamepad, SDL.GamepadButton.LeftShoulder, GamepadButtons.LeftShoulder);
        buttons = Apply(buttons, gamepad, SDL.GamepadButton.RightShoulder, GamepadButtons.RightShoulder);
        buttons = Apply(buttons, gamepad, SDL.GamepadButton.DPadUp, GamepadButtons.DPadUp);
        buttons = Apply(buttons, gamepad, SDL.GamepadButton.DPadDown, GamepadButtons.DPadDown);
        buttons = Apply(buttons, gamepad, SDL.GamepadButton.DPadLeft, GamepadButtons.DPadLeft);
        buttons = Apply(buttons, gamepad, SDL.GamepadButton.DPadRight, GamepadButtons.DPadRight);

        return new GamepadState(
            buttons,
            SDL.GetGamepadAxis(gamepad, SDL.GamepadAxis.LeftX),
            SDL.GetGamepadAxis(gamepad, SDL.GamepadAxis.LeftY),
            SDL.GetGamepadAxis(gamepad, SDL.GamepadAxis.RightX),
            SDL.GetGamepadAxis(gamepad, SDL.GamepadAxis.RightY),
            ToTrigger(SDL.GetGamepadAxis(gamepad, SDL.GamepadAxis.LeftTrigger)),
            ToTrigger(SDL.GetGamepadAxis(gamepad, SDL.GamepadAxis.RightTrigger)),
            default);
    }

    internal static ushort ToTrigger(short value)
    {
        return value <= 0 ? (ushort)0 : (ushort)value;
    }

    private static SdlGamepadSource Connect(SdlGamepadOptions options)
    {
        bool initialized = false;
        nint gamepad = 0;

        try
        {
            InitializeGamepads();
            initialized = true;

            uint[] gamepadIds = SDL.GetGamepads(out int count) ?? [];
            if (count <= 0)
            {
                throw new InvalidOperationException("No SDL gamepads were found.");
            }

            if (options.DeviceIndex >= count)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(options),
                    $"SDL gamepad index {options.DeviceIndex} was not found.");
            }

            uint instanceId = gamepadIds[options.DeviceIndex];
            gamepad = SDL.OpenGamepad(instanceId);
            if (gamepad == 0)
            {
                throw new InvalidOperationException($"Could not open SDL gamepad: {SDL.GetError()}");
            }

            string deviceName =
                SDL.GetGamepadName(gamepad) ??
                SDL.GetGamepadNameForID(instanceId) ??
                $"SDL gamepad {instanceId}";

            nint connectedGamepad = gamepad;
            gamepad = 0;
            initialized = false;
            return new SdlGamepadSource(
                connectedGamepad,
                instanceId,
                deviceName,
                options.PollInterval);
        }
        finally
        {
            if (gamepad != 0)
            {
                SDL.CloseGamepad(gamepad);
            }

            if (initialized)
            {
                SDL.QuitSubSystem(SDL.InitFlags.Gamepad);
            }
        }
    }

    private static List<SdlGamepadInfo> GetGamepadsCore()
    {
        InitializeGamepads();
        uint[] gamepadIds = SDL.GetGamepads(out int count) ?? [];
        try
        {
            if (gamepadIds.Length == 0 && count > 0)
            {
                throw new InvalidOperationException($"Could not list SDL gamepads: {SDL.GetError()}");
            }

            List<SdlGamepadInfo> gamepads = new(count);
            for (int i = 0; i < count; i++)
            {
                uint instanceId = gamepadIds[i];
                string name =
                    SDL.GetGamepadNameForID(instanceId) ??
                    $"SDL gamepad {instanceId}";

                gamepads.Add(new SdlGamepadInfo(gamepads.Count, instanceId, name));
            }

            return gamepads;
        }
        finally
        {
            SDL.QuitSubSystem(SDL.InitFlags.Gamepad);
        }
    }

    private static void InitializeGamepads()
    {
        if (!SDL.Init(SDL.InitFlags.Gamepad))
        {
            throw new InvalidOperationException($"Could not initialize SDL gamepad input: {SDL.GetError()}");
        }
    }

    private static void ValidateOptions(SdlGamepadOptions options)
    {
        if (options.DeviceIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "SDL gamepad index must be non-negative.");
        }

        if (options.PollInterval < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "SDL poll interval must be non-negative.");
        }
    }

    private static GamepadButtons Apply(
        GamepadButtons buttons,
        nint gamepad,
        SDL.GamepadButton sdlButton,
        GamepadButtons outputButton)
    {
        return SDL.GetGamepadButton(gamepad, sdlButton)
            ? buttons | outputButton
            : buttons;
    }

    private void WaitForNextPoll(CancellationToken cancellationToken)
    {
        if (_pollInterval == TimeSpan.Zero)
        {
            _ = Thread.Yield();
            return;
        }

        _ = cancellationToken.WaitHandle.WaitOne(_pollInterval);
    }

    private static InvalidOperationException CreateSdlUnavailableException(Exception exception)
    {
        return new InvalidOperationException(
            "SDL3 runtime is not available. Restore SDL3-CS.Native or put SDL3.dll next to the app.",
            exception);
    }
}
