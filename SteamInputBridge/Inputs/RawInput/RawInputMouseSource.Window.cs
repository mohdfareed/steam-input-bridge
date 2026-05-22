using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace SteamInputBridge.Inputs.RawInput;

[SupportedOSPlatform("windows")]
public sealed partial class RawInputMouseSource
{
    private const int ClassAlreadyRegisteredError = 1410;
    private const int RawInputSink = 0x00000100;
    private const int UsagePageGenericDesktop = 0x01;
    private const int UsageMouse = 0x02;
    private const int WmClose = 0x0010;
    private const int WmDestroy = 0x0002;
    private const int WmInput = 0x00FF;

    private static readonly nint MessageOnlyWindow = new(-3);
    private const string WindowClassName = "Inputs.RawInput";
    private const string WindowName = "Raw Input mouse source";

    // MARK: Methods
    // ========================================================================

    private static nint HandleWindowMessage(nint hwnd, uint message, nint wParam, nint lParam)
    {
        if (message == WmInput)
        {
            try
            {
                CurrentState?.HandleWindowInput(lParam);
            }
            catch (OperationCanceledException) when (CurrentState?.CancellationToken.IsCancellationRequested == true)
            {
            }

            return wParam != nint.Zero
                ? nint.Zero
                : NativeMethods.DefWindowProc(hwnd, message, wParam, lParam);
        }

        if (message == WmClose)
        {
            _ = NativeMethods.DestroyWindow(hwnd);
            return nint.Zero;
        }

        if (message == WmDestroy)
        {
            NativeMethods.PostQuitMessage(0);
            return nint.Zero;
        }

        return NativeMethods.DefWindowProc(hwnd, message, wParam, lParam);
    }

    private static nint CreateWindowHandle()
    {
        nint classNameHandle = Marshal.StringToHGlobalUni(WindowClassName);
        WindowClassEx windowClass = new()
        {
            ClassName = classNameHandle,
            MenuName = nint.Zero,
            Instance = NativeMethods.GetModuleHandle(null),
            Size = (uint)Marshal.SizeOf<WindowClassEx>(),
            WindowProc = Marshal.GetFunctionPointerForDelegate(WindowProcDelegate),
        };

        try
        {
            int registerError = NativeMethods.RegisterClassEx(ref windowClass) == 0 ? Marshal.GetLastWin32Error() : 0;
            if (registerError is not 0 and not ClassAlreadyRegisteredError)
            {
                throw new Win32Exception(registerError, "Could not register Raw Input mouse window class.");
            }
        }
        finally
        {
            Marshal.FreeHGlobal(classNameHandle);
        }

        nint windowHandle = NativeMethods.CreateWindowEx(
            0,
            WindowClassName,
            WindowName,
            0,
            0,
            0,
            0,
            0,
            MessageOnlyWindow,
            nint.Zero,
            windowClass.Instance,
            nint.Zero);

        int error = Marshal.GetLastWin32Error();
        return windowHandle != nint.Zero
            ? windowHandle
            : throw new Win32Exception(error, "Could not create Raw Input mouse window.");
    }

    private static void RegisterRawInput(nint windowHandle)
    {
        RawInputDevice[] devices =
        [
            new()
            {
                UsagePage = UsagePageGenericDesktop,
                Usage = UsageMouse,
                Flags = RawInputSink,
                Target = windowHandle,
            }
        ];

        if (!NativeMethods.RegisterRawInputDevices(devices, (uint)devices.Length, (uint)Marshal.SizeOf<RawInputDevice>()))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not register raw mouse input.");
        }
    }

    private static void RunMessageLoop()
    {
        int result = NativeMethods.GetMessage(out Message message, nint.Zero, 0, 0);
        while (result > 0)
        {
            _ = NativeMethods.TranslateMessage(ref message);
            _ = NativeMethods.DispatchMessage(ref message);
            result = NativeMethods.GetMessage(out message, nint.Zero, 0, 0);
        }

        if (result < 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not process raw mouse input.");
        }
    }
}
