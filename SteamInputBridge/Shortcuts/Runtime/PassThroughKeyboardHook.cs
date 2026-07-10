using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Vanara.PInvoke;

namespace SteamInputBridge.Shortcuts.Runtime;

internal sealed class PassThroughKeyboardHook : IDisposable
{
    private static readonly IntPtr KeyDown = (IntPtr)User32.WindowMessage.WM_KEYDOWN;
    private static readonly IntPtr SystemKeyDown = (IntPtr)User32.WindowMessage.WM_SYSKEYDOWN;
    private static readonly IntPtr KeyUp = (IntPtr)User32.WindowMessage.WM_KEYUP;
    private static readonly IntPtr SystemKeyUp = (IntPtr)User32.WindowMessage.WM_SYSKEYUP;

    private readonly Action<ushort, bool> _keyChanged;
    private readonly User32.HookProc _hookProc;
    private User32.HHOOK _hook;

    public PassThroughKeyboardHook(Action<ushort, bool> keyChanged)
    {
        _keyChanged = keyChanged;
        _hookProc = OnKeyboardHook;
    }

    public void Start()
    {
        if (!_hook.IsNull)
        {
            return;
        }

        HINSTANCE module = Kernel32.GetModuleHandle(null);
        _hook = User32.SetWindowsHookEx(User32.HookType.WH_KEYBOARD_LL, _hookProc, module, 0);
        if (_hook.IsNull)
        {
            throw new Win32Exception();
        }
    }

    public void Stop()
    {
        if (_hook.IsNull)
        {
            return;
        }

        _ = User32.UnhookWindowsHookEx(_hook);
        _hook = default;
    }

    public void Dispose()
    {
        Stop();
    }

    private IntPtr OnKeyboardHook(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            if (wParam == KeyDown || wParam == SystemKeyDown)
            {
                _keyChanged((ushort)Marshal.ReadInt32(lParam), true);
            }
            else if (wParam == KeyUp || wParam == SystemKeyUp)
            {
                _keyChanged((ushort)Marshal.ReadInt32(lParam), false);
            }
        }

        return User32.CallNextHookEx(_hook, code, wParam, lParam);
    }
}
