using System;
using System.Collections.Generic;
using System.Runtime.Versioning;
using System.Threading;
using SteamInputBridge.Inputs.Mouse;

namespace SteamInputBridge.Inputs.RawInput;

[SupportedOSPlatform("windows")]
public sealed partial class RawInputMouseSource
{
    private sealed class RunState(MouseInputHandler handler, CancellationToken cancellationToken) : IDisposable
    {
        private readonly Dictionary<nint, string> _deviceNames = [];
        private readonly RawInputBuffer<RawInputNative.RawInputMouseData> _inputBuffer = new();
        private MouseButtons _currentButtons;

        // MARK: Publics
        // ====================================================================

        public CancellationToken CancellationToken { get; } = cancellationToken;

        internal void HandleWindowInput(nint rawInputHandle)
        {
            // Microsoft's buffered Raw Input pattern reads the current WM_INPUT
            // lParam, then drains accumulated high-frequency mouse events.
            CancellationToken.ThrowIfCancellationRequested();

            if (_inputBuffer.TryReadData(rawInputHandle, out RawInputNative.RawInputMouseData rawInput))
            {
                HandleRawInputEvent(rawInput);
            }

            _inputBuffer.Drain(HandleRawInputEvent, CancellationToken.ThrowIfCancellationRequested);
        }

        public void Dispose()
        {
            _inputBuffer.Dispose();
        }

        // MARK: Raw Input
        // ====================================================================

        private void HandleRawInputEvent(RawInputNative.RawInputMouseData rawInput)
        {
            if (rawInput.Header.Type != RawInputNative.RawInputMouse)
            {
                return;
            }

            RawInputNative.RawMouse mouse = rawInput.Mouse;
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

        // MARK: Privates
        // ====================================================================

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
