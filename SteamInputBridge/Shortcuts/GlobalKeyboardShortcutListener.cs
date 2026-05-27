using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;

namespace SteamInputBridge.Shortcuts;

/// <summary>Windows global keyboard shortcut listener.</summary>
internal sealed class GlobalKeyboardShortcutListener : IKeyboardShortcutListener
{
    private KeyboardShortcutSession? _session;
    private bool _disposed;

    /// <inheritdoc />
    public void Update(
        IReadOnlyList<KeyboardShortcutRegistration> shortcuts,
        Action<int> pressed,
        Action<int> released)
    {
        ArgumentNullException.ThrowIfNull(shortcuts);
        ArgumentNullException.ThrowIfNull(pressed);
        ArgumentNullException.ThrowIfNull(released);
        ObjectDisposedException.ThrowIf(_disposed, this);

        _session?.Dispose();
        _session = null;
        if (shortcuts.Count != 0)
        {
            _session = KeyboardShortcutSession.Start(shortcuts, pressed, released);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _session?.Dispose();
        _session = null;
        _disposed = true;
    }
}

internal sealed class KeyboardShortcutSession : IDisposable
{
    private const uint ModNoRepeat = 0x4000;
    private const uint WmHotkey = 0x0312;
    private const uint WmTimer = 0x0113;
    private const uint WmKeyDown = 0x0100;
    private const uint WmKeyUp = 0x0101;
    private const uint WmSysKeyDown = 0x0104;
    private const uint WmSysKeyUp = 0x0105;
    private const uint WmQuit = 0x0012;
    private const uint PmNoRemove = 0x0000;
    private const uint RequestedReleaseTimerId = 1;
    private const uint ReleasePollMilliseconds = 30;
    private const int WhKeyboardLl = 13;

    private readonly IReadOnlyList<KeyboardShortcutRegistration> _shortcuts;
    private readonly Dictionary<int, KeyboardShortcutCombination> _combinations = [];
    private readonly HashSet<int> _pressedShortcuts = [];
    private readonly HashSet<ushort> _keysDown = [];
    private readonly Action<int> _pressed;
    private readonly Action<int> _released;
    private readonly LowLevelKeyboardProc _keyboardHookCallback;
    private readonly ManualResetEventSlim _ready = new();
    private readonly Thread _thread;
    private volatile uint _threadId;
    private IntPtr _keyboardHook;
    private Exception? _startupError;
    private UIntPtr _releaseTimerId;
    private bool _releaseTimerActive;
    private bool _disposed;

    private KeyboardShortcutSession(
        IReadOnlyList<KeyboardShortcutRegistration> shortcuts,
        Action<int> pressed,
        Action<int> released)
    {
        _shortcuts = shortcuts;
        foreach (KeyboardShortcutRegistration shortcut in shortcuts)
        {
            _combinations[shortcut.Id] = shortcut.Combination;
        }

        _pressed = pressed;
        _released = released;
        _keyboardHookCallback = HandleKeyboardHook;
        _thread = new Thread(Run)
        {
            IsBackground = true,
            Name = "SteamInputBridge shortcut listener",
        };
    }

    internal static KeyboardShortcutSession Start(
        IReadOnlyList<KeyboardShortcutRegistration> shortcuts,
        Action<int> pressed,
        Action<int> released)
    {
        KeyboardShortcutSession session = new(shortcuts, pressed, released);
        session._thread.Start();
        session._ready.Wait();
        if (session._startupError is not null)
        {
            session.Dispose();
            throw session._startupError;
        }

        return session;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        uint threadId = _threadId;
        if (threadId != 0)
        {
            _ = PostThreadMessage(threadId, WmQuit, UIntPtr.Zero, IntPtr.Zero);
        }

        _ = _thread.Join(TimeSpan.FromSeconds(2));

        _ready.Dispose();
        GC.SuppressFinalize(this);
    }

    ~KeyboardShortcutSession()
    {
        UnregisterKeyboardHook();
    }

