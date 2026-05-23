using System;

namespace SteamInputBridge.Forwarding.Controller;

/// <summary>Maps canonical controller state into concrete output reports.</summary>
public static class ControllerOutputMapping
{
    private const float RadiansToDegrees = 180f / MathF.PI;
    private const float Ds4GyroCountsPerDegreePerSecond = 16f;
    private const float Ds4AccelCountsPerMeterPerSecondSquared = 512f;

    // MARK: Publics
    // ========================================================================

    /// <summary>Maps a controller state to an Xbox 360 report.</summary>
    public static Xbox360Report ToXbox360Report(in ControllerState state)
    {
        return state.Standard is not { } standard
            ? Xbox360Report.Empty
            : new Xbox360Report(
            ToXbox360Buttons(standard.Buttons),
            ToByteTrigger(standard.LeftTrigger),
            ToByteTrigger(standard.RightTrigger),
            standard.LeftX,
            InvertAxis(standard.LeftY),
            standard.RightX,
            InvertAxis(standard.RightY));
    }

    /// <summary>Maps a controller state to a DualShock 4 report.</summary>
    public static Ds4Report ToDs4Report(in ControllerState state)
    {
        ControllerMotionState? motion = state.Motion;
        ControllerTouchpadState? touchpad = state.Touchpad;
        ControllerStandardState standard = state.Standard.GetValueOrDefault();
        bool hasStandard = state.Standard.HasValue;

        byte leftTrigger = hasStandard
            ? ToByteTrigger(standard.LeftTrigger)
            : byte.MinValue;
        byte rightTrigger = hasStandard
            ? ToByteTrigger(standard.RightTrigger)
            : byte.MinValue;
        Ds4Buttons buttons = hasStandard
            ? ToDs4Buttons(standard.Buttons, leftTrigger, rightTrigger)
            : Ds4Buttons.None;

        return new Ds4Report(
            buttons,
            hasStandard ? ToDs4DPad(standard.Buttons) : Ds4DPad.Neutral,
            hasStandard ? ToDs4Axis(standard.LeftX) : (sbyte)0,
            hasStandard ? ToDs4Axis(standard.LeftY) : (sbyte)0,
            hasStandard ? ToDs4Axis(standard.RightX) : (sbyte)0,
            hasStandard ? ToDs4Axis(standard.RightY) : (sbyte)0,
            leftTrigger,
            rightTrigger,
            touchpad is { IsTouched: true } firstTouch ? ToTouchpadX(firstTouch.X) : (ushort)0,
            touchpad is { IsTouched: true } firstTouchY ? ToTouchpadY(firstTouchY.Y) : (ushort)0,
            touchpad is { IsTouched: true },
            0,
            0,
            false,
            motion is { HasGyro: true } gyroX ? ToDs4GyroRaw(gyroX.GyroX) : (short)0,
            motion is { HasGyro: true } gyroY ? ToDs4GyroRaw(gyroY.GyroY) : (short)0,
            motion is { HasGyro: true } gyroZ ? ToDs4GyroRaw(gyroZ.GyroZ) : (short)0,
            motion is { HasAccelerometer: true } accelX ? ToDs4AccelRaw(accelX.AccelX) : (short)0,
            motion is { HasAccelerometer: true } accelY ? ToDs4AccelRaw(accelY.AccelY) : (short)0,
            motion is { HasAccelerometer: true } accelZ
                ? ToDs4AccelRaw(accelZ.AccelZ)
                : Ds4Report.DefaultAccelZ);
    }

    /// <summary>Maps Xbox 360 rumble feedback to canonical controller feedback.</summary>
    public static ControllerFeedback ToControllerFeedback(Xbox360Rumble rumble)
    {
        return new ControllerFeedback(new ControllerRumble(
            ToUShortMotor(rumble.LeftMotor),
            ToUShortMotor(rumble.RightMotor)));
    }

    /// <summary>Maps DualShock 4 rumble feedback to canonical controller feedback.</summary>
    public static ControllerFeedback ToControllerFeedback(Ds4Feedback feedback)
    {
        return new ControllerFeedback(
            new ControllerRumble(
                ToUShortMotor(feedback.LargeRumble),
                ToUShortMotor(feedback.SmallRumble)),
            new ControllerLight(
                feedback.LedRed,
                feedback.LedGreen,
                feedback.LedBlue,
                feedback.FlashOn,
                feedback.FlashOff));
    }

    // MARK: Private
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

