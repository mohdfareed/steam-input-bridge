using System;
using SDL3;
using SteamInputBridge.Forwarding.Controller;

namespace SteamInputBridge.Inputs.Sdl;

internal static class SdlGamepadStateReader
{
    private const int MaxTouchContacts = 2;

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
        int touchpadCount = SDL.GetNumGamepadTouchpads(gamepad);
        if (touchpadCount <= 0)
        {
            return null;
        }

        // DS4/DualSense expose one SDL touchpad with two fingers. Newer SDL
        // exposes Steam Controller as two touchpads with one finger each.
        // Flatten both shapes into the two DS4-compatible contacts we forward.
        ControllerTouchContact touch1 = default;
        ControllerTouchContact touch2 = default;
        int contactCount = 0;

        for (int touchpad = 0; touchpad < touchpadCount && contactCount < MaxTouchContacts; touchpad++)
        {
            int fingerCount = SDL.GetNumGamepadTouchpadFingers(gamepad, touchpad);
            for (int finger = 0; finger < fingerCount && contactCount < MaxTouchContacts; finger++)
            {
                ControllerTouchContact contact = ReadTouchContact(gamepad, touchpad, finger);
                if (!contact.IsTouched)
                {
                    continue;
                }

                if (contactCount == 0)
                {
                    touch1 = contact;
                }
                else
                {
                    touch2 = contact;
                }

                contactCount++;
            }
        }

        return contactCount == 0
            ? null
            : new ControllerTouchpadState(touch1, touch2);
    }

    private static ControllerTouchContact ReadTouchContact(nint gamepad, int touchpad, int finger)
    {
        return SDL.GetGamepadTouchpadFinger(
            gamepad,
            touchpad: touchpad,
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
