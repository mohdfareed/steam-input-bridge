using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;

namespace SteamInputBridge.Shortcuts;

// MARK: Models
// ============================================================================

/// <summary>Keyboard shortcut modifiers.</summary>
[Flags]
public enum KeyboardShortcutModifiers
{
    /// <summary>No modifier.</summary>
    None = 0,

    /// <summary>Alt key.</summary>
    Alt = 0x0001,

    /// <summary>Control key.</summary>
    Control = 0x0002,

    /// <summary>Shift key.</summary>
    Shift = 0x0004,

    /// <summary>Windows key.</summary>
    Windows = 0x0008,
}

/// <summary>Parsed keyboard shortcut combination.</summary>
public readonly record struct KeyboardShortcutCombination(
    KeyboardShortcutModifiers Modifiers,
    ushort VirtualKey)
{
    /// <inheritdoc />
    public override string ToString()
    {
        List<string> parts = [];
        if ((Modifiers & KeyboardShortcutModifiers.Control) != 0)
        {
            parts.Add("Ctrl");
        }

        if ((Modifiers & KeyboardShortcutModifiers.Alt) != 0)
        {
            parts.Add("Alt");
        }

        if ((Modifiers & KeyboardShortcutModifiers.Shift) != 0)
        {
            parts.Add("Shift");
        }

        if ((Modifiers & KeyboardShortcutModifiers.Windows) != 0)
        {
            parts.Add("Win");
        }

        parts.Add(FormatVirtualKey(VirtualKey));
        return string.Join("+", parts);
    }

    private static string FormatVirtualKey(ushort virtualKey)
    {
        return virtualKey is >= 0x70 and <= 0x87
            ? $"F{virtualKey - 0x70 + 1}"
            : virtualKey is >= 'A' and <= 'Z'
            ? ((char)virtualKey).ToString()
            : virtualKey is >= '0' and <= '9'
            ? ((char)virtualKey).ToString()
            : $"0x{virtualKey:x2}";
    }
}

/// <summary>Registered keyboard shortcut.</summary>
public sealed record KeyboardShortcutRegistration(
    int Id,
    KeyboardShortcutCombination Combination);

/// <summary>Receives global keyboard shortcut presses.</summary>
public interface IKeyboardShortcutListener : IDisposable
{
    /// <summary>Replaces the active shortcut registrations.</summary>
    void Update(IReadOnlyList<KeyboardShortcutRegistration> shortcuts, Action<int> pressed);
}

// MARK: Parser
// ============================================================================

/// <summary>Parses keyboard shortcut combinations.</summary>
public static class KeyboardShortcutParser
{
    /// <summary>Parses a key combination such as Ctrl+Alt+F13.</summary>
    public static KeyboardShortcutCombination Parse(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        KeyboardShortcutModifiers modifiers = KeyboardShortcutModifiers.None;
        ushort? key = null;
        foreach (string part in value.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (TryParseModifier(part, out KeyboardShortcutModifiers modifier))
            {
                modifiers |= modifier;
                continue;
            }

            if (key.HasValue)
            {
                throw new FormatException($"Shortcut \"{value}\" contains more than one non-modifier key.");
            }

            key = ParseVirtualKey(part);
        }

        return key.HasValue
            ? new KeyboardShortcutCombination(modifiers, key.Value)
            : throw new FormatException($"Shortcut \"{value}\" does not contain a key.");
    }

    private static bool TryParseModifier(string value, out KeyboardShortcutModifiers modifier)
    {
        if (string.Equals(value, "ctrl", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "control", StringComparison.OrdinalIgnoreCase))
        {
            modifier = KeyboardShortcutModifiers.Control;
            return true;
        }

        if (string.Equals(value, "alt", StringComparison.OrdinalIgnoreCase))
        {
            modifier = KeyboardShortcutModifiers.Alt;
            return true;
        }

        if (string.Equals(value, "shift", StringComparison.OrdinalIgnoreCase))
        {
            modifier = KeyboardShortcutModifiers.Shift;
            return true;
        }

        if (string.Equals(value, "win", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "windows", StringComparison.OrdinalIgnoreCase))
        {
            modifier = KeyboardShortcutModifiers.Windows;
            return true;
        }

        modifier = KeyboardShortcutModifiers.None;
        return false;
    }

