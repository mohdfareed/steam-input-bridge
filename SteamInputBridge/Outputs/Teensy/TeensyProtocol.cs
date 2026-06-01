using System;
using System.Buffers.Binary;
using SteamInputBridge.Inputs.Mouse;

namespace SteamInputBridge.Outputs.Teensy;

internal static class TeensyProtocol
{
    internal const int HeaderSize = 7;
    internal const int MousePayloadSize = 8;
    internal const int ChecksumSize = 2;
    internal const int FrameSize = HeaderSize + MousePayloadSize + ChecksumSize;
    internal const int HandshakeProbeFrameSize = HeaderSize + ChecksumSize;
    internal const int HandshakeResponsePayloadSize = 4;
    internal const int HandshakeResponseFrameSize = HeaderSize + HandshakeResponsePayloadSize + ChecksumSize;

    internal const byte Magic0 = (byte)'S';
    internal const byte Magic1 = (byte)'I';
    internal const byte Magic2 = (byte)'B';
    internal const byte Version = 1;

    private const byte HandshakeProbeType = 0x00;
    private const byte MouseReportType = 0x01;
    private const byte HandshakeResponseType = 0x80;

    private static readonly byte[] HandshakeResponsePayload =
    [
        (byte)'T',
        (byte)'N',
        (byte)'S',
        (byte)'Y',
    ];

    public static int WriteHandshakeProbe(Span<byte> destination, byte sequence)
    {
        if (destination.Length < HandshakeProbeFrameSize)
        {
            throw new ArgumentException("Destination is too small.", nameof(destination));
        }

        WriteHeader(destination, HandshakeProbeType, sequence, payloadSize: 0);
        WriteChecksum(destination, payloadSize: 0);
        return HandshakeProbeFrameSize;
    }

    public static int WriteMouseReport(Span<byte> destination, byte sequence, in MouseReport report)
    {
        if (destination.Length < FrameSize)
        {
            throw new ArgumentException("Destination is too small.", nameof(destination));
        }

        WriteHeader(destination, MouseReportType, sequence, MousePayloadSize);

        Span<byte> payload = destination.Slice(HeaderSize, MousePayloadSize);
        BinaryPrimitives.WriteUInt16LittleEndian(payload, unchecked((ushort)report.Buttons));
        BinaryPrimitives.WriteInt16LittleEndian(payload[2..], ClampToInt16(report.DeltaX));
        BinaryPrimitives.WriteInt16LittleEndian(payload[4..], ClampToInt16(report.DeltaY));
        BinaryPrimitives.WriteInt16LittleEndian(payload[6..], ClampToInt16(report.WheelDelta));

        WriteChecksum(destination, MousePayloadSize);
        return FrameSize;
    }

    public static bool IsHandshakeResponse(ReadOnlySpan<byte> frame, byte sequence)
    {
        if (frame.Length < HandshakeResponseFrameSize)
        {
            return false;
        }

        if (!HasHeader(frame, HandshakeResponseType, sequence, HandshakeResponsePayloadSize))
        {
            return false;
        }

        if (!frame.Slice(HeaderSize, HandshakeResponsePayloadSize).SequenceEqual(HandshakeResponsePayload))
        {
            return false;
        }

        ushort expected = ComputeCrc16(frame[..(HeaderSize + HandshakeResponsePayloadSize)]);
        ushort actual = BinaryPrimitives.ReadUInt16LittleEndian(
            frame[(HeaderSize + HandshakeResponsePayloadSize)..HandshakeResponseFrameSize]);
        return expected == actual;
    }

    internal static bool TryReadHandshakeResponseByte(byte value, byte sequence, Span<byte> frame, ref int offset)
    {
        if (offset == 0 && value != Magic0)
        {
            return false;
        }

        frame[offset++] = value;
        if ((offset == 2 && frame[1] != Magic1) ||
            (offset == 3 && frame[2] != Magic2) ||
            (offset == 4 && frame[3] != Version) ||
            (offset == 5 && frame[4] != HandshakeResponseType) ||
            (offset == 7 && frame[6] != HandshakeResponsePayloadSize))
        {
            ResetFrame(value, frame, ref offset);
            return false;
        }

        if (offset != HandshakeResponseFrameSize)
        {
            return false;
        }

        bool valid = IsHandshakeResponse(frame, sequence);
        offset = 0;
        return valid;
    }

    internal static ushort ComputeCrc16(ReadOnlySpan<byte> data)
    {
        ushort crc = 0xFFFF;
        foreach (byte value in data)
        {
            crc ^= (ushort)(value << 8);
            for (int bit = 0; bit < 8; bit++)
            {
                crc = (crc & 0x8000) != 0
                    ? (ushort)((crc << 1) ^ 0x1021)
                    : (ushort)(crc << 1);
            }
        }

        return crc;
    }

    private static void WriteHeader(Span<byte> destination, byte type, byte sequence, int payloadSize)
    {
        destination[0] = Magic0;
        destination[1] = Magic1;
        destination[2] = Magic2;
        destination[3] = Version;
        destination[4] = type;
        destination[5] = sequence;
        destination[6] = checked((byte)payloadSize);
    }

    private static void WriteChecksum(Span<byte> destination, int payloadSize)
    {
        ushort checksum = ComputeCrc16(destination[..(HeaderSize + payloadSize)]);
        BinaryPrimitives.WriteUInt16LittleEndian(destination[(HeaderSize + payloadSize)..], checksum);
    }

    private static bool HasHeader(ReadOnlySpan<byte> frame, byte type, byte sequence, int payloadSize)
    {
        return frame[0] == Magic0 &&
            frame[1] == Magic1 &&
            frame[2] == Magic2 &&
            frame[3] == Version &&
            frame[4] == type &&
            frame[5] == sequence &&
            frame[6] == payloadSize;
    }

    private static void ResetFrame(byte firstByte, Span<byte> frame, ref int offset)
    {
        offset = 0;
        if (firstByte == Magic0)
        {
            frame[offset++] = firstByte;
        }
    }

    private static short ClampToInt16(int value)
    {
        return value > short.MaxValue
            ? short.MaxValue
            : value < short.MinValue ? short.MinValue : (short)value;
    }
}
