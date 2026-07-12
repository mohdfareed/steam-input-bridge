using System.Runtime.Versioning;
using Vanara.PInvoke;
using static Vanara.PInvoke.User32;

namespace SteamInputBridge.Profiles;

internal enum WindowActivationResult
{
    WindowNotFound,
    Activated,
    Rejected,
}

/// <summary>Best-effort foreground activation for tracked receiver process windows.</summary>
[SupportedOSPlatform("windows")]
internal static class ReceiverWindowActivator
{
    public static WindowActivationResult TryActivate(int processId)
    {
        HWND window = FindReceiverWindow(processId);
        if (window.IsNull)
        {
            return WindowActivationResult.WindowNotFound;
        }

        uint currentThreadId = Kernel32.GetCurrentThreadId();
        uint targetThreadId = GetWindowThreadProcessId(window, out _);
        uint foregroundThreadId = ForegroundThreadId();
        bool attachedForeground = false;
        bool attachedTarget = false;
        try
        {
            attachedForeground = AttachInput(currentThreadId, foregroundThreadId);
            attachedTarget = AttachInput(currentThreadId, targetThreadId);

            _ = ShowWindow(window, IsIconic(window) ? ShowWindowCommand.SW_RESTORE : ShowWindowCommand.SW_SHOW);
            _ = BringWindowToTop(window);
            _ = SetForegroundWindow(window);
        }
        finally
        {
            if (attachedTarget)
            {
                _ = AttachThreadInput(currentThreadId, targetThreadId, false);
            }

            if (attachedForeground)
            {
                _ = AttachThreadInput(currentThreadId, foregroundThreadId, false);
            }
        }

        return ForegroundProcessId() == processId
            ? WindowActivationResult.Activated
            : WindowActivationResult.Rejected;
    }

    private static HWND FindReceiverWindow(int processId)
    {
        HWND receiverWindow = default;
        _ = EnumWindows((window, parameter) =>
        {
            _ = parameter;
            _ = GetWindowThreadProcessId(window, out uint windowProcessId);
            if (windowProcessId == processId &&
                IsWindowVisible(window) &&
                GetWindow(window, GetWindowCmd.GW_OWNER).IsNull)
            {
                receiverWindow = window;
                return false;
            }

            return true;
        }, default);

        return receiverWindow;
    }

    private static bool AttachInput(uint currentThreadId, uint otherThreadId)
    {
        return otherThreadId != 0 &&
            otherThreadId != currentThreadId &&
            AttachThreadInput(currentThreadId, otherThreadId, true);
    }

    private static uint ForegroundThreadId()
    {
        HWND foregroundWindow = GetForegroundWindow();
        return foregroundWindow.IsNull
            ? 0
            : GetWindowThreadProcessId(foregroundWindow, out _);
    }

    private static int? ForegroundProcessId()
    {
        HWND foregroundWindow = GetForegroundWindow();
        if (foregroundWindow.IsNull)
        {
            return null;
        }

        _ = GetWindowThreadProcessId(foregroundWindow, out uint processId);
        return processId == 0 ? null : (int)processId;
    }
}
