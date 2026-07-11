using System;
using System.Collections.Generic;
using System.Threading;
using SteamInputBridge.Inputs.RawInput;
using Vanara.PInvoke;

namespace SteamInputBridge.Shortcuts.Runtime;

/// <summary>Reports Raw Input keyboard changes for the virtual keys selected by ShortcutService.</summary>
public sealed class GlobalShortcutListener : IGlobalShortcutListener
{
    private readonly RawInputKeyboardSource _keyboard;
    private readonly Lock _gate = new();
    private readonly HashSet<ushort> _observedKeys = [];
    private Action<ushort, bool> _changed = static (_, _) => { };
    private bool _disposed;

    /// <summary>Creates the global shortcut listener.</summary>
    public GlobalShortcutListener()
    {
        _keyboard = new(OnRawKeyChanged);
    }

    internal void Update(IReadOnlyCollection<ushort> observedVirtualKeys, Action<ushort, bool> changed)
    {
        ArgumentNullException.ThrowIfNull(observedVirtualKeys);
        ArgumentNullException.ThrowIfNull(changed);
        ObjectDisposedException.ThrowIf(_disposed, this);

        _keyboard.Stop();
        lock (_gate)
        {
            _observedKeys.Clear();
            foreach (ushort virtualKey in observedVirtualKeys)
            {
                _ = _observedKeys.Add(virtualKey);
            }

            _changed = changed;
        }

        if (observedVirtualKeys.Count != 0)
        {
            _keyboard.Start();
        }
    }

    void IGlobalShortcutListener.Update(IReadOnlyCollection<ushort> observedVirtualKeys, Action<ushort, bool> changed)
    {
        Update(observedVirtualKeys, changed);
    }

    bool IGlobalShortcutListener.IsKeyDown(ushort virtualKey)
    {
        return IsKeyDown(virtualKey);
    }

    /// <summary>Stops listening for keyboard input.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _keyboard.Dispose();
    }

    private void OnRawKeyChanged(ushort virtualKey, bool pressed)
    {
        Action<ushort, bool> changed;
        lock (_gate)
        {
            if (!_observedKeys.Contains(virtualKey))
            {
                return;
            }

            changed = _changed;
        }

        changed(virtualKey, pressed);
    }

    private static bool IsKeyDown(ushort virtualKey)
    {
        return (User32.GetAsyncKeyState((User32.VK)virtualKey) & 0x8000) != 0;
    }
}

internal interface IGlobalShortcutListener : IDisposable
{
    void Update(IReadOnlyCollection<ushort> observedVirtualKeys, Action<ushort, bool> changed);

    bool IsKeyDown(ushort virtualKey);
}
