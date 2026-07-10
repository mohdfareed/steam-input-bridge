using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace SteamInputBridge.Inputs.RawInput;

[SupportedOSPlatform("windows")]
public sealed partial class RawInputMouseSource
{
    private const string WindowClassName = "SteamInputBridge.RawInput";
    private const string WindowName = "Steam Input Bridge Raw Input";

    // MARK: Methods
    // ========================================================================

    private static nint HandleWindowMessage(nint hwnd, uint message, nint wParam, nint lParam)
    {
        if (message == RawInputNative.WmInput)
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
                : RawInputNative.DefWindowProc(hwnd, message, wParam, lParam);
        }

        if (message == RawInputNative.WmClose)
        {
            _ = RawInputNative.DestroyWindow(hwnd);
            return nint.Zero;
        }

        if (message == RawInputNative.WmDestroy)
        {
            RawInputNative.PostQuitMessage(0);
            return nint.Zero;
        }

        return RawInputNative.DefWindowProc(hwnd, message, wParam, lParam);
    }

    private static nint CreateWindowHandle()
    {
        nint classNameHandle = Marshal.StringToHGlobalUni(WindowClassName);
        RawInputNative.WindowClassEx windowClass = new()
        {
            ClassName = classNameHandle,
            MenuName = nint.Zero,
            Instance = RawInputNative.GetModuleHandle(null),
            Size = (uint)Marshal.SizeOf<RawInputNative.WindowClassEx>(),
            WindowProc = Marshal.GetFunctionPointerForDelegate(WindowProcDelegate),
        };

        try
        {
            int registerError = RawInputNative.RegisterClassEx(ref windowClass) == 0 ? Marshal.GetLastWin32Error() : 0;
            if (registerError is not 0 and not RawInputNative.ClassAlreadyRegisteredError)
            {
                throw new Win32Exception(registerError, "Could not register Raw Input mouse window class.");
            }
        }
        finally
        {
            Marshal.FreeHGlobal(classNameHandle);
        }

        nint windowHandle = RawInputNative.CreateWindowEx(
            0,
            WindowClassName,
            WindowName,
            0,
            0,
            0,
            0,
            0,
            RawInputNative.MessageOnlyWindow,
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
        RawInputNative.RawInputDevice[] devices =
        [
            new()
            {
                UsagePage = RawInputNative.UsagePageGenericDesktop,
                Usage = RawInputNative.UsageMouse,
                Flags = RawInputNative.RawInputSink,
                Target = windowHandle,
            }
        ];

        if (!RawInputNative.RegisterRawInputDevices(
            devices,
            (uint)devices.Length,
            (uint)Marshal.SizeOf<RawInputNative.RawInputDevice>()))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not register raw mouse input.");
        }
    }

    private static void RunMessageLoop()
    {
        int result = RawInputNative.GetMessage(out RawInputNative.Message message, nint.Zero, 0, 0);
        while (result > 0)
        {
            _ = RawInputNative.TranslateMessage(ref message);
            _ = RawInputNative.DispatchMessage(ref message);
            result = RawInputNative.GetMessage(out message, nint.Zero, 0, 0);
        }

        if (result < 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not process raw mouse input.");
        }
    }
}
