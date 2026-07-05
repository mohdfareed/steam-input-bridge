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
    private int _connected = 1;

    private SdlSteamControllerSource(SdlSteamControllerInfo controller, nint gamepad)
    {
        Controller = controller;
        _gamepad = gamepad;
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

    /// <summary>Returns whether an SDL event updates this controller state.</summary>
    public bool IsStateEvent(SDL.Event sdlEvent)
    {
        SDL.EventType type = (SDL.EventType)sdlEvent.Type;
        if (type == SDL.EventType.GamepadRemoved && sdlEvent.GDevice.Which == Controller.InstanceId)
        {
            _ = Interlocked.Exchange(ref _connected, 0);
            return false;
        }

        return type == SDL.EventType.GamepadUpdateComplete &&
            sdlEvent.GDevice.Which == Controller.InstanceId &&
            IsConnected;
    }

    /// <summary>Reads the current controller state.</summary>
    public ControllerState ReadState()
    {
        nint gamepad = _gamepad;
        return !IsConnected || gamepad == 0
            ? ControllerState.Empty
            : new ControllerState(
                ReadButtons(gamepad),
                SDL.GetGamepadAxis(gamepad, SDL.GamepadAxis.LeftX),
                SDL.GetGamepadAxis(gamepad, SDL.GamepadAxis.LeftY),
                SDL.GetGamepadAxis(gamepad, SDL.GamepadAxis.RightX),
                SDL.GetGamepadAxis(gamepad, SDL.GamepadAxis.RightY),
                ToTrigger(SDL.GetGamepadAxis(gamepad, SDL.GamepadAxis.LeftTrigger)),
                ToTrigger(SDL.GetGamepadAxis(gamepad, SDL.GamepadAxis.RightTrigger)));
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

    private static ushort ToTrigger(short value)
    {
        return value <= 0 ? (ushort)0 : (ushort)value;
    }
}
