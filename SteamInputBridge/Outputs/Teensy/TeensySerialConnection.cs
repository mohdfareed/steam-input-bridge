using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;

namespace SteamInputBridge.Outputs.Teensy;

internal class TeensySerialConnection : IDisposable
{
    private const int BaudRate = 115200;
    private SerialPort? _port;

    public virtual bool IsConnected => _port?.IsOpen == true;

    public virtual string? PortName => IsConnected ? _port?.PortName : null;

    public virtual bool TryConnect(IReadOnlyList<string> candidatePorts)
    {
        Close();
        foreach (string portName in candidatePorts)
        {
            SerialPort port = CreatePort(portName);
            try
            {
                port.Open();
                _port = port;
                return true;
            }
            catch (Exception exception) when (IsSerialFailure(exception))
            {
                port.Dispose();
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
            ReadTimeout = 5,
            WriteTimeout = 5,
            DtrEnable = true,
            RtsEnable = true,
        };
    }

    private static bool IsSerialFailure(Exception exception)
    {
        return exception is IOException or InvalidOperationException or TimeoutException or UnauthorizedAccessException;
    }
}
