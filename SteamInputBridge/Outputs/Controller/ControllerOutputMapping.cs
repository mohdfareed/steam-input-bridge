using System;
using SteamInputBridge.Inputs.Controller;

namespace SteamInputBridge.Outputs.Controller;

/// <summary>Maps controller states into concrete output reports.</summary>
public static class ControllerOutputMapping
{
    /// <summary>Maps a controller state to an Xbox 360 report.</summary>
    public static Xbox360Report ToXbox360Report(in ControllerState state)
    {
        return new Xbox360Report(
            ToXbox360Buttons(state.Buttons),
            ToByteTrigger(state.LeftTrigger),
            ToByteTrigger(state.RightTrigger),
            state.LeftX,
            InvertAxis(state.LeftY),
            state.RightX,
            InvertAxis(state.RightY));
    }

    /// <summary>Maps Xbox 360 rumble feedback to controller feedback.</summary>
    public static ControllerRumble ToControllerRumble(Xbox360Rumble rumble)
    {
        return new(
            ToUShortMotor(rumble.LeftMotor),
            ToUShortMotor(rumble.RightMotor));
    }

    // MARK: Mapping
    // ========================================================================

    private static Xbox360Buttons ToXbox360Buttons(ControllerButtons buttons)
    {
        Xbox360Buttons output = Xbox360Buttons.None;
        Map(buttons, ControllerButtons.South, ref output, Xbox360Buttons.A);
        Map(buttons, ControllerButtons.East, ref output, Xbox360Buttons.B);
        Map(buttons, ControllerButtons.West, ref output, Xbox360Buttons.X);
        Map(buttons, ControllerButtons.North, ref output, Xbox360Buttons.Y);
        Map(buttons, ControllerButtons.Back, ref output, Xbox360Buttons.Back);
        Map(buttons, ControllerButtons.Guide, ref output, Xbox360Buttons.Guide);
        Map(buttons, ControllerButtons.Start, ref output, Xbox360Buttons.Start);
        Map(buttons, ControllerButtons.LeftStick, ref output, Xbox360Buttons.LeftThumb);
        Map(buttons, ControllerButtons.RightStick, ref output, Xbox360Buttons.RightThumb);
        Map(buttons, ControllerButtons.LeftShoulder, ref output, Xbox360Buttons.LeftShoulder);
        Map(buttons, ControllerButtons.RightShoulder, ref output, Xbox360Buttons.RightShoulder);
        Map(buttons, ControllerButtons.DPadUp, ref output, Xbox360Buttons.DPadUp);
        Map(buttons, ControllerButtons.DPadDown, ref output, Xbox360Buttons.DPadDown);
        Map(buttons, ControllerButtons.DPadLeft, ref output, Xbox360Buttons.DPadLeft);
        Map(buttons, ControllerButtons.DPadRight, ref output, Xbox360Buttons.DPadRight);
        return output;
    }

    private static void Map(
        ControllerButtons input,
        ControllerButtons inputButton,
        ref Xbox360Buttons output,
        Xbox360Buttons outputButton)
    {
        if ((input & inputButton) != 0)
        {
            output |= outputButton;
        }
    }

    private static byte ToByteTrigger(ushort value)
    {
        return (byte)Math.Clamp(value * byte.MaxValue / 32767, byte.MinValue, byte.MaxValue);
    }

    private static ushort ToUShortMotor(byte value)
    {
        return (ushort)(value * ushort.MaxValue / byte.MaxValue);
    }

    private static short InvertAxis(short value)
    {
        return value == short.MinValue ? short.MaxValue : (short)-value;
    }
}
