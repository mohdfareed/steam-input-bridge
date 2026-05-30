using System;
using System.Buffers.Binary;
using System.IO;
using SteamInputBridge.Forwarding.Controller;

namespace SteamInputBridge.Forwarding;

// MARK: Definitions
// ============================================================================

internal static class ControllerPipeFrame
{
    public const int Size = Layout.LightFlashOffOffset + sizeof(byte);

    private const byte Present = 1;

    [Flags]
    private enum FeedbackFields : byte
    {
        None = 0,
        Rumble = 1,
        Light = 2,
        AdaptiveTriggers = 4,
    }

    private static class Layout
    {
        public const int TypeOffset = 0;
        public const int ControllerIndexOffset = 1;
        public const int ButtonsOffset = 3;
        public const int LeftXOffset = 7;
        public const int LeftYOffset = 9;
        public const int RightXOffset = 11;
        public const int RightYOffset = 13;
        public const int LeftTriggerOffset = 15;
        public const int RightTriggerOffset = 17;
        public const int HasStandardOffset = 19;
        public const int HasMotionOffset = 20;
        public const int GyroXOffset = 21;
        public const int GyroYOffset = 25;
        public const int GyroZOffset = 29;
        public const int HasAccelerometerOffset = 33;
        public const int AccelXOffset = 34;
        public const int AccelYOffset = 38;
        public const int AccelZOffset = 42;
        public const int TouchpadFlagsOffset = 46;
        public const int TouchXOffset = 47;
        public const int TouchYOffset = 51;
        public const int TouchPressureOffset = 55;
        public const int Touch2XOffset = 59;
        public const int Touch2YOffset = 63;
        public const int Touch2PressureOffset = 67;
        public const int RumbleLowOffset = 71;
        public const int RumbleHighOffset = 73;
        public const int LightRedOffset = 75;
        public const int LightGreenOffset = 76;
        public const int LightBlueOffset = 77;
        public const int AdaptiveLeftOffset = 78;
        public const int AdaptiveRightOffset = 79;
        public const int FeedbackFlagsOffset = 80;
        public const int LightFlashOnOffset = 81;
        public const int LightFlashOffOffset = 82;
    }

    // MARK: Frames
    // =========================================================================

    internal static void WriteInput(Span<byte> buffer, ControllerInputFrame frame)
    {
        Validate(buffer);
        buffer.Clear();
        buffer[Layout.TypeOffset] = (byte)ControllerPipeFrameType.Input;
        WriteUInt16(buffer, Layout.ControllerIndexOffset, frame.ControllerIndex);
        WriteState(buffer, frame.State);
    }

    internal static void WriteFeedback(Span<byte> buffer, ControllerFeedbackFrame frame)
    {
        Validate(buffer);
        buffer.Clear();
        buffer[Layout.TypeOffset] = (byte)ControllerPipeFrameType.Feedback;
        WriteUInt16(buffer, Layout.ControllerIndexOffset, frame.ControllerIndex);
        WriteFeedbackPayload(buffer, frame.Feedback);
    }

    internal static ControllerPipeMessage Read(ReadOnlySpan<byte> buffer)
    {
        Validate(buffer);
        ControllerPipeFrameType type = (ControllerPipeFrameType)buffer[Layout.TypeOffset];
        ushort controllerIndex = ReadUInt16(buffer, Layout.ControllerIndexOffset);

        return type switch
        {
            ControllerPipeFrameType.None => throw new InvalidDataException("Missing controller pipe frame type."),
            ControllerPipeFrameType.Input => new ControllerPipeMessage(
                type,
                new ControllerInputFrame(controllerIndex, ReadState(buffer)),
                default),
            ControllerPipeFrameType.Feedback => new ControllerPipeMessage(
                type,
                default,
                new ControllerFeedbackFrame(controllerIndex, ReadFeedbackPayload(buffer))),
            _ => throw new InvalidDataException("Unknown controller pipe frame type."),
        };
    }

    // MARK: Payloads
    // =========================================================================

