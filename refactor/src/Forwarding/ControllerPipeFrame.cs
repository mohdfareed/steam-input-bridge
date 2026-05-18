using System;
using System.Buffers.Binary;
using System.IO;

namespace VirtualMouse.Forwarding;

internal static class ControllerPipeFrame
{
    private const int TypeOffset = 0;
    private const int ControllerIndexOffset = 1;
    private const int ButtonsOffset = 3;
    private const int LeftXOffset = 7;
    private const int LeftYOffset = 9;
    private const int RightXOffset = 11;
    private const int RightYOffset = 13;
    private const int LeftTriggerOffset = 15;
    private const int RightTriggerOffset = 17;
    private const int HasStandardOffset = 19;
    private const int HasMotionOffset = 20;
    private const int GyroXOffset = 21;
    private const int GyroYOffset = 25;
    private const int GyroZOffset = 29;
    private const int HasAccelerometerOffset = 33;
    private const int AccelXOffset = 34;
    private const int AccelYOffset = 38;
    private const int AccelZOffset = 42;
    private const int HasTouchpadOffset = 46;
    private const int TouchXOffset = 47;
    private const int TouchYOffset = 51;
    private const int RumbleLowOffset = 55;
    private const int RumbleHighOffset = 57;
    private const int LightRedOffset = 59;
    private const int LightGreenOffset = 60;
    private const int LightBlueOffset = 61;
    private const int AdaptiveLeftOffset = 62;
    private const int AdaptiveRightOffset = 63;
    private const int FeedbackFlagsOffset = 64;

    public const int Size = 65;

    // MARK: Frames
    // =========================================================================

    public static void WriteInput(Span<byte> buffer, ControllerInputFrame frame)
    {
        Validate(buffer);
        buffer.Clear();
        buffer[TypeOffset] = (byte)ControllerPipeFrameType.Input;
        WriteUInt16(buffer, ControllerIndexOffset, frame.ControllerIndex);
        WriteState(buffer, frame.State);
    }

    public static void WriteFeedback(Span<byte> buffer, ControllerFeedbackFrame frame)
    {
        Validate(buffer);
        buffer.Clear();
        buffer[TypeOffset] = (byte)ControllerPipeFrameType.Feedback;
        WriteUInt16(buffer, ControllerIndexOffset, frame.ControllerIndex);
        WriteFeedbackPayload(buffer, frame.Feedback);
    }

    public static ControllerPipeMessage Read(ReadOnlySpan<byte> buffer)
    {
        Validate(buffer);
        ControllerPipeFrameType type = (ControllerPipeFrameType)buffer[TypeOffset];
        ushort controllerIndex = ReadUInt16(buffer, ControllerIndexOffset);
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
            buffer[HasStandardOffset] = 1;
            WriteUInt32(buffer, ButtonsOffset, (uint)standard.Buttons);
            WriteInt16(buffer, LeftXOffset, standard.LeftX);
            WriteInt16(buffer, LeftYOffset, standard.LeftY);
            WriteInt16(buffer, RightXOffset, standard.RightX);
            WriteInt16(buffer, RightYOffset, standard.RightY);
            WriteUInt16(buffer, LeftTriggerOffset, standard.LeftTrigger);
            WriteUInt16(buffer, RightTriggerOffset, standard.RightTrigger);
        }

        if (state.Motion is { } motion)
        {
            buffer[HasMotionOffset] = motion.HasGyro ? (byte)1 : (byte)0;
            WriteSingle(buffer, GyroXOffset, motion.GyroX);
            WriteSingle(buffer, GyroYOffset, motion.GyroY);
            WriteSingle(buffer, GyroZOffset, motion.GyroZ);
            buffer[HasAccelerometerOffset] = motion.HasAccelerometer ? (byte)1 : (byte)0;
            WriteSingle(buffer, AccelXOffset, motion.AccelX);
            WriteSingle(buffer, AccelYOffset, motion.AccelY);
            WriteSingle(buffer, AccelZOffset, motion.AccelZ);
        }

        if (state.Touchpad is { } touchpad)
        {
            buffer[HasTouchpadOffset] = touchpad.IsTouched ? (byte)1 : (byte)0;
            WriteSingle(buffer, TouchXOffset, touchpad.X);
            WriteSingle(buffer, TouchYOffset, touchpad.Y);
        }
    }

    private static ControllerState ReadState(ReadOnlySpan<byte> buffer)
    {
        ControllerStandardState? standard = buffer[HasStandardOffset] == 0
            ? null
            : new ControllerStandardState(
                (ControllerButtons)ReadUInt32(buffer, ButtonsOffset),
                ReadInt16(buffer, LeftXOffset),
                ReadInt16(buffer, LeftYOffset),
                ReadInt16(buffer, RightXOffset),
                ReadInt16(buffer, RightYOffset),
                ReadUInt16(buffer, LeftTriggerOffset),
                ReadUInt16(buffer, RightTriggerOffset));
        ControllerMotionState? motion = buffer[HasMotionOffset] == 0 && buffer[HasAccelerometerOffset] == 0
            ? null
            : new ControllerMotionState(
                buffer[HasMotionOffset] != 0,
                ReadSingle(buffer, GyroXOffset),
                ReadSingle(buffer, GyroYOffset),
                ReadSingle(buffer, GyroZOffset),
                buffer[HasAccelerometerOffset] != 0,
                ReadSingle(buffer, AccelXOffset),
                ReadSingle(buffer, AccelYOffset),
                ReadSingle(buffer, AccelZOffset));
        ControllerTouchpadState? touchpad = buffer[HasTouchpadOffset] == 0
            ? null
            : new ControllerTouchpadState(true, ReadSingle(buffer, TouchXOffset), ReadSingle(buffer, TouchYOffset));
        return new ControllerState(standard, motion, touchpad);
    }

    private static void WriteFeedbackPayload(Span<byte> buffer, ControllerFeedback feedback)
    {
        byte flags = 0;
        if (feedback.Rumble is { } rumble)
        {
            flags |= 1;
            WriteUInt16(buffer, RumbleLowOffset, rumble.LowFrequency);
            WriteUInt16(buffer, RumbleHighOffset, rumble.HighFrequency);
        }

        if (feedback.Light is { } light)
        {
            flags |= 2;
            buffer[LightRedOffset] = light.Red;
            buffer[LightGreenOffset] = light.Green;
            buffer[LightBlueOffset] = light.Blue;
        }

        if (feedback.AdaptiveTriggers is { } adaptive)
        {
            flags |= 4;
            buffer[AdaptiveLeftOffset] = adaptive.LeftMode;
            buffer[AdaptiveRightOffset] = adaptive.RightMode;
        }

        buffer[FeedbackFlagsOffset] = flags;
    }

    private static ControllerFeedback ReadFeedbackPayload(ReadOnlySpan<byte> buffer)
    {
        byte flags = buffer[FeedbackFlagsOffset];
        ControllerRumble? rumble = (flags & 1) == 0
            ? null
            : new ControllerRumble(ReadUInt16(buffer, RumbleLowOffset), ReadUInt16(buffer, RumbleHighOffset));
        ControllerLight? light = (flags & 2) == 0
            ? null
            : new ControllerLight(buffer[LightRedOffset], buffer[LightGreenOffset], buffer[LightBlueOffset]);
        ControllerAdaptiveTriggers? adaptive = (flags & 4) == 0
            ? null
            : new ControllerAdaptiveTriggers(buffer[AdaptiveLeftOffset], buffer[AdaptiveRightOffset]);
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
