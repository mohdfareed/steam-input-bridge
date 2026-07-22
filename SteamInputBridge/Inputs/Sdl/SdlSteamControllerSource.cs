using System;
using System.Threading;
using System.Threading.Tasks;
using SDL3;
using SteamInputBridge.Inputs.Controller;

namespace SteamInputBridge.Inputs.Sdl;

/// <summary>Open SDL Steam Input controller stream.</summary>
public sealed class SdlSteamControllerSource : IAsyncDisposable
{
    // REVIEW: Verify SDL's max rumble duration is the right "hold until next update" behavior.
    private const uint RumbleHoldDurationMilliseconds = uint.MaxValue;

    private nint _gamepad;
    private ControllerState _eventState;
    private int _connected = 1;

    private SdlSteamControllerSource(SdlSteamControllerInfo controller, nint gamepad)
    {
        Controller = controller;
        _gamepad = gamepad;
        _eventState = ReadCurrentState(gamepad);
    }

    // MARK: Publics
    // ========================================================================

    /// <summary>Controller identity.</summary>
    public SdlSteamControllerInfo Controller { get; }

    /// <summary>Gets whether the SDL stream is still connected.</summary>
    public bool IsConnected => Volatile.Read(ref _connected) != 0;

    /// <summary>Opens one controller stream.</summary>
    public static SdlSteamControllerSource Open(SdlSteamControllerInfo controller)
    {
        ArgumentNullException.ThrowIfNull(controller);
        SdlGamepadRuntime.EnsureInitialized();
        nint gamepad = SDL.OpenGamepad(controller.InstanceId);
        return gamepad == 0
            ? throw new InvalidOperationException($"Could not open SDL controller: {SDL.GetError()}")
            : new SdlSteamControllerSource(controller, gamepad);
    }

    /// <summary>Applies one SDL event and returns a coherent completed controller update.</summary>
    public bool TryReadStateEvent(SDL.Event sdlEvent, out ControllerState state, out ulong timestamp)
    {
        state = ControllerState.Empty;
        timestamp = 0;
        SDL.EventType type = (SDL.EventType)sdlEvent.Type;
        if (type == SDL.EventType.GamepadRemoved && sdlEvent.GDevice.Which == Controller.InstanceId)
        {
            _ = Interlocked.Exchange(ref _connected, 0);
            return false;
        }

        if (!IsConnected)
        {
            return false;
        }

        if (type == SDL.EventType.GamepadAxisMotion && sdlEvent.GAxis.Which == Controller.InstanceId)
        {
            ApplyAxis((SDL.GamepadAxis)sdlEvent.GAxis.Axis, sdlEvent.GAxis.Value);
            return false;
        }

        if ((type is SDL.EventType.GamepadButtonDown or SDL.EventType.GamepadButtonUp) &&
            sdlEvent.GButton.Which == Controller.InstanceId)
        {
            ApplyButton(
                (SDL.GamepadButton)sdlEvent.GButton.Button,
                type == SDL.EventType.GamepadButtonDown);
            return false;
        }

        if (type != SDL.EventType.GamepadUpdateComplete || sdlEvent.GDevice.Which != Controller.InstanceId)
        {
            return false;
        }

        state = _eventState;
        timestamp = sdlEvent.GDevice.Timestamp;
        return true;
    }

    /// <summary>Reads the current controller state.</summary>
    public ControllerState ReadState()
    {
        nint gamepad = _gamepad;
        return !IsConnected || gamepad == 0
            ? ControllerState.Empty
            : ReadCurrentState(gamepad);
    }

    /// <summary>Sends rumble feedback to the controller.</summary>
    public void SendRumble(ControllerRumble rumble)
    {
        nint gamepad = _gamepad;
        if (!IsConnected || gamepad == 0)
        {
            return;
        }

        _ = SDL.RumbleGamepad(
            gamepad,
            rumble.LowFrequency,
            rumble.HighFrequency,
            rumble.LowFrequency == 0 && rumble.HighFrequency == 0 ? 0 : RumbleHoldDurationMilliseconds);
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        _ = Interlocked.Exchange(ref _connected, 0);
        nint gamepad = Interlocked.Exchange(ref _gamepad, 0);
        if (gamepad != 0)
        {
            _ = SDL.RumbleGamepad(gamepad, 0, 0, 0);
            SDL.CloseGamepad(gamepad);
        }

        return ValueTask.CompletedTask;
    }

    // MARK: State Mapping
    // ========================================================================

#pragma warning disable IDE0072 // Unknown SDL axes and buttons are intentionally ignored.
    private void ApplyAxis(SDL.GamepadAxis axis, short value)
    {
        _eventState = axis switch
        {
            SDL.GamepadAxis.LeftX => _eventState with { LeftX = value },
            SDL.GamepadAxis.LeftY => _eventState with { LeftY = value },
            SDL.GamepadAxis.RightX => _eventState with { RightX = value },
            SDL.GamepadAxis.RightY => _eventState with { RightY = value },
            SDL.GamepadAxis.LeftTrigger => _eventState with { LeftTrigger = ToTrigger(value) },
            SDL.GamepadAxis.RightTrigger => _eventState with { RightTrigger = ToTrigger(value) },
            _ => _eventState,
        };
    }