    private static void WriteState(Span<byte> buffer, ControllerState state)
    {
        if (state.Standard is { } standard)
        {
            buffer[Layout.HasStandardOffset] = Present;
            WriteUInt32(buffer, Layout.ButtonsOffset, (uint)standard.Buttons);
            WriteInt16(buffer, Layout.LeftXOffset, standard.LeftX);
            WriteInt16(buffer, Layout.LeftYOffset, standard.LeftY);
            WriteInt16(buffer, Layout.RightXOffset, standard.RightX);
            WriteInt16(buffer, Layout.RightYOffset, standard.RightY);
            WriteUInt16(buffer, Layout.LeftTriggerOffset, standard.LeftTrigger);
            WriteUInt16(buffer, Layout.RightTriggerOffset, standard.RightTrigger);
        }

        if (state.Motion is { } motion)
        {
            buffer[Layout.HasMotionOffset] = motion.HasGyro ? Present : (byte)0;
            WriteSingle(buffer, Layout.GyroXOffset, motion.GyroX);
            WriteSingle(buffer, Layout.GyroYOffset, motion.GyroY);
            WriteSingle(buffer, Layout.GyroZOffset, motion.GyroZ);
            buffer[Layout.HasAccelerometerOffset] = motion.HasAccelerometer ? Present : (byte)0;
            WriteSingle(buffer, Layout.AccelXOffset, motion.AccelX);
            WriteSingle(buffer, Layout.AccelYOffset, motion.AccelY);
            WriteSingle(buffer, Layout.AccelZOffset, motion.AccelZ);
        }

        if (state.Touchpad is { } touchpad)
        {
            buffer[Layout.TouchpadFlagsOffset] =
                (byte)((touchpad.Touch1.IsTouched ? 1 : 0) | (touchpad.Touch2.IsTouched ? 2 : 0));
            WriteSingle(buffer, Layout.TouchXOffset, touchpad.Touch1.X);
            WriteSingle(buffer, Layout.TouchYOffset, touchpad.Touch1.Y);
            WriteSingle(buffer, Layout.TouchPressureOffset, touchpad.Touch1.Pressure);
            WriteSingle(buffer, Layout.Touch2XOffset, touchpad.Touch2.X);
            WriteSingle(buffer, Layout.Touch2YOffset, touchpad.Touch2.Y);
            WriteSingle(buffer, Layout.Touch2PressureOffset, touchpad.Touch2.Pressure);
        }
    }

    private static ControllerState ReadState(ReadOnlySpan<byte> buffer)
    {
        ControllerStandardState? standard = buffer[Layout.HasStandardOffset] == 0
            ? null
            : new ControllerStandardState(
                (ControllerButtons)ReadUInt32(buffer, Layout.ButtonsOffset),
                ReadInt16(buffer, Layout.LeftXOffset),
                ReadInt16(buffer, Layout.LeftYOffset),
                ReadInt16(buffer, Layout.RightXOffset),
                ReadInt16(buffer, Layout.RightYOffset),
                ReadUInt16(buffer, Layout.LeftTriggerOffset),
                ReadUInt16(buffer, Layout.RightTriggerOffset));

        ControllerMotionState? motion =
            buffer[Layout.HasMotionOffset] == 0 &&
            buffer[Layout.HasAccelerometerOffset] == 0
            ? null
            : new ControllerMotionState(
                buffer[Layout.HasMotionOffset] != 0,
                ReadSingle(buffer, Layout.GyroXOffset),
                ReadSingle(buffer, Layout.GyroYOffset),
                ReadSingle(buffer, Layout.GyroZOffset),
                buffer[Layout.HasAccelerometerOffset] != 0,
                ReadSingle(buffer, Layout.AccelXOffset),
                ReadSingle(buffer, Layout.AccelYOffset),
                ReadSingle(buffer, Layout.AccelZOffset));

        byte touchpadFlags = buffer[Layout.TouchpadFlagsOffset];
        ControllerTouchpadState? touchpad = touchpadFlags == 0
            ? null
            : new ControllerTouchpadState(
                new ControllerTouchContact(
                    (touchpadFlags & 1) != 0,
                    ReadSingle(buffer, Layout.TouchXOffset),
                    ReadSingle(buffer, Layout.TouchYOffset),
                    ReadSingle(buffer, Layout.TouchPressureOffset)),
                new ControllerTouchContact(
                    (touchpadFlags & 2) != 0,
                    ReadSingle(buffer, Layout.Touch2XOffset),
                    ReadSingle(buffer, Layout.Touch2YOffset),
                    ReadSingle(buffer, Layout.Touch2PressureOffset)));

        return new ControllerState(standard, motion, touchpad);
    }

