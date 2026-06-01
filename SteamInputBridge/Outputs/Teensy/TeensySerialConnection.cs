using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;

namespace SteamInputBridge.Outputs.Teensy;

internal class TeensySerialConnection : IDisposable
{
    private const int BaudRate = 115_200;
    private static readonly TimeSpan HandshakeTimeout = TimeSpan.FromMilliseconds(750);
    private readonly byte[] _handshakeProbe = new byte[TeensyProtocol.HandshakeProbeFrameSize];
    private readonly byte[] _handshakeResponse = new byte[TeensyProtocol.HandshakeResponseFrameSize];
    private SerialPort? _port;
    private byte _sequence;

    public virtual bool IsConnected => _port?.IsOpen == true;

    public virtual string? PortName => IsConnected ? _port?.PortName : null;

    public virtual bool TryConnect(IReadOnlyList<string> candidatePorts)
    {
        Close();
        foreach (string portName in candidatePorts)
        {
            SerialPort? port = null;
            try
            {
#pragma warning disable CA2000 // Disposed in finally unless ownership transfers to _port after handshake.
                port = CreatePort(portName);
#pragma warning restore CA2000
                port.Open();
                if (TryHandshake(port))
                {
                    _port = port;
                    port = null;
                    return true;
                }
            }
            catch (Exception exception) when (IsSerialFailure(exception))
            {
            }
            finally
            {
                port?.Dispose();
            }
        }

        return false;
    }

    public virtual bool TryWrite(byte[] frame, int bytes)
    {
        if (!IsConnected || _port is null)
        {
            return false;
        }

        try
        {
            _port.Write(frame, 0, bytes);
            return true;
        }
        catch (Exception exception) when (IsSerialFailure(exception))
        {
            Close();
            return false;
        }
    }

    public virtual void Close()
    {
        try
        {
            _port?.Dispose();
        }
        finally
        {
            _port = null;
        }
    }

    public void Dispose()
    {
        Close();
    }

    private static SerialPort CreatePort(string portName)
    {
        return new(portName, BaudRate)
        {
            ReadTimeout = 50,
            WriteTimeout = 100,
            DtrEnable = true,
            RtsEnable = true,
        };
    }

    private bool TryHandshake(SerialPort port)
    {
        byte sequence = _sequence++;
        int probeBytes = TeensyProtocol.WriteHandshakeProbe(_handshakeProbe, sequence);

        port.DiscardInBuffer();
        port.Write(_handshakeProbe, 0, probeBytes);

        return TryReadHandshakeResponse(port, sequence, HandshakeTimeout);
    }

    private bool TryReadHandshakeResponse(SerialPort port, byte sequence, TimeSpan timeout)
    {
        int offset = 0;
        DateTime deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            int remainingMs = (int)Math.Ceiling((deadline - DateTime.UtcNow).TotalMilliseconds);
            if (remainingMs <= 0)
            {
                return false;
            }

            port.ReadTimeout = Math.Min(50, remainingMs);
            try
            {
                int value = port.ReadByte();
                if (value < 0)
                {
                    continue;
                }

                if (TeensyProtocol.TryReadHandshakeResponseByte((byte)value, sequence, _handshakeResponse, ref offset))
                {
                    return true;
                }
            }
            catch (TimeoutException)
            {
            }
        }

        return false;
    }

    private static bool IsSerialFailure(Exception exception)
    {
        return exception is IOException or InvalidOperationException or TimeoutException or UnauthorizedAccessException;
    }
}