    private void ApplyButton(SDL.GamepadButton button, bool pressed)
    {
        ControllerButtons mapped = MapButton(button);
        _eventState = _eventState with
        {
            Buttons = pressed ? _eventState.Buttons | mapped : _eventState.Buttons & ~mapped,
        };
    }

    private static ControllerState ReadCurrentState(nint gamepad)
    {
        return new ControllerState(
            ReadButtons(gamepad),
            SDL.GetGamepadAxis(gamepad, SDL.GamepadAxis.LeftX),
            SDL.GetGamepadAxis(gamepad, SDL.GamepadAxis.LeftY),
            SDL.GetGamepadAxis(gamepad, SDL.GamepadAxis.RightX),
            SDL.GetGamepadAxis(gamepad, SDL.GamepadAxis.RightY),
            ToTrigger(SDL.GetGamepadAxis(gamepad, SDL.GamepadAxis.LeftTrigger)),
            ToTrigger(SDL.GetGamepadAxis(gamepad, SDL.GamepadAxis.RightTrigger)));
    }

    private static ControllerButtons ReadButtons(nint gamepad)
    {
        ControllerButtons buttons = ControllerButtons.None;
        buttons = Add(buttons, gamepad, SDL.GamepadButton.South, ControllerButtons.South);
        buttons = Add(buttons, gamepad, SDL.GamepadButton.East, ControllerButtons.East);
        buttons = Add(buttons, gamepad, SDL.GamepadButton.West, ControllerButtons.West);
        buttons = Add(buttons, gamepad, SDL.GamepadButton.North, ControllerButtons.North);
        buttons = Add(buttons, gamepad, SDL.GamepadButton.Back, ControllerButtons.Back);
        buttons = Add(buttons, gamepad, SDL.GamepadButton.Guide, ControllerButtons.Guide);
        buttons = Add(buttons, gamepad, SDL.GamepadButton.Start, ControllerButtons.Start);
        buttons = Add(buttons, gamepad, SDL.GamepadButton.LeftStick, ControllerButtons.LeftStick);
        buttons = Add(buttons, gamepad, SDL.GamepadButton.RightStick, ControllerButtons.RightStick);
        buttons = Add(buttons, gamepad, SDL.GamepadButton.LeftShoulder, ControllerButtons.LeftShoulder);
        buttons = Add(buttons, gamepad, SDL.GamepadButton.RightShoulder, ControllerButtons.RightShoulder);
        buttons = Add(buttons, gamepad, SDL.GamepadButton.DPadUp, ControllerButtons.DPadUp);
        buttons = Add(buttons, gamepad, SDL.GamepadButton.DPadDown, ControllerButtons.DPadDown);
        buttons = Add(buttons, gamepad, SDL.GamepadButton.DPadLeft, ControllerButtons.DPadLeft);
        buttons = Add(buttons, gamepad, SDL.GamepadButton.DPadRight, ControllerButtons.DPadRight);
        return buttons;
    }

    private static ControllerButtons Add(
        ControllerButtons buttons,
        nint gamepad,
        SDL.GamepadButton sdlButton,
        ControllerButtons bridgeButton)
    {
        return SDL.GetGamepadButton(gamepad, sdlButton)
            ? buttons | bridgeButton
            : buttons;
    }

    private static ControllerButtons MapButton(SDL.GamepadButton button)
    {
        return button switch
        {
            SDL.GamepadButton.South => ControllerButtons.South,
            SDL.GamepadButton.East => ControllerButtons.East,
            SDL.GamepadButton.West => ControllerButtons.West,
            SDL.GamepadButton.North => ControllerButtons.North,
            SDL.GamepadButton.Back => ControllerButtons.Back,
            SDL.GamepadButton.Guide => ControllerButtons.Guide,
            SDL.GamepadButton.Start => ControllerButtons.Start,
            SDL.GamepadButton.LeftStick => ControllerButtons.LeftStick,
            SDL.GamepadButton.RightStick => ControllerButtons.RightStick,
            SDL.GamepadButton.LeftShoulder => ControllerButtons.LeftShoulder,
            SDL.GamepadButton.RightShoulder => ControllerButtons.RightShoulder,
            SDL.GamepadButton.DPadUp => ControllerButtons.DPadUp,
            SDL.GamepadButton.DPadDown => ControllerButtons.DPadDown,
            SDL.GamepadButton.DPadLeft => ControllerButtons.DPadLeft,
            SDL.GamepadButton.DPadRight => ControllerButtons.DPadRight,
            _ => ControllerButtons.None,
        };
    }
#pragma warning restore IDE0072

    private static ushort ToTrigger(short value)
    {
        return value <= 0 ? (ushort)0 : (ushort)value;
    }
}
