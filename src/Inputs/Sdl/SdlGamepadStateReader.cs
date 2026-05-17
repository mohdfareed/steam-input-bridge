using System;
using SDL3;

namespace Inputs.Sdl;

internal static class SdlGamepadStateReader
{
    public static GamepadState ReadState(
        nint gamepad,
        bool hasGyro,
        bool hasAccelerometer,
        ReadOnlySpan<float> gyro,
        ReadOnlySpan<float> accelerometer)
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
            CreateMotion(hasGyro, gyro, hasAccelerometer, accelerometer));
    }

    private static GamepadMotion CreateMotion(
        bool hasGyro,
        ReadOnlySpan<float> gyro,
        bool hasAccelerometer,
        ReadOnlySpan<float> accelerometer)
    {
        return new GamepadMotion(
            hasGyro,
            hasGyro ? gyro[0] : 0,
            hasGyro ? gyro[1] : 0,
            hasGyro ? gyro[2] : 0,
            hasAccelerometer,
            hasAccelerometer ? accelerometer[0] : 0,
            hasAccelerometer ? accelerometer[1] : 0,
            hasAccelerometer ? accelerometer[2] : 0);
    }

    private static ushort ToTrigger(short value)
    {
        return value <= 0 ? (ushort)0 : (ushort)value;
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
}

