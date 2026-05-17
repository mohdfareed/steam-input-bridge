using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;

namespace Inputs.RawInput;

[SupportedOSPlatform("windows")]
public sealed partial class RawInputMouseSource
{
    private const int Input = 0x10000003;
    private const int RawInputMouse = 0;

    // MARK: State
    // ========================================================================

    private sealed class RunState(
        MouseInputHandler handler,
        Action<long, long>? timingHandler,
        CancellationToken cancellationToken) : IDisposable
    {
        private readonly Dictionary<nint, string> deviceNames = [];
        private MouseButtons currentButtons;
        private nint inputBuffer;
        private uint inputBufferSize;

        public CancellationToken CancellationToken { get; } = cancellationToken;

        public void HandleRawInput(nint rawInputHandle)
        {
            CancellationToken.ThrowIfCancellationRequested();
            long startedTimestamp = Stopwatch.GetTimestamp();
            if (TryReadRawInputData(rawInputHandle, out RawInput rawInput))
            {
                HandleRawInput(rawInput, startedTimestamp);
            }

            DrainRawInputQueue();
        }

        public void Dispose()
        {
            if (inputBuffer != nint.Zero)
            {
                Marshal.FreeHGlobal(inputBuffer);
                inputBuffer = nint.Zero;
                inputBufferSize = 0;
            }
        }

        private void HandleRawInput(RawInput rawInput, long startedTimestamp)
        {
            if (rawInput.Header.Type != RawInputMouse)
            {
                return;
            }

            RawMouse mouse = rawInput.Mouse;
            ushort buttonFlags = mouse.ButtonFlags;
            ushort buttonData = mouse.ButtonData;
            int deltaX = mouse.LastX;
            int deltaY = mouse.LastY;
            int wheelDelta = GetWheelDelta(buttonFlags, buttonData);
            bool hasButtonEvent = HasMouseButtonEvent(buttonFlags);
            if (deltaX == 0 && deltaY == 0 && !hasButtonEvent && wheelDelta == 0)
            {
                return;
            }

            MouseReport report = CreateReport(buttonFlags, deltaX, deltaY, wheelDelta);
            MouseInput input = new(report, GetCachedDeviceName(rawInput.Header.Device));
            timingHandler?.Invoke(startedTimestamp, Stopwatch.GetTimestamp());
            handler(in input);
        }

        private bool TryReadRawInputBuffer(out uint count)
        {
            EnsureInputBuffer(RawInputBufferInitialCapacity);

            uint size = inputBufferSize;
            count = NativeMethods.GetRawInputBuffer(
                inputBuffer,
                ref size,
                RawInputHeaderSize);

            if (count == uint.MaxValue)
            {
                uint requiredSize = 0;
                _ = NativeMethods.GetRawInputBuffer(
                    nint.Zero,
                    ref requiredSize,
                    RawInputHeaderSize);

                if (requiredSize == 0 || requiredSize <= inputBufferSize)
                {
                    count = 0;
                    return false;
                }

                EnsureInputBuffer(requiredSize);
                size = inputBufferSize;
                count = NativeMethods.GetRawInputBuffer(
                    inputBuffer,
                    ref size,
                    RawInputHeaderSize);
            }

            if (count == uint.MaxValue)
            {
                count = 0;
                return false;
            }

            return count > 0;
        }

        private bool TryReadRawInputData(nint rawInputHandle, out RawInput rawInput)
        {
            EnsureInputBuffer((uint)RawInputBufferInitialSize);

            uint size = inputBufferSize;
            uint read = NativeMethods.GetRawInputData(
                rawInputHandle,
                Input,
                inputBuffer,
                ref size,
                RawInputHeaderSize);

            if (read == uint.MaxValue)
            {
                uint requiredSize = 0;
                _ = NativeMethods.GetRawInputData(
                    rawInputHandle,
                    Input,
                    nint.Zero,
                    ref requiredSize,
                    RawInputHeaderSize);

                if (requiredSize == 0)
                {
                    rawInput = default;
                    return false;
                }

                EnsureInputBuffer(requiredSize);
                size = inputBufferSize;
                read = NativeMethods.GetRawInputData(
                    rawInputHandle,
                    Input,
                    inputBuffer,
                    ref size,
                    RawInputHeaderSize);
            }

            if (read == uint.MaxValue || read < (uint)RawInputBufferInitialSize)
            {
                rawInput = default;
                return false;
            }

            rawInput = Marshal.PtrToStructure<RawInput>(inputBuffer);
            return true;
        }

        private void DrainRawInputQueue()
        {
            while (TryReadRawInputBuffer(out uint count))
            {
                nint current = inputBuffer;
                for (uint i = 0; i < count; i++)
                {
                    CancellationToken.ThrowIfCancellationRequested();
                    long startedTimestamp = Stopwatch.GetTimestamp();
                    RawInput rawInput = Marshal.PtrToStructure<RawInput>(current);
                    HandleRawInput(rawInput, startedTimestamp);
                    current += (int)rawInput.Header.Size;
                }
            }
        }

        private void EnsureInputBuffer(uint size)
        {
            if (inputBuffer != nint.Zero && inputBufferSize >= size)
            {
                return;
            }

            if (inputBuffer != nint.Zero)
            {
                Marshal.FreeHGlobal(inputBuffer);
            }

            inputBufferSize = Math.Max(size, (uint)RawInputBufferInitialSize);
            inputBuffer = Marshal.AllocHGlobal((int)inputBufferSize);
        }

        private string GetCachedDeviceName(nint device)
        {
            if (device == nint.Zero)
            {
                return string.Empty;
            }

            if (!deviceNames.TryGetValue(device, out string? deviceName))
            {
                deviceName = GetDeviceName(device);
                deviceNames[device] = deviceName;
            }

            return deviceName;
        }

        private MouseReport CreateReport(ushort buttonFlags, int deltaX, int deltaY, int wheelDelta)
        {
            if (buttonFlags != 0)
            {
                currentButtons = ApplyButton(currentButtons, buttonFlags, 0x0001, 0x0002, MouseButtons.Left);
                currentButtons = ApplyButton(currentButtons, buttonFlags, 0x0004, 0x0008, MouseButtons.Right);
                currentButtons = ApplyButton(currentButtons, buttonFlags, 0x0010, 0x0020, MouseButtons.Middle);
                currentButtons = ApplyButton(currentButtons, buttonFlags, 0x0040, 0x0080, MouseButtons.Back);
                currentButtons = ApplyButton(currentButtons, buttonFlags, 0x0100, 0x0200, MouseButtons.Forward);
            }

            return new MouseReport(currentButtons, deltaX, deltaY, wheelDelta);
        }
    }
}
