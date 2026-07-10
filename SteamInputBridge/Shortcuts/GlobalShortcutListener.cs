using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Windows.Forms;
using SteamInputBridge.Shortcuts.Runtime;
using Vanara.PInvoke;
using Timer = System.Windows.Forms.Timer;

namespace SteamInputBridge.Shortcuts;

/// <summary>Observes global shortcuts and reports press/release transitions.</summary>
public sealed class GlobalShortcutListener : IGlobalShortcutListener
{
    private const int ReleasePollMilliseconds = 25;

    private readonly MessageThread _messageThread = new();
    private readonly ShortcutPressTracker _tracker = new(IsKeyDown);
    private readonly PassThroughKeyboardHook _keyboardHook;

    private Timer? _timer;
    private bool _disposed;

    /// <summary>Creates the global shortcut listener.</summary>
    public GlobalShortcutListener()
    {
        _keyboardHook = new(OnKeyDown);
    }

    // MARK: Publics
    // ========================================================================

    internal void Update(
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
            _tracker.Update(shortcuts, pressed, released);

            if (shortcuts.Count != 0)
            {
                _keyboardHook.Start();
            }
        });
    }

    void IGlobalShortcutListener.Update(
        IReadOnlyList<KeyboardShortcutRegistration> shortcuts,
        Action<int> pressed,
        Action<int> released)
    {
        Update(shortcuts, pressed, released);
    }

    /// <summary>Removes shortcut registrations and stops the listener thread.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _messageThread.Invoke(ClearRegistrations);
        _messageThread.Dispose();
        _keyboardHook.Dispose();
        _disposed = true;
    }

    // MARK: Registrations
    // ========================================================================

    private void ClearRegistrations()
    {
        StopTimer();
        _keyboardHook.Stop();
        _tracker.Update([], static _ => { }, static _ => { });
    }

    // MARK: Events
    // ========================================================================

    private void OnKeyDown(ushort virtualKey)
    {
        if (_tracker.KeyPressed(virtualKey))
        {
            StartTimer();
        }
    }

    private void RefreshPressedShortcuts(object? sender, EventArgs args)
    {
        _ = sender;
        _ = args;

        _tracker.Refresh();

        if (!_tracker.HasActiveState)
        {
            StopTimer();
        }
    }

    private void StartTimer()
    {
        if (_timer is not null)
        {
            return;
        }

        _timer = new Timer
        {
            Interval = ReleasePollMilliseconds,
        };
        _timer.Tick += RefreshPressedShortcuts;
        _timer.Start();
    }

    private void StopTimer()
    {
        if (_timer is null)
        {
            return;
        }

        _timer.Tick -= RefreshPressedShortcuts;
        _timer.Dispose();
        _timer = null;
    }

    private static bool IsKeyDown(ushort virtualKey)
    {
        return (User32.GetAsyncKeyState((User32.VK)virtualKey) & 0x8000) != 0;
    }

    // MARK: Message Thread
    // ========================================================================

    private sealed class MessageThread : IDisposable
    {
        private readonly ManualResetEventSlim _ready = new();
        private readonly Thread _thread;
        private Control? _control;
        private Exception? _startupError;
        private bool _disposed;

        public MessageThread()
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

            Control control = _control ?? throw new InvalidOperationException("Shortcut message thread is not ready.");
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
            catch (Exception exception) when (exception is InvalidOperationException or ThreadStateException or Win32Exception)
            {
                _startupError = exception;
                _ready.Set();
            }
        }
    }
}