    private static ushort ParseVirtualKey(string value)
    {
        if (value.Length == 1)
        {
            char c = char.ToUpperInvariant(value[0]);
            if (c is (>= 'A' and <= 'Z') or (>= '0' and <= '9'))
            {
                return c;
            }
        }

        return value.Length is 2 or 3 &&
            value[0] is 'F' or 'f' &&
            int.TryParse(value[1..], out int functionKey) &&
            functionKey is >= 1 and <= 24
            ? checked((ushort)(0x70 + functionKey - 1))
            : value.ToUpperInvariant() switch
            {
                "NUM0" or "NUMPAD0" => (ushort)0x60,
                "NUM1" or "NUMPAD1" => (ushort)0x61,
                "NUM2" or "NUMPAD2" => (ushort)0x62,
                "NUM3" or "NUMPAD3" => (ushort)0x63,
                "NUM4" or "NUMPAD4" => (ushort)0x64,
                "NUM5" or "NUMPAD5" => (ushort)0x65,
                "NUM6" or "NUMPAD6" => (ushort)0x66,
                "NUM7" or "NUMPAD7" => (ushort)0x67,
                "NUM8" or "NUMPAD8" => (ushort)0x68,
                "NUM9" or "NUMPAD9" => (ushort)0x69,
                "ENTER" => (ushort)0x0D,
                "ESC" or "ESCAPE" => (ushort)0x1B,
                "SPACE" => (ushort)0x20,
                "TAB" => (ushort)0x09,
                "BACKSPACE" => (ushort)0x08,
                _ => throw new FormatException($"Unsupported shortcut key \"{value}\"."),
            };
    }
}

// MARK: Global Listener
// ============================================================================

/// <summary>Windows global keyboard shortcut listener.</summary>
public sealed class GlobalKeyboardShortcutListener : IKeyboardShortcutListener
{
    private KeyboardShortcutSession? _session;
    private bool _disposed;

    /// <inheritdoc />
    public void Update(IReadOnlyList<KeyboardShortcutRegistration> shortcuts, Action<int> pressed)
    {
        ArgumentNullException.ThrowIfNull(shortcuts);
        ArgumentNullException.ThrowIfNull(pressed);
        ObjectDisposedException.ThrowIf(_disposed, this);

        _session?.Dispose();
        _session = null;
        if (shortcuts.Count != 0)
        {
            _session = KeyboardShortcutSession.Start(shortcuts, pressed);
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
    private const uint WmQuit = 0x0012;
    private const uint PmNoRemove = 0x0000;

    private readonly IReadOnlyList<KeyboardShortcutRegistration> _shortcuts;
    private readonly Action<int> _pressed;
    private readonly ManualResetEventSlim _ready = new();
    private readonly Thread _thread;
    private volatile uint _threadId;
    private Exception? _startupError;
    private bool _disposed;

    private KeyboardShortcutSession(
        IReadOnlyList<KeyboardShortcutRegistration> shortcuts,
        Action<int> pressed)
    {
        _shortcuts = shortcuts;
        _pressed = pressed;
        _thread = new Thread(Run)
        {
            IsBackground = true,
            Name = "SteamInputBridge shortcut listener",
        };
    }

    public static KeyboardShortcutSession Start(
        IReadOnlyList<KeyboardShortcutRegistration> shortcuts,
        Action<int> pressed)
    {
        KeyboardShortcutSession session = new(shortcuts, pressed);
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
    }

    private void Run()
    {
        _threadId = GetCurrentThreadId();
        _ = PeekMessage(out _, IntPtr.Zero, 0, 0, PmNoRemove);
        try
        {
            RegisterShortcuts();
            _ready.Set();

            while (GetMessage(out NativeMessage message, IntPtr.Zero, 0, 0) > 0)
            {
                if (message.Message == WmHotkey)
                {
                    try
                    {
                        _pressed((int)message.WParam);
                    }
                    catch (Exception exception) when (exception is InvalidOperationException or ObjectDisposedException)
                    {
                    }
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

    [DllImport("kernel32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint GetCurrentThreadId();

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
