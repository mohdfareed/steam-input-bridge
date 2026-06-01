using System;
using System.Buffers.Binary;
using SteamInputBridge.Inputs.Mouse;

namespace SteamInputBridge.Outputs.Teensy;

internal static class TeensyProtocol
{
    internal const int HeaderSize = 7;
    internal const int PayloadSize = 8;
    internal const int ChecksumSize = 2;
    internal const int FrameSize = HeaderSize + PayloadSize + ChecksumSize;

    private const byte Magic0 = (byte)'S';
    private const byte Magic1 = (byte)'I';
    private const byte Magic2 = (byte)'B';
    private const byte Version = 1;
    private const byte MouseReportType = 0x01;

    public static int WriteMouseReport(Span<byte> destination, byte sequence, in MouseReport report)
    {
        if (destination.Length < FrameSize)
        {
            throw new ArgumentException("Destination is too small.", nameof(destination));
        }

        destination[0] = Magic0;
        destination[1] = Magic1;
        destination[2] = Magic2;
        destination[3] = Version;
        destination[4] = MouseReportType;
        destination[5] = sequence;
        destination[6] = PayloadSize;

        Span<byte> payload = destination.Slice(HeaderSize, PayloadSize);
        BinaryPrimitives.WriteUInt16LittleEndian(payload, unchecked((ushort)report.Buttons));
        BinaryPrimitives.WriteInt16LittleEndian(payload[2..], ClampToInt16(report.DeltaX));
        BinaryPrimitives.WriteInt16LittleEndian(payload[4..], ClampToInt16(report.DeltaY));
        BinaryPrimitives.WriteInt16LittleEndian(payload[6..], ClampToInt16(report.WheelDelta));

        ushort checksum = ComputeCrc16(destination[..(HeaderSize + PayloadSize)]);
        BinaryPrimitives.WriteUInt16LittleEndian(destination[(HeaderSize + PayloadSize)..], checksum);
        return FrameSize;
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

    private static short ClampToInt16(int value)
    {
        return value > short.MaxValue
            ? short.MaxValue
            : value < short.MinValue ? short.MinValue : (short)value;
    }
}