    private void Run()
    {
        _threadId = GetCurrentThreadId();
        _ = PeekMessage(out _, IntPtr.Zero, 0, 0, PmNoRemove);
        try
        {
            RegisterShortcuts();
            _keyboardHook = SetWindowsHookEx(WhKeyboardLl, _keyboardHookCallback, GetModuleHandle(null), 0);
            if (_keyboardHook == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }

            _ready.Set();

            while (GetMessage(out NativeMessage message, IntPtr.Zero, 0, 0) > 0)
            {
                if (message.Message == WmHotkey)
                {
                    int shortcutId = (int)message.WParam;
                    if (!_pressedShortcuts.Add(shortcutId))
                    {
                        StartReleaseTimer();
                        continue;
                    }

                    StartReleaseTimer();
                    try
                    {
                        _pressed(shortcutId);
                    }
                    catch (Exception exception) when (exception is InvalidOperationException or ObjectDisposedException)
                    {
                    }
                }
                else if (message.Message == WmTimer &&
                    _releaseTimerActive &&
                    message.WParam.Equals(_releaseTimerId))
                {
                    ReleaseCompletedShortcuts();
                }
            }
        }
        catch (Win32Exception exception)
        {
            _startupError = exception;
            _ready.Set();
        }
        finally
        {
            StopReleaseTimer();
            UnregisterKeyboardHook();
            UnregisterShortcuts();
        }
    }

    private void RegisterShortcuts()
    {
        foreach (KeyboardShortcutRegistration shortcut in _shortcuts)
        {
            uint modifiers = (uint)shortcut.Combination.Modifiers | ModNoRepeat;
            if (!RegisterHotKey(IntPtr.Zero, shortcut.Id, modifiers, shortcut.Combination.VirtualKey))
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }
        }
    }

    private void UnregisterShortcuts()
    {
        foreach (KeyboardShortcutRegistration shortcut in _shortcuts)
        {
            _ = UnregisterHotKey(IntPtr.Zero, shortcut.Id);
        }
    }

    private IntPtr HandleKeyboardHook(int code, UIntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            uint message = wParam.ToUInt32();
            ushort virtualKey = checked((ushort)Marshal.PtrToStructure<KeyboardHookInfo>(lParam).VirtualKey);
            if (message is WmKeyDown or WmSysKeyDown)
            {
                _ = _keysDown.Add(virtualKey);
                PressCompletedShortcuts();
            }
            else if (message is WmKeyUp or WmSysKeyUp)
            {
                _ = _keysDown.Remove(virtualKey);
                ReleaseCompletedShortcuts();
            }
        }

