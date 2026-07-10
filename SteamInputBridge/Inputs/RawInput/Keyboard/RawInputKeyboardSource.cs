using System;

namespace SteamInputBridge.Inputs.RawInput;

internal sealed partial class RawInputKeyboardSource : IDisposable
{
    private readonly Action<ushort, bool> _keyChanged;
    private readonly MessageThread _messageThread;
    private nint _targetWindow;
    private bool _disposed;

    public RawInputKeyboardSource(Action<ushort, bool> keyChanged)
    {
        ArgumentNullException.ThrowIfNull(keyChanged);

        _keyChanged = keyChanged;
        _messageThread = new(HandleRawInput);
    }

    public bool IsStarted => _targetWindow != nint.Zero;

    public string HandleText => _targetWindow == nint.Zero
        ? "0x0"
        : $"0x{_targetWindow.ToInt64():X}";

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _messageThread.Invoke(() =>
        {
            if (_targetWindow != nint.Zero)
            {
                return;
            }

            nint windowHandle = _messageThread.WindowHandle;
            RegisterRawInput(windowHandle);
            _targetWindow = windowHandle;
        });
    }

    public void Stop()
    {
        if (_disposed)
        {
            return;
        }

        _messageThread.Invoke(() =>
        {
            if (_targetWindow == nint.Zero)
            {
                return;
            }

            UnregisterRawInput();
            _targetWindow = nint.Zero;
        });
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Stop();
        _disposed = true;
        _messageThread.Dispose();
        FreeInputBuffer();
    }
}
