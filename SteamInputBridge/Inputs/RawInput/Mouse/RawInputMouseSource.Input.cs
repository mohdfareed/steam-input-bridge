using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using SteamInputBridge.Inputs.Mouse;

namespace SteamInputBridge.Inputs.RawInput;

[SupportedOSPlatform("windows")]
public sealed partial class RawInputMouseSource
{
    private const ushort MouseWheel = 0x0400;
    private const int WheelDelta = 120;

    // MARK: Methods
    // ========================================================================

    private static bool HasMouseButtonEvent(ushort flags)
    {
        const ushort buttonMask =
            0x0001 | 0x0002 |
            0x0004 | 0x0008 |
            0x0010 | 0x0020 |
            0x0040 | 0x0080 |
            0x0100 | 0x0200;

        return (flags & buttonMask) != 0;
    }

    private static MouseButtons ApplyButton(
        MouseButtons buttons,
        ushort flags, ushort downFlag, ushort upFlag,
        MouseButtons button)
    {
        return (flags & downFlag) != 0
            ? buttons | button
            : (flags & upFlag) != 0
                ? buttons & ~button
                : buttons;
    }

    internal sealed class VerticalWheelAccumulator
    {
        private readonly Dictionary<nint, int> _remainders = [];

        public int Accumulate(nint device, ushort flags, ushort buttonData)
        {
            if ((flags & MouseWheel) == 0)
            {
                return 0;
            }

            _ = _remainders.TryGetValue(device, out int remainder);
            int accumulated = remainder + unchecked((short)buttonData);
            int wheelDelta = accumulated / WheelDelta;
            int newRemainder = accumulated % WheelDelta;
            if (newRemainder == 0)
            {
                _ = _remainders.Remove(device);
            }
            else
            {
                _remainders[device] = newRemainder;
            }

            return wheelDelta;
        }
    }

    private static string GetDeviceName(nint device)
    {
        if (device == nint.Zero)
        {
            return string.Empty;
        }

        uint size = 0;
        _ = RawInputNative.GetRawInputDeviceInfo(device, RawInputNative.DeviceName, nint.Zero, ref size);
        if (size == 0)
        {
            return string.Empty;
        }

        nint buffer = Marshal.AllocHGlobal((int)(size * sizeof(char)));
        try
        {
            uint result = RawInputNative.GetRawInputDeviceInfo(device, RawInputNative.DeviceName, buffer, ref size);
            return result == uint.MaxValue
                ? string.Empty
                : Marshal.PtrToStringUni(buffer) ?? string.Empty;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}
