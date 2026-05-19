using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using VirtualMouse.Forwarding;

namespace VirtualMouse.Inputs.RawInput;

[SupportedOSPlatform("windows")]
public sealed partial class RawInputMouseSource
{
    private const int DeviceName = 0x20000007;
    private const ushort MouseWheel = 0x0400;
    private const int WheelDelta = 120;

    private static readonly int RawInputBufferInitialSize = Marshal.SizeOf<RawInput>();
    private static readonly uint RawInputBufferInitialCapacity = (uint)(RawInputBufferInitialSize * 64);
    private static readonly uint RawInputHeaderSize = (uint)Marshal.SizeOf<RawInputHeader>();

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
        ushort flags,
        ushort downFlag,
        ushort upFlag,
        MouseButtons button)
    {
        return (flags & downFlag) != 0
            ? buttons | button
            : (flags & upFlag) != 0
                ? buttons & ~button
                : buttons;
    }

    private static int GetWheelDelta(ushort flags, ushort buttonData)
    {
        return (flags & MouseWheel) == 0
            ? 0
            : unchecked((short)buttonData) / WheelDelta;
    }

    private static string GetDeviceName(nint device)
    {
        if (device == nint.Zero)
        {
            return string.Empty;
        }

        uint size = 0;
        _ = NativeMethods.GetRawInputDeviceInfo(device, DeviceName, nint.Zero, ref size);
        if (size == 0)
        {
            return string.Empty;
        }

        nint buffer = Marshal.AllocHGlobal((int)(size * sizeof(char)));
        try
        {
            uint result = NativeMethods.GetRawInputDeviceInfo(device, DeviceName, buffer, ref size);
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
