using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using SteamInputBridge.Inputs.Mouse;

namespace SteamInputBridge.Inputs.RawInput;

[SupportedOSPlatform("windows")]
public sealed partial class RawInputMouseSource
{
    private const int Input = 0x10000003;
    private const int RawInputMouse = 0;

    private sealed class RunState(MouseInputHandler handler, CancellationToken cancellationToken) : IDisposable
    {
        private readonly Dictionary<nint, string> _deviceNames = [];
        private MouseButtons _currentButtons;
        private nint _inputBuffer;
        private uint _inputBufferSize;

        // MARK: Publics
        // ====================================================================

        public CancellationToken CancellationToken { get; } = cancellationToken;

        internal void HandleWindowInput(nint rawInputHandle)
        {
            // Microsoft's buffered Raw Input pattern reads the current WM_INPUT
            // lParam, then drains accumulated high-frequency mouse events.
            CancellationToken.ThrowIfCancellationRequested();

            if (TryReadRawInputData(rawInputHandle, out RawInput rawInput))
            {
                HandleRawInputEvent(rawInput);
            }

            DrainRawInputQueue();
        }

        public void Dispose()
        {
            if (_inputBuffer != nint.Zero)
            {
                Marshal.FreeHGlobal(_inputBuffer);
                _inputBuffer = nint.Zero;
                _inputBufferSize = 0;
            }
        }

        // MARK: Raw Input
        // ====================================================================

        private void HandleRawInputEvent(RawInput rawInput)
        {
            if (rawInput.Header.Type != RawInputMouse)
            {
                return;
            }

            RawMouse mouse = rawInput.Mouse;
            ushort buttonFlags = mouse.ButtonFlags;
            ushort buttonData = mouse.ButtonData;
            bool hasButtonEvent = HasMouseButtonEvent(buttonFlags);

            int deltaX = mouse.LastX;
            int deltaY = mouse.LastY;
            int wheelDelta = GetWheelDelta(buttonFlags, buttonData);
            if (deltaX == 0 && deltaY == 0 && !hasButtonEvent && wheelDelta == 0)
            {
                return;
            }

            MouseReport report = CreateReport(buttonFlags, deltaX, deltaY, wheelDelta);
            MouseInput input = new(
                report,
                GetCachedDeviceName(rawInput.Header.Device),
                rawInput.Header.Device);

            handler(in input);
        }

        private bool TryReadRawInputBuffer(out uint count)
        {
            EnsureInputBuffer(RawInputBufferInitialCapacity);

            uint size = _inputBufferSize;
            count = NativeMethods.GetRawInputBuffer(
                _inputBuffer,
                ref size,
                RawInputHeaderSize);

            if (count == uint.MaxValue)
            {
                uint requiredSize = 0;
                _ = NativeMethods.GetRawInputBuffer(
                    nint.Zero,
                    ref requiredSize,
                    RawInputHeaderSize);

                if (requiredSize == 0 || requiredSize <= _inputBufferSize)
                {
                    count = 0;
                    return false;
                }

                EnsureInputBuffer(requiredSize);
                size = _inputBufferSize;
                count = NativeMethods.GetRawInputBuffer(
                    _inputBuffer,
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

            uint size = _inputBufferSize;
            uint read = NativeMethods.GetRawInputData(
                rawInputHandle,
                Input,
                _inputBuffer,
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
                size = _inputBufferSize;
                read = NativeMethods.GetRawInputData(
                    rawInputHandle,
                    Input,
                    _inputBuffer,
                    ref size,
                    RawInputHeaderSize);
            }

            if (read == uint.MaxValue || read < (uint)RawInputBufferInitialSize)
            {
                rawInput = default;
                return false;
            }

            rawInput = Marshal.PtrToStructure<RawInput>(_inputBuffer);
            return true;
        }

        private void DrainRawInputQueue()
        {
            while (TryReadRawInputBuffer(out uint count))
            {
                nint current = _inputBuffer;
                for (uint i = 0; i < count; i++)
                {
                    CancellationToken.ThrowIfCancellationRequested();
                    RawInput rawInput = Marshal.PtrToStructure<RawInput>(current);
                    HandleRawInputEvent(rawInput);
                    current += (int)rawInput.Header.Size;
                }
            }
        }

        // MARK: Privates
        // ====================================================================

        private void EnsureInputBuffer(uint size)
        {
            if (_inputBuffer != nint.Zero && _inputBufferSize >= size)
            {
                return;
            }

            if (_inputBuffer != nint.Zero)
            {
                Marshal.FreeHGlobal(_inputBuffer);
            }

            _inputBufferSize = Math.Max(size, (uint)RawInputBufferInitialSize);
            _inputBuffer = Marshal.AllocHGlobal((int)_inputBufferSize);
        }

        private string GetCachedDeviceName(nint device)
        {
            if (device == nint.Zero)
            {
                return string.Empty;
            }

            if (!_deviceNames.TryGetValue(device, out string? deviceName))
            {
                deviceName = GetDeviceName(device);
                _deviceNames[device] = deviceName;
            }

            return deviceName;
        }

        private MouseReport CreateReport(ushort buttonFlags, int deltaX, int deltaY, int wheelDelta)
        {
            if (buttonFlags != 0)
            {
                _currentButtons = ApplyButton(_currentButtons, buttonFlags, 0x0001, 0x0002, MouseButtons.Left);
                _currentButtons = ApplyButton(_currentButtons, buttonFlags, 0x0004, 0x0008, MouseButtons.Right);
                _currentButtons = ApplyButton(_currentButtons, buttonFlags, 0x0010, 0x0020, MouseButtons.Middle);
                _currentButtons = ApplyButton(_currentButtons, buttonFlags, 0x0040, 0x0080, MouseButtons.Back);
                _currentButtons = ApplyButton(_currentButtons, buttonFlags, 0x0100, 0x0200, MouseButtons.Forward);
            }

            return new MouseReport(_currentButtons, deltaX, deltaY, wheelDelta);
        }
    }
}
