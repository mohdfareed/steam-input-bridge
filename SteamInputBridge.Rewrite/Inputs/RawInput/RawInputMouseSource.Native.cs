using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace SteamInputBridge.Inputs.RawInput;

[SupportedOSPlatform("windows")]
public sealed partial class RawInputMouseSource
{
    // MARK: Models
    // ========================================================================

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate nint WindowProc(nint hwnd, uint message, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct Message
    {
        public nint WindowHandle;
        public uint MessageId;
        public nint WParam;
        public nint LParam;
        public uint Time;
        public Point Point;
        public uint Private;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowClassEx
    {
        public uint Size;
        public uint Style;
        public nint WindowProc;
        public int ClassExtra;
        public int WindowExtra;
        public nint Instance;
        public nint Icon;
        public nint Cursor;
        public nint Background;
        public nint MenuName;
        public nint ClassName;
        public nint SmallIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInputDevice
    {
        public ushort UsagePage;
        public ushort Usage;
        public uint Flags;
        public nint Target;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInput
    {
        public RawInputHeader Header;
        public RawMouse Mouse;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInputHeader
    {
        public uint Type;
        public uint Size;
        public nint Device;
        public nint WParam;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawMouse
    {
        public ushort Flags;
        public ushort Buttons;
        public ushort ButtonFlags;
        public ushort ButtonData;
        public uint RawButtons;
        public int LastX;
        public int LastY;
        public uint ExtraInformation;
    }

    // MARK: Methods
    // ========================================================================

    private static partial class NativeMethods
    {
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
        public static partial nint GetModuleHandle(string? moduleName);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [LibraryImport("user32.dll", EntryPoint = "RegisterClassExW", SetLastError = true)]
        public static partial ushort RegisterClassEx(ref WindowClassEx windowClass);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [LibraryImport("user32.dll", EntryPoint = "CreateWindowExW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
        public static partial nint CreateWindowEx(
            int exStyle,
            string className,
            string windowName,
            int style,
            int x,
            int y,
            int width,
            int height,
            nint parent,
            nint menu,
            nint instance,
            nint param);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [LibraryImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool RegisterRawInputDevices([In] RawInputDevice[] devices, uint deviceCount, uint size);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [LibraryImport("user32.dll", SetLastError = true)]
        public static partial uint GetRawInputBuffer(nint data, ref uint size, uint headerSize);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [LibraryImport("user32.dll", SetLastError = true)]
        public static partial uint GetRawInputData(nint rawInput, uint command, nint data, ref uint size, uint headerSize);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [LibraryImport("user32.dll", EntryPoint = "GetRawInputDeviceInfoW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
        public static partial uint GetRawInputDeviceInfo(nint device, uint command, nint data, ref uint size);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [LibraryImport("user32.dll", EntryPoint = "GetMessageW", SetLastError = true)]
        public static partial int GetMessage(out Message message, nint windowHandle, uint min, uint max);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool TranslateMessage(ref Message message);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [LibraryImport("user32.dll", EntryPoint = "DispatchMessageW")]
        public static partial nint DispatchMessage(ref Message message);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [LibraryImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool DestroyWindow(nint windowHandle);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [LibraryImport("user32.dll", EntryPoint = "PostMessageW", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool PostMessage(nint windowHandle, uint message, nint wParam, nint lParam);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [LibraryImport("user32.dll")]
        public static partial void PostQuitMessage(int exitCode);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [LibraryImport("user32.dll", EntryPoint = "DefWindowProcW")]
        public static partial nint DefWindowProc(nint hwnd, uint message, nint wParam, nint lParam);
    }
}
