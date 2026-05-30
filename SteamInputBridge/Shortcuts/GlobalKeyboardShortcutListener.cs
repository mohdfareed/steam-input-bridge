using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Windows.Forms;
using NHotkey;
using NHotkey.WindowsForms;
using Vanara.PInvoke;
using Timer = System.Windows.Forms.Timer;

namespace SteamInputBridge.Shortcuts;

/// <summary>Windows global keyboard shortcut listener.</summary>
internal sealed class GlobalKeyboardShortcutListener : IKeyboardShortcutListener
{
    private const int ShortcutPollMilliseconds = 30;
    private readonly ShortcutMessageThread _messageThread = new();
    private readonly Dictionary<int, KeyboardShortcutCombination> _combinations = [];
    private readonly HashSet<int> _pressedShortcuts = [];
    private readonly List<string> _registeredNames = [];
    private Timer? _shortcutTimer;
    private Action<int>? _pressed;
    private Action<int>? _released;
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

        _messageThread.Invoke(() =>
        {
            ClearRegistrations();
            _pressed = pressed;
            _released = released;
            foreach (KeyboardShortcutRegistration shortcut in shortcuts)
            {
                Register(shortcut);
            }
        });
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _messageThread.Invoke(ClearRegistrations);
        _messageThread.Dispose();
        _disposed = true;
    }

    private void Register(KeyboardShortcutRegistration shortcut)
    {
        string name = shortcut.Id.ToString(CultureInfo.InvariantCulture);
        HotkeyManager.Current.AddOrReplace(
            name,
            ToKeys(shortcut.Combination),
            noRepeat: true,
            OnHotkeyPressed);
        _registeredNames.Add(name);
        _combinations[shortcut.Id] = shortcut.Combination;
    }

    private void ClearRegistrations()
    {
        StopShortcutTimer();
        _pressedShortcuts.Clear();
        _combinations.Clear();
        foreach (string name in _registeredNames)
        {
            HotkeyManager.Current.Remove(name);
        }

        _registeredNames.Clear();
    }

    private void OnHotkeyPressed(object? sender, HotkeyEventArgs args)
    {
        _ = sender;
        args.Handled = true;
        if (!int.TryParse(args.Name, NumberStyles.Integer, CultureInfo.InvariantCulture, out int shortcutId))
        {
            return;
        }

        Action<int>? callback = null;
        if (_disposed || !_combinations.ContainsKey(shortcutId))
        {
            return;
        }

        StartShortcutTimer();
        if (_pressedShortcuts.Add(shortcutId))
        {
            callback = _pressed;
        }

        InvokeShortcutCallback(callback, shortcutId);
    }

    private void RefreshPressedShortcuts(object? sender, EventArgs args)
    {
        _ = sender;
        _ = args;
        List<int>? pressed = null;
        List<int>? released = null;
        Action<int>? pressCallback;
        Action<int>? releaseCallback;

        if (_disposed)
        {
            return;
        }

        // RegisterHotKey can miss a third non-modifier shortcut while other
        // registered shortcuts are held. Once any shortcut is down, scan the
        // configured set so held overlay colors and hold gates stay exact.
        foreach (KeyValuePair<int, KeyboardShortcutCombination> combination in _combinations)
        {
            bool isDown = IsShortcutDown(combination.Value);
            bool wasDown = _pressedShortcuts.Contains(combination.Key);
            if (isDown && !wasDown)
            {
                pressed ??= [];
                pressed.Add(combination.Key);
            }
            else if (!isDown && wasDown)
            {
                released ??= [];
                released.Add(combination.Key);
            }
        }

        if (pressed is null && released is null)
        {
            return;
        }

        if (released is not null)
        {
            foreach (int shortcutId in released)
            {
                _ = _pressedShortcuts.Remove(shortcutId);
            }
        }

        if (pressed is not null)
        {
            foreach (int shortcutId in pressed)
            {
                _ = _pressedShortcuts.Add(shortcutId);
            }
        }

        if (_pressedShortcuts.Count == 0)
        {
            StopShortcutTimer();
        }

        releaseCallback = _released;
        if (released is not null)
        {
            foreach (int shortcutId in released)
            {
                InvokeShortcutCallback(releaseCallback, shortcutId);
            }
        }

        pressCallback = _pressed;
        if (pressed is not null)
        {
            foreach (int shortcutId in pressed)
            {
                InvokeShortcutCallback(pressCallback, shortcutId);
            }
        }
    }

    private void StartShortcutTimer()
    {
        if (_shortcutTimer is not null)
        {
            return;
        }

        _shortcutTimer = new Timer
        {
            Interval = ShortcutPollMilliseconds,
        };
        _shortcutTimer.Tick += RefreshPressedShortcuts;
        _shortcutTimer.Start();
    }

    private void StopShortcutTimer()
    {
        _shortcutTimer?.Dispose();
        _shortcutTimer = null;
    }

    private static bool IsShortcutDown(KeyboardShortcutCombination combination)
    {
        return IsKeyDown(combination.VirtualKey) &&
            HasExactModifierState(combination.Modifiers);
    }

    private static bool HasExactModifierState(KeyboardShortcutModifiers modifiers)
    {
        return HasModifier(modifiers, KeyboardShortcutModifiers.Control) == IsKeyDown((ushort)Keys.ControlKey) &&
            HasModifier(modifiers, KeyboardShortcutModifiers.Alt) == IsKeyDown((ushort)Keys.Menu) &&
            HasModifier(modifiers, KeyboardShortcutModifiers.Shift) == IsKeyDown((ushort)Keys.ShiftKey) &&
            HasModifier(modifiers, KeyboardShortcutModifiers.Windows) ==
            (IsKeyDown((ushort)Keys.LWin) || IsKeyDown((ushort)Keys.RWin));
    }

    private static bool HasModifier(KeyboardShortcutModifiers actual, KeyboardShortcutModifiers expected)
    {
        return (actual & expected) != 0;
    }

    private static bool IsKeyDown(ushort virtualKey)
    {
        return (User32.GetAsyncKeyState((User32.VK)virtualKey) & 0x8000) != 0;
    }

    private static Keys ToKeys(KeyboardShortcutCombination combination)
    {
        Keys keys = (Keys)combination.VirtualKey;
        if ((combination.Modifiers & KeyboardShortcutModifiers.Control) != 0)
        {
            keys |= Keys.Control;
        }

        if ((combination.Modifiers & KeyboardShortcutModifiers.Alt) != 0)
        {
            keys |= Keys.Alt;
        }

        if ((combination.Modifiers & KeyboardShortcutModifiers.Shift) != 0)
        {
            keys |= Keys.Shift;
        }

        if ((combination.Modifiers & KeyboardShortcutModifiers.Windows) != 0)
        {
            keys |= Keys.LWin;
        }

        return keys;
    }

    private static void InvokeShortcutCallback(Action<int>? callback, int shortcutId)
    {
        if (callback is null)
        {
            return;
        }

        try
        {
            callback(shortcutId);
        }
        catch (Exception exception) when (exception is InvalidOperationException or ObjectDisposedException)
        {
        }
    }

    private sealed class ShortcutMessageThread : IDisposable
    {
        private readonly ManualResetEventSlim _ready = new();
        private readonly Thread _thread;
        private Control? _control;
        private Exception? _startupError;
        private bool _disposed;

        public ShortcutMessageThread()
        {
            _thread = new Thread(Run)
            {
                IsBackground = true,
                Name = "SteamInputBridge shortcut listener",
            };
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();
            _ready.Wait();
            if (_startupError is not null)
            {
                throw _startupError;
            }
        }

        public void Invoke(Action action)
        {
            ArgumentNullException.ThrowIfNull(action);
            ObjectDisposedException.ThrowIf(_disposed, this);

            Control control = _control ??
                throw new InvalidOperationException("Shortcut message thread is not ready.");
            if (control.InvokeRequired)
            {
                control.Invoke(action);
                return;
            }

            action();
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            try
            {
                Control? control = _control;
                if (control is not null && !control.IsDisposed)
                {
                    control.Invoke(static () => Application.ExitThread());
                }
            }
            catch (InvalidOperationException)
            {
            }

            _ = _thread.Join(TimeSpan.FromSeconds(2));
            if (!_thread.IsAlive)
            {
                _control?.Dispose();
                _control = null;
            }

            _ready.Dispose();
        }

        private void Run()
        {
            try
            {
                Control control = new();
                _control = control;
                _ = control.Handle;
                _ready.Set();
                try
                {
                    Application.Run();
                }
                finally
                {
                    control.Dispose();
                }
            }
            catch (Exception exception) when (exception is InvalidOperationException or ThreadStateException)
            {
                _startupError = exception;
                _ready.Set();
            }
        }
    }
}
