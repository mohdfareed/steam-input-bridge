using System;
using System.Buffers.Binary;
using System.IO;

namespace VirtualMouse.Forwarding;

// MARK: Definitions
// ============================================================================

internal static class ControllerPipeFrame
{
    public const int Size = 65;

    private struct FrameDefinition
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
        public const int HasTouchpadOffset = 46;
        public const int TouchXOffset = 47;
        public const int TouchYOffset = 51;
        public const int RumbleLowOffset = 55;
        public const int RumbleHighOffset = 57;
        public const int LightRedOffset = 59;
        public const int LightGreenOffset = 60;
        public const int LightBlueOffset = 61;
        public const int AdaptiveLeftOffset = 62;
        public const int AdaptiveRightOffset = 63;
        public const int FeedbackFlagsOffset = 64;
    }

    // MARK: Frames
    // =========================================================================

    public static void WriteInput(Span<byte> buffer, ControllerInputFrame frame)
    {
        Validate(buffer);
        buffer.Clear();
        buffer[FrameDefinition.TypeOffset] = (byte)ControllerPipeFrameType.Input;
        WriteUInt16(buffer, FrameDefinition.ControllerIndexOffset, frame.ControllerIndex);
        WriteState(buffer, frame.State);
    }

    public static void WriteFeedback(Span<byte> buffer, ControllerFeedbackFrame frame)
    {
        Validate(buffer);
        buffer.Clear();
        buffer[FrameDefinition.TypeOffset] = (byte)ControllerPipeFrameType.Feedback;
        WriteUInt16(buffer, FrameDefinition.ControllerIndexOffset, frame.ControllerIndex);
        WriteFeedbackPayload(buffer, frame.Feedback);
    }

    public static ControllerPipeMessage Read(ReadOnlySpan<byte> buffer)
    {
        Validate(buffer);
        ControllerPipeFrameType type = (ControllerPipeFrameType)buffer[FrameDefinition.TypeOffset];
        ushort controllerIndex = ReadUInt16(buffer, FrameDefinition.ControllerIndexOffset);

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
            buffer[FrameDefinition.HasStandardOffset] = 1;
            WriteUInt32(buffer, FrameDefinition.ButtonsOffset, (uint)standard.Buttons);
            WriteInt16(buffer, FrameDefinition.LeftXOffset, standard.LeftX);
            WriteInt16(buffer, FrameDefinition.LeftYOffset, standard.LeftY);
            WriteInt16(buffer, FrameDefinition.RightXOffset, standard.RightX);
            WriteInt16(buffer, FrameDefinition.RightYOffset, standard.RightY);
            WriteUInt16(buffer, FrameDefinition.LeftTriggerOffset, standard.LeftTrigger);
            WriteUInt16(buffer, FrameDefinition.RightTriggerOffset, standard.RightTrigger);
        }

        if (state.Motion is { } motion)
        {
            buffer[FrameDefinition.HasMotionOffset] = motion.HasGyro ? (byte)1 : (byte)0;
            WriteSingle(buffer, FrameDefinition.GyroXOffset, motion.GyroX);
            WriteSingle(buffer, FrameDefinition.GyroYOffset, motion.GyroY);
            WriteSingle(buffer, FrameDefinition.GyroZOffset, motion.GyroZ);
            buffer[FrameDefinition.HasAccelerometerOffset] = motion.HasAccelerometer ? (byte)1 : (byte)0;
            WriteSingle(buffer, FrameDefinition.AccelXOffset, motion.AccelX);
            WriteSingle(buffer, FrameDefinition.AccelYOffset, motion.AccelY);
            WriteSingle(buffer, FrameDefinition.AccelZOffset, motion.AccelZ);
        }

        if (state.Touchpad is { } touchpad)
        {
            buffer[FrameDefinition.HasTouchpadOffset] = touchpad.IsTouched ? (byte)1 : (byte)0;
            WriteSingle(buffer, FrameDefinition.TouchXOffset, touchpad.X);
            WriteSingle(buffer, FrameDefinition.TouchYOffset, touchpad.Y);
        }
    }

    private static ControllerState ReadState(ReadOnlySpan<byte> buffer)
    {
        ControllerStandardState? standard = buffer[FrameDefinition.HasStandardOffset] == 0
            ? null
            : new ControllerStandardState(
                (ControllerButtons)ReadUInt32(buffer, FrameDefinition.ButtonsOffset),
                ReadInt16(buffer, FrameDefinition.LeftXOffset),
                ReadInt16(buffer, FrameDefinition.LeftYOffset),
                ReadInt16(buffer, FrameDefinition.RightXOffset),
                ReadInt16(buffer, FrameDefinition.RightYOffset),
                ReadUInt16(buffer, FrameDefinition.LeftTriggerOffset),
                ReadUInt16(buffer, FrameDefinition.RightTriggerOffset));

        ControllerMotionState? motion =
            buffer[FrameDefinition.HasMotionOffset] == 0 &&
            buffer[FrameDefinition.HasAccelerometerOffset] == 0
            ? null
            : new ControllerMotionState(
                buffer[FrameDefinition.HasMotionOffset] != 0,
                ReadSingle(buffer, FrameDefinition.GyroXOffset),
                ReadSingle(buffer, FrameDefinition.GyroYOffset),
                ReadSingle(buffer, FrameDefinition.GyroZOffset),
                buffer[FrameDefinition.HasAccelerometerOffset] != 0,
                ReadSingle(buffer, FrameDefinition.AccelXOffset),
                ReadSingle(buffer, FrameDefinition.AccelYOffset),
                ReadSingle(buffer, FrameDefinition.AccelZOffset));

        ControllerTouchpadState? touchpad = buffer[FrameDefinition.HasTouchpadOffset] == 0
            ? null
            : new ControllerTouchpadState(
                true,
                ReadSingle(buffer, FrameDefinition.TouchXOffset),
                ReadSingle(buffer, FrameDefinition.TouchYOffset));

        return new ControllerState(standard, motion, touchpad);
    }

    private static void WriteFeedbackPayload(Span<byte> buffer, ControllerFeedback feedback)
    {
        byte flags = 0;

        if (feedback.Rumble is { } rumble)
        {
            flags |= 1;
            WriteUInt16(buffer, FrameDefinition.RumbleLowOffset, rumble.LowFrequency);
            WriteUInt16(buffer, FrameDefinition.RumbleHighOffset, rumble.HighFrequency);
        }

        if (feedback.Light is { } light)
        {
            flags |= 2;
            buffer[FrameDefinition.LightRedOffset] = light.Red;
            buffer[FrameDefinition.LightGreenOffset] = light.Green;
            buffer[FrameDefinition.LightBlueOffset] = light.Blue;
        }

        if (feedback.AdaptiveTriggers is { } adaptive)
        {
            flags |= 4;
            buffer[FrameDefinition.AdaptiveLeftOffset] = adaptive.LeftMode;
            buffer[FrameDefinition.AdaptiveRightOffset] = adaptive.RightMode;
        }

        buffer[FrameDefinition.FeedbackFlagsOffset] = flags;
    }

    private static ControllerFeedback ReadFeedbackPayload(ReadOnlySpan<byte> buffer)
    {
        byte flags = buffer[FrameDefinition.FeedbackFlagsOffset];

        ControllerRumble? rumble = (flags & 1) == 0
            ? null
            : new ControllerRumble(
                ReadUInt16(buffer, FrameDefinition.RumbleLowOffset),
                ReadUInt16(buffer, FrameDefinition.RumbleHighOffset));

        ControllerLight? light = (flags & 2) == 0
            ? null
            : new ControllerLight(
                buffer[FrameDefinition.LightRedOffset],
                buffer[FrameDefinition.LightGreenOffset],
                buffer[FrameDefinition.LightBlueOffset]);

        ControllerAdaptiveTriggers? adaptive = (flags & 4) == 0
            ? null
            : new ControllerAdaptiveTriggers(
                buffer[FrameDefinition.AdaptiveLeftOffset],
                buffer[FrameDefinition.AdaptiveRightOffset]);

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
