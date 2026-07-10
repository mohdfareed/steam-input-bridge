using System;
using System.Runtime.InteropServices;

namespace SteamInputBridge.Inputs.RawInput;

internal sealed class RawInputBuffer<T> : IDisposable where T : struct
{
    private static readonly uint HeaderSize = (uint)Marshal.SizeOf<RawInputNative.RawInputHeader>();
    private static readonly uint InputSize = (uint)Marshal.SizeOf<T>();
    private static readonly uint InitialBufferSize = InputSize * 64;

    private nint _buffer;
    private uint _bufferSize;

    public bool TryReadData(nint rawInputHandle, out T rawInput)
    {
        Ensure(InputSize);

        uint size = _bufferSize;
        uint read = RawInputNative.GetRawInputData(
            rawInputHandle,
            RawInputNative.Input,
            _buffer,
            ref size,
            HeaderSize);

        if (read == uint.MaxValue)
        {
            uint requiredSize = 0;
            _ = RawInputNative.GetRawInputData(
                rawInputHandle,
                RawInputNative.Input,
                nint.Zero,
                ref requiredSize,
                HeaderSize);

            if (requiredSize == 0)
            {
                rawInput = default;
                return false;
            }

            Ensure(requiredSize);
            size = _bufferSize;
            read = RawInputNative.GetRawInputData(
                rawInputHandle,
                RawInputNative.Input,
                _buffer,
                ref size,
                HeaderSize);
        }

        if (read == uint.MaxValue || read < InputSize)
        {
            rawInput = default;
            return false;
        }

        rawInput = Marshal.PtrToStructure<T>(_buffer);
        return true;
    }

    public void Drain(Action<T> handle, Action? beforeEach = null)
    {
        while (TryReadBuffer(out uint count))
        {
            nint current = _buffer;
            for (uint i = 0; i < count; i++)
            {
                beforeEach?.Invoke();
                T rawInput = Marshal.PtrToStructure<T>(current);
                handle(rawInput);

                RawInputNative.RawInputHeader header =
                    Marshal.PtrToStructure<RawInputNative.RawInputHeader>(current);
                current += (int)header.Size;
            }
        }
    }

    public void Dispose()
    {
        if (_buffer == nint.Zero)
        {
            return;
        }

        Marshal.FreeHGlobal(_buffer);
        _buffer = nint.Zero;
        _bufferSize = 0;
    }

    private bool TryReadBuffer(out uint count)
    {
        Ensure(InitialBufferSize);

        uint size = _bufferSize;
        count = RawInputNative.GetRawInputBuffer(_buffer, ref size, HeaderSize);

        if (count == uint.MaxValue)
        {
            uint requiredSize = 0;
            _ = RawInputNative.GetRawInputBuffer(nint.Zero, ref requiredSize, HeaderSize);

            if (requiredSize == 0 || requiredSize <= _bufferSize)
            {
                count = 0;
                return false;
            }

            Ensure(requiredSize);
            size = _bufferSize;
            count = RawInputNative.GetRawInputBuffer(_buffer, ref size, HeaderSize);
        }

        if (count == uint.MaxValue)
        {
            count = 0;
            return false;
        }

        return count > 0;
    }

    private void Ensure(uint size)
    {
        if (_buffer != nint.Zero && _bufferSize >= size)
        {
            return;
        }

        if (_buffer != nint.Zero)
        {
            Marshal.FreeHGlobal(_buffer);
        }

        _bufferSize = Math.Max(size, InputSize);
        _buffer = Marshal.AllocHGlobal((int)_bufferSize);
    }
}