    private static void WriteFeedbackPayload(Span<byte> buffer, ControllerFeedback feedback)
    {
        FeedbackFields fields = FeedbackFields.None;

        if (feedback.Rumble is { } rumble)
        {
            fields |= FeedbackFields.Rumble;
            WriteUInt16(buffer, Layout.RumbleLowOffset, rumble.LowFrequency);
            WriteUInt16(buffer, Layout.RumbleHighOffset, rumble.HighFrequency);
        }

        if (feedback.Light is { } light)
        {
            fields |= FeedbackFields.Light;
            buffer[Layout.LightRedOffset] = light.Red;
            buffer[Layout.LightGreenOffset] = light.Green;
            buffer[Layout.LightBlueOffset] = light.Blue;
            buffer[Layout.LightFlashOnOffset] = light.FlashOn;
            buffer[Layout.LightFlashOffOffset] = light.FlashOff;
        }

        if (feedback.AdaptiveTriggers is { } adaptive)
        {
            fields |= FeedbackFields.AdaptiveTriggers;
            buffer[Layout.AdaptiveLeftOffset] = adaptive.LeftMode;
            buffer[Layout.AdaptiveRightOffset] = adaptive.RightMode;
        }

        buffer[Layout.FeedbackFlagsOffset] = (byte)fields;
    }

    private static ControllerFeedback ReadFeedbackPayload(ReadOnlySpan<byte> buffer)
    {
        FeedbackFields fields = (FeedbackFields)buffer[Layout.FeedbackFlagsOffset];

        ControllerRumble? rumble = (fields & FeedbackFields.Rumble) == 0
            ? null
            : new ControllerRumble(
                ReadUInt16(buffer, Layout.RumbleLowOffset),
                ReadUInt16(buffer, Layout.RumbleHighOffset));

        ControllerLight? light = (fields & FeedbackFields.Light) == 0
            ? null
            : new ControllerLight(
                buffer[Layout.LightRedOffset],
                buffer[Layout.LightGreenOffset],
                buffer[Layout.LightBlueOffset],
                buffer[Layout.LightFlashOnOffset],
                buffer[Layout.LightFlashOffOffset]);

        ControllerAdaptiveTriggers? adaptive = (fields & FeedbackFields.AdaptiveTriggers) == 0
            ? null
            : new ControllerAdaptiveTriggers(
                buffer[Layout.AdaptiveLeftOffset],
                buffer[Layout.AdaptiveRightOffset]);

        return new ControllerFeedback(rumble, light, adaptive);
    }

    // MARK: Primitives
    // =========================================================================

    private static void Validate(ReadOnlySpan<byte> buffer)
    {
        if (buffer.Length < Size)
        {
            throw new ArgumentException("Controller pipe frame buffer is too small.", nameof(buffer));
        }
    }

    private static void WriteUInt32(Span<byte> buffer, int offset, uint value)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.Slice(offset, sizeof(uint)), value);
    }

    private static uint ReadUInt32(ReadOnlySpan<byte> buffer, int offset)
    {
        return BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(offset, sizeof(uint)));
    }

    private static void WriteUInt16(Span<byte> buffer, int offset, ushort value)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.Slice(offset, sizeof(ushort)), value);
    }

    private static ushort ReadUInt16(ReadOnlySpan<byte> buffer, int offset)
    {
        return BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(offset, sizeof(ushort)));
    }

    private static void WriteInt16(Span<byte> buffer, int offset, short value)
    {
        BinaryPrimitives.WriteInt16LittleEndian(buffer.Slice(offset, sizeof(short)), value);
    }

    private static short ReadInt16(ReadOnlySpan<byte> buffer, int offset)
    {
        return BinaryPrimitives.ReadInt16LittleEndian(buffer.Slice(offset, sizeof(short)));
    }

    private static void WriteSingle(Span<byte> buffer, int offset, float value)
    {
        BinaryPrimitives.WriteSingleLittleEndian(buffer.Slice(offset, sizeof(float)), value);
    }

    private static float ReadSingle(ReadOnlySpan<byte> buffer, int offset)
    {
        return BinaryPrimitives.ReadSingleLittleEndian(buffer.Slice(offset, sizeof(float)));
    }
}