        return CallNextHookEx(_keyboardHook, code, wParam, lParam);
    }

    private void PressCompletedShortcuts()
    {
        foreach ((int shortcutId, KeyboardShortcutCombination combination) in _combinations)
        {
            if (_pressedShortcuts.Contains(shortcutId) ||
                !IsShortcutDown(combination))
            {
                continue;
            }

            _ = _pressedShortcuts.Add(shortcutId);
            StartReleaseTimer();
            try
            {
                _pressed(shortcutId);
            }
            catch (Exception exception) when (exception is InvalidOperationException or ObjectDisposedException)
            {
            }
        }
    }

    private void ReleaseCompletedShortcuts()
    {
        if (_pressedShortcuts.Count == 0)
        {
            return;
        }

        List<int>? released = null;
        foreach (int shortcutId in _pressedShortcuts)
        {
            if (_combinations.TryGetValue(shortcutId, out KeyboardShortcutCombination combination) &&
                IsShortcutDown(combination))
            {
                continue;
            }

            released ??= [];
            released.Add(shortcutId);
        }

        if (released is null)
        {
            return;
        }

        foreach (int shortcutId in released)
        {
            _ = _pressedShortcuts.Remove(shortcutId);
            try
            {
                _released(shortcutId);
            }
            catch (Exception exception) when (exception is InvalidOperationException or ObjectDisposedException)
            {
            }
        }

        if (_pressedShortcuts.Count == 0)
        {
            StopReleaseTimer();
        }
    }

    private bool IsShortcutDown(KeyboardShortcutCombination combination)
    {
        return IsKeyDown(combination.VirtualKey) &&
            HasModifierState(combination.Modifiers, KeyboardShortcutModifiers.Control, 0x11) &&
            HasModifierState(combination.Modifiers, KeyboardShortcutModifiers.Alt, 0x12) &&
            HasModifierState(combination.Modifiers, KeyboardShortcutModifiers.Shift, 0x10) &&
            HasModifierState(combination.Modifiers, KeyboardShortcutModifiers.Windows, 0x5B, 0x5C);
    }

    private bool HasModifierState(
        KeyboardShortcutModifiers actual,
        KeyboardShortcutModifiers expected,
        ushort virtualKey,
        ushort? alternateVirtualKey = null)
    {
        return (actual & expected) == 0 ||
            IsKeyDown(virtualKey) ||
            (alternateVirtualKey.HasValue && IsKeyDown(alternateVirtualKey.Value));
    }

    private bool IsKeyDown(ushort virtualKey)
    {
        return _keysDown.Contains(virtualKey) || KeyboardShortcutState.IsKeyDown(virtualKey);
    }

    private void StartReleaseTimer()
    {
        if (_releaseTimerActive)
        {
            return;
        }

        // RegisterHotKey gives us the press event, but some foreground apps do
        // not reliably deliver the corresponding low-level key-up event. Poll
        // only while a shortcut is held so hold-mode targets are released even
        // when no later keyboard event wakes the hook.
        UIntPtr timerId = SetTimer(
            IntPtr.Zero,
            new UIntPtr(RequestedReleaseTimerId),
            ReleasePollMilliseconds,
            IntPtr.Zero);
        if (timerId == UIntPtr.Zero)
        {
            return;
        }

        _releaseTimerId = timerId;
        _releaseTimerActive = true;
    }

    private void StopReleaseTimer()
    {
        if (!_releaseTimerActive)
        {
            return;
        }

        _releaseTimerActive = false;
        _ = KillTimer(IntPtr.Zero, _releaseTimerId);
        _releaseTimerId = UIntPtr.Zero;
    }

    private void UnregisterKeyboardHook()
    {
        IntPtr hook = _keyboardHook;
        if (hook == IntPtr.Zero)
        {
            return;
        }

        _keyboardHook = IntPtr.Zero;
        _ = UnhookWindowsHookEx(hook);
    }

    [DllImport("user32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern bool RegisterHotKey(
        IntPtr hWnd,
        int id,
        uint fsModifiers,
        uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int GetMessage(
        out NativeMessage message,
        IntPtr hWnd,
        uint messageFilterMin,
        uint messageFilterMax);

    [DllImport("user32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern bool PeekMessage(
        out NativeMessage message,
        IntPtr hWnd,
        uint messageFilterMin,
        uint messageFilterMax,
        uint removeMessage);

    [DllImport("user32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern bool PostThreadMessage(
        uint idThread,
        uint msg,
        UIntPtr wParam,
        IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern UIntPtr SetTimer(
        IntPtr hWnd,
        UIntPtr nIdEvent,
        uint uElapse,
        IntPtr lpTimerFunc);

    [DllImport("user32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern bool KillTimer(
        IntPtr hWnd,
        UIntPtr uIdEvent);

    [DllImport("user32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern IntPtr SetWindowsHookEx(
        int idHook,
        LowLevelKeyboardProc callback,
        IntPtr hInstance,
        uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern IntPtr CallNextHookEx(
        IntPtr hook,
        int code,
        UIntPtr wParam,
        IntPtr lParam);

    [DllImport("kernel32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint GetCurrentThreadId();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern IntPtr GetModuleHandle(string? moduleName);

    private delegate IntPtr LowLevelKeyboardProc(int code, UIntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct KeyboardHookInfo
    {
        public readonly uint VirtualKey;
        public readonly uint ScanCode;
        public readonly uint Flags;
        public readonly uint Time;
        public readonly UIntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct NativeMessage
    {
        public readonly IntPtr Hwnd;
        public readonly uint Message;
        public readonly UIntPtr WParam;
        public readonly IntPtr LParam;
        public readonly uint Time;
        public readonly int PointX;
        public readonly int PointY;
    }
}
