using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.Versioning;
using Vanara.PInvoke;
using static Vanara.PInvoke.User32;

namespace SteamInputBridge.Profiles;

internal enum ReceiverWindowActivationResult
{
    WindowNotFound,
    Activated,
    Rejected,
}

/// <summary>Best-effort foreground activation for tracked receiver process windows.</summary>
[SupportedOSPlatform("windows")]
internal static class ReceiverWindowActivator
{
    private const SetWindowPosFlags FrontMostFlags =
        SetWindowPosFlags.SWP_NOMOVE |
        SetWindowPosFlags.SWP_NOSIZE |
        SetWindowPosFlags.SWP_SHOWWINDOW;

    public static ReceiverWindowActivationResult TryActivate(int processId)
    {
        HWND window = FindReceiverWindow(processId);
        if (window.IsNull)
        {
            return ReceiverWindowActivationResult.WindowNotFound;
        }

        _ = ShowWindow(window, IsIconic(window) ? ShowWindowCommand.SW_RESTORE : ShowWindowCommand.SW_SHOW);

        HWND foregroundWindow = GetForegroundWindow();
        uint currentThreadId = Kernel32.GetCurrentThreadId();
        uint receiverThreadId = GetWindowThreadProcessId(window, out _);
        uint foregroundThreadId = foregroundWindow.IsNull ? 0 : GetWindowThreadProcessId(foregroundWindow, out _);
        bool receiverAttached = false;
        bool foregroundAttached = false;

        try
        {
            if (foregroundThreadId != 0 && foregroundThreadId != currentThreadId)
            {
                foregroundAttached = AttachThreadInput(currentThreadId, foregroundThreadId, true);
            }

            if (receiverThreadId != 0 && receiverThreadId != currentThreadId)
            {
                receiverAttached = AttachThreadInput(currentThreadId, receiverThreadId, true);
            }

            _ = SetWindowPos(window, HWND.HWND_TOPMOST, 0, 0, 0, 0, FrontMostFlags);
            _ = SetWindowPos(window, HWND.HWND_NOTOPMOST, 0, 0, 0, 0, FrontMostFlags);
            _ = BringWindowToTop(window);
            _ = SetForegroundWindow(window);
            _ = SetActiveWindow(window);
            _ = SetFocus(window);
        }
        finally
        {
            if (receiverAttached)
            {
                _ = AttachThreadInput(currentThreadId, receiverThreadId, false);
            }

            if (foregroundAttached)
            {
                _ = AttachThreadInput(currentThreadId, foregroundThreadId, false);
            }
        }

        return GetForegroundWindow() == window
            ? ReceiverWindowActivationResult.Activated
            : ReceiverWindowActivationResult.Rejected;
    }

    private static HWND FindReceiverWindow(int processId)
    {
        try
        {
            using Process process = Process.GetProcessById(processId);
            process.Refresh();
            nint windowHandle = process.MainWindowHandle;
            if (windowHandle == nint.Zero)
            {
                return default;
            }

            HWND window = new(windowHandle);
            return IsWindowVisible(window) ? window : default;
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException or Win32Exception or NotSupportedException)
        {
            return default;
        }
    }
}