    private static Ds4Buttons ToDs4Buttons(
        ControllerButtons buttons,
        byte leftTrigger,
        byte rightTrigger)
    {
        Ds4Buttons output = Ds4Buttons.None;
        Map(buttons, ControllerButtons.South, ref output, Ds4Buttons.Cross);
        Map(buttons, ControllerButtons.East, ref output, Ds4Buttons.Circle);
        Map(buttons, ControllerButtons.West, ref output, Ds4Buttons.Square);
        Map(buttons, ControllerButtons.North, ref output, Ds4Buttons.Triangle);
        Map(buttons, ControllerButtons.Back, ref output, Ds4Buttons.Share);
        Map(buttons, ControllerButtons.Guide, ref output, Ds4Buttons.PlayStation);
        Map(buttons, ControllerButtons.Start, ref output, Ds4Buttons.Options);
        Map(buttons, ControllerButtons.LeftStick, ref output, Ds4Buttons.L3);
        Map(buttons, ControllerButtons.RightStick, ref output, Ds4Buttons.R3);
        Map(buttons, ControllerButtons.LeftShoulder, ref output, Ds4Buttons.L1);
        Map(buttons, ControllerButtons.RightShoulder, ref output, Ds4Buttons.R1);

        if (leftTrigger != 0)
        {
            output |= Ds4Buttons.L2;
        }

        if (rightTrigger != 0)
        {
            output |= Ds4Buttons.R2;
        }

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

    private static void Map(
        ControllerButtons input,
        ControllerButtons inputButton,
        ref Ds4Buttons output,
        Ds4Buttons outputButton)
    {
        if ((input & inputButton) != 0)
        {
            output |= outputButton;
        }
    }

    private static Ds4DPad ToDs4DPad(ControllerButtons buttons)
    {
        bool up = buttons.HasFlag(ControllerButtons.DPadUp);
        bool down = buttons.HasFlag(ControllerButtons.DPadDown);
        bool left = buttons.HasFlag(ControllerButtons.DPadLeft);
        bool right = buttons.HasFlag(ControllerButtons.DPadRight);

        bool verticalConflict = up && down;
        bool horizontalConflict = left && right;
        return verticalConflict || horizontalConflict
            ? Ds4DPad.Neutral
            : (up, down, left, right) switch
            {
                (true, false, false, true) => Ds4DPad.UpRight,
                (false, true, false, true) => Ds4DPad.DownRight,
                (false, true, true, false) => Ds4DPad.DownLeft,
                (true, false, true, false) => Ds4DPad.UpLeft,
                (true, false, false, false) => Ds4DPad.Up,
                (false, true, false, false) => Ds4DPad.Down,
                (false, false, true, false) => Ds4DPad.Left,
                (false, false, false, true) => Ds4DPad.Right,
                _ => Ds4DPad.Neutral,
            };
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

    private static sbyte ToDs4Axis(short value)
    {
        return value < 0
            ? checked((sbyte)(value * 128 / 32768))
            : checked((sbyte)(value * 127 / 32767));
    }

    private static ushort ToTouchpadX(float value)
    {
        return ToTouchpadCoordinate(value, Ds4Report.TouchpadMaxX);
    }

    private static ushort ToTouchpadY(float value)
    {
        return ToTouchpadCoordinate(value, Ds4Report.TouchpadMaxY);
    }

    private static ushort ToTouchpadCoordinate(float value, ushort maximum)
    {
        return checked((ushort)Math.Clamp(MathF.Round(value * maximum), 0, maximum));
    }

    private static short ToDs4GyroRaw(float radiansPerSecond)
    {
        // VIIPER DS4 uses °/s scaled by 16. SDL exposes gyro in rad/s.
        // https://pkg.go.dev/github.com/Alia5/VIIPER/device/dualshock4
        return ToInt16Raw(radiansPerSecond * RadiansToDegrees * Ds4GyroCountsPerDegreePerSecond);
    }

    private static short ToDs4AccelRaw(float metersPerSecondSquared)
    {
        // VIIPER DS4 uses m/s² scaled by 512, which already matches SDL's accel unit.
        // https://pkg.go.dev/github.com/Alia5/VIIPER/device/dualshock4
        return ToInt16Raw(metersPerSecondSquared * Ds4AccelCountsPerMeterPerSecondSquared);
    }

    private static short ToInt16Raw(float value)
    {
        return checked((short)Math.Clamp(MathF.Round(value), short.MinValue, short.MaxValue));
    }
}
