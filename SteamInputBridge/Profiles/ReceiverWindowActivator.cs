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
    public static ReceiverWindowActivationResult TryActivate(int processId)
    {
        HWND window = FindReceiverWindow(processId);
        if (window.IsNull)
        {
            return ReceiverWindowActivationResult.WindowNotFound;
        }

        _ = ShowWindow(window, IsIconic(window) ? ShowWindowCommand.SW_RESTORE : ShowWindowCommand.SW_SHOW);
        _ = BringWindowToTop(window);
        return SetForegroundWindow(window)
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
