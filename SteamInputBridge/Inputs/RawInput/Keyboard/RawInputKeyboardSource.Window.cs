using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace SteamInputBridge.Inputs.RawInput;

internal sealed partial class RawInputKeyboardSource
{
    private static void RegisterRawInput(nint windowHandle)
    {
        RawInputNative.RawInputDevice[] devices =
        [
            new()
            {
                UsagePage = RawInputNative.UsagePageGenericDesktop,
                Usage = RawInputNative.UsageKeyboard,
                Flags = RawInputNative.RawInputSink,
                Target = windowHandle,
            },
        ];

        if (!RawInputNative.RegisterRawInputDevices(
            devices,
            (uint)devices.Length,
            (uint)Marshal.SizeOf<RawInputNative.RawInputDevice>()))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not register raw keyboard input.");
        }
    }

    private static void UnregisterRawInput()
    {
        RawInputNative.RawInputDevice[] devices =
        [
            new()
            {
                UsagePage = RawInputNative.UsagePageGenericDesktop,
                Usage = RawInputNative.UsageKeyboard,
                Flags = RawInputNative.RawInputRemove,
                Target = nint.Zero,
            },
        ];

        _ = RawInputNative.RegisterRawInputDevices(
            devices,
            (uint)devices.Length,
            (uint)Marshal.SizeOf<RawInputNative.RawInputDevice>());
    }

    private sealed class RawInputMessageControl(Action<nint> rawInput) : Control
    {
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == RawInputNative.WmInput)
            {
                rawInput(m.LParam);
                if (m.WParam == nint.Zero)
                {
                    base.WndProc(ref m);
                }

                return;
            }

            base.WndProc(ref m);
        }
    }

    private sealed class MessageThread : IDisposable
    {
        private readonly ManualResetEventSlim _ready = new();
        private readonly Action<nint> _rawInput;
        private readonly Thread _thread;
        private Control? _control;
        private Exception? _startupError;
        private bool _disposed;

        public MessageThread(Action<nint> rawInput)
        {
            _rawInput = rawInput;
            _thread = new Thread(Run)
            {
                IsBackground = true,
                Name = "SteamInputBridge raw keyboard input",
            };
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();
            _ready.Wait();
            if (_startupError is not null)
            {
                throw _startupError;
            }
        }

        public nint WindowHandle =>
            _control?.Handle ?? throw new InvalidOperationException("Raw keyboard input thread is not ready.");

        public void Invoke(Action action)
        {
            ArgumentNullException.ThrowIfNull(action);
            ObjectDisposedException.ThrowIf(_disposed, this);

            Control control = _control ?? throw new InvalidOperationException("Raw keyboard input thread is not ready.");
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
                Control control = new RawInputMessageControl(_rawInput);
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
