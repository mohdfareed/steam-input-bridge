using System;
using SDL3;
using SteamInputBridge.Forwarding.Controller;

namespace SteamInputBridge.Inputs.Sdl;

internal static class SdlGamepadStateReader
{
    // MARK: Publics
    // ========================================================================

    public static ControllerState ReadState(
        nint gamepad,
        bool hasGyro,
        bool hasAccelerometer,
        bool hasTouchpad,
        ReadOnlySpan<float> gyro,
        ReadOnlySpan<float> accelerometer)
    {
        ControllerStandardState standard = new(
            ReadButtons(gamepad),
            SDL.GetGamepadAxis(gamepad, SDL.GamepadAxis.LeftX),
            SDL.GetGamepadAxis(gamepad, SDL.GamepadAxis.LeftY),
            SDL.GetGamepadAxis(gamepad, SDL.GamepadAxis.RightX),
            SDL.GetGamepadAxis(gamepad, SDL.GamepadAxis.RightY),
            ToTrigger(SDL.GetGamepadAxis(gamepad, SDL.GamepadAxis.LeftTrigger)),
            ToTrigger(SDL.GetGamepadAxis(gamepad, SDL.GamepadAxis.RightTrigger)));
        ControllerMotionState? motion = !hasGyro && !hasAccelerometer
            ? null
            : new ControllerMotionState(
                hasGyro,
                hasGyro ? gyro[0] : 0,
                hasGyro ? gyro[1] : 0,
                hasGyro ? gyro[2] : 0,
                hasAccelerometer,
                hasAccelerometer ? accelerometer[0] : 0,
                hasAccelerometer ? accelerometer[1] : 0,
                hasAccelerometer ? accelerometer[2] : 0);

        return new ControllerState(standard, motion, hasTouchpad ? ReadTouchpad(gamepad) : null);
    }

    // MARK: Privates
    // ========================================================================

    private static ControllerButtons ReadButtons(nint gamepad)
    {
        ControllerButtons buttons = ControllerButtons.None;

        buttons = Apply(buttons, gamepad, SDL.GamepadButton.South, ControllerButtons.South);
        buttons = Apply(buttons, gamepad, SDL.GamepadButton.East, ControllerButtons.East);
        buttons = Apply(buttons, gamepad, SDL.GamepadButton.West, ControllerButtons.West);
        buttons = Apply(buttons, gamepad, SDL.GamepadButton.North, ControllerButtons.North);
        buttons = Apply(buttons, gamepad, SDL.GamepadButton.Back, ControllerButtons.Back);
        buttons = Apply(buttons, gamepad, SDL.GamepadButton.Guide, ControllerButtons.Guide);
        buttons = Apply(buttons, gamepad, SDL.GamepadButton.Start, ControllerButtons.Start);
        buttons = Apply(buttons, gamepad, SDL.GamepadButton.LeftStick, ControllerButtons.LeftStick);
        buttons = Apply(buttons, gamepad, SDL.GamepadButton.RightStick, ControllerButtons.RightStick);
        buttons = Apply(buttons, gamepad, SDL.GamepadButton.LeftShoulder, ControllerButtons.LeftShoulder);
        buttons = Apply(buttons, gamepad, SDL.GamepadButton.RightShoulder, ControllerButtons.RightShoulder);
        buttons = Apply(buttons, gamepad, SDL.GamepadButton.DPadUp, ControllerButtons.DPadUp);
        buttons = Apply(buttons, gamepad, SDL.GamepadButton.DPadDown, ControllerButtons.DPadDown);
        buttons = Apply(buttons, gamepad, SDL.GamepadButton.DPadLeft, ControllerButtons.DPadLeft);
        buttons = Apply(buttons, gamepad, SDL.GamepadButton.DPadRight, ControllerButtons.DPadRight);
        buttons = Apply(buttons, gamepad, SDL.GamepadButton.Touchpad, ControllerButtons.TouchpadClick);

        return buttons;
    }

    private static ControllerTouchpadState? ReadTouchpad(nint gamepad)
    {
        if (SDL.GetNumGamepadTouchpads(gamepad) <= 0)
        {
            return null;
        }

        ControllerTouchContact touch1 = ReadTouchContact(gamepad, finger: 0);
        ControllerTouchContact touch2 = SDL.GetNumGamepadTouchpadFingers(gamepad, touchpad: 0) > 1
            ? ReadTouchContact(gamepad, finger: 1)
            : default;

        return !touch1.IsTouched && !touch2.IsTouched
            ? null
            : new ControllerTouchpadState(touch1, touch2);
    }

    private static ControllerTouchContact ReadTouchContact(nint gamepad, int finger)
    {
        return SDL.GetGamepadTouchpadFinger(
            gamepad,
            touchpad: 0,
            finger: finger,
            out bool down,
            out float x,
            out float y,
            out float pressure)
            ? new ControllerTouchContact(down, x, y, pressure)
            : default;
    }

    private static ushort ToTrigger(short value)
    {
        return value <= 0 ? (ushort)0 : (ushort)value;
    }

    private static ControllerButtons Apply(
        ControllerButtons buttons,
        nint gamepad,
        SDL.GamepadButton sdlButton,
        ControllerButtons outputButton)
    {
        return SDL.GetGamepadButton(gamepad, sdlButton)
            ? buttons | outputButton
            : buttons;
    }
}
