using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using SteamInputBridge.Settings;
using SteamInputBridge.Shortcuts.Runtime;

namespace SteamInputBridge.Shortcuts;

public sealed partial class ShortcutService
{
    // MARK: Key Events
    // ============================================================================

    private void OnKeyChanged(ushort virtualKey, bool pressed)
    {
        lock (_queueGate)
        {
            _pendingKeys.Enqueue(new(virtualKey, pressed, Volatile.Read(ref _registrationVersion)));
        }

        if (Interlocked.Exchange(ref _processingPendingKeys, 1) == 0)
        {
            _ = ThreadPool.QueueUserWorkItem(static state => ((ShortcutService)state!).ProcessPendingKeys(), this);
        }
    }

    private void ProcessPendingKeys()
    {
        try
        {
            while (true)
            {
                KeyChange key;
                lock (_queueGate)
                {
                    if (_pendingKeys.Count == 0)
                    {
                        return;
                    }

                    key = _pendingKeys.Dequeue();
                }

                ProcessKeyChange(key);
            }
        }
        finally
        {
            _ = Interlocked.Exchange(ref _processingPendingKeys, 0);
            lock (_queueGate)
            {
                if (_pendingKeys.Count != 0 && Interlocked.Exchange(ref _processingPendingKeys, 1) == 0)
                {
                    _ = ThreadPool.QueueUserWorkItem(static state => ((ShortcutService)state!).ProcessPendingKeys(), this);
                }
            }
        }
    }

    private void ProcessKeyChange(KeyChange key)
    {
        List<(int Id, ShortcutEntry Entry, ShortcutPhase Phase)> events = [];
        bool changed;
        lock (_gate)
        {
            if (key.RegistrationVersion != Volatile.Read(ref _registrationVersion))
            {
                return;
            }

            long now = Stopwatch.GetTimestamp();
            ReconcileModifierKeys(now);
            if (key.Pressed)
            {
                if (_downKeys.Add(key.VirtualKey))
                {
                    _downAt[key.VirtualKey] = now;
                }
            }
            else if (IsModifierKey(key.VirtualKey))
            {
                ClearReleasedModifier(key.VirtualKey);
            }
            else
            {
                _ = _downKeys.Remove(key.VirtualKey);
                _ = _downAt.Remove(key.VirtualKey);
            }

            changed = ReconcilePressedShortcuts(events);
        }

        foreach ((int id, ShortcutEntry entry, ShortcutPhase phase) in events)
        {
            if (entry.Target.HasValue)
            {
                Shortcut?.Invoke(this, new(id, entry.Keys, entry.Target.Value, entry.Action, phase));
            }
        }

        if (changed)
        {
            StatusChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private bool ReconcilePressedShortcuts(List<(int Id, ShortcutEntry Entry, ShortcutPhase Phase)> events)
    {
        _desiredShortcuts.Clear();
        AddDesiredPressedShortcuts();

        bool changed = false;
        _scratchShortcutIds.Clear();
        foreach (int id in _pressedShortcuts)
        {
            _scratchShortcutIds.Add(id);
        }

        foreach (int id in _scratchShortcutIds)
        {
            if (_desiredShortcuts.Contains(id) || !_pressedShortcuts.Remove(id))
            {
                continue;
            }

            changed = true;
            if (!_shortcutEntries.TryGetValue(id, out IReadOnlyList<ShortcutEntry>? entries))
            {
                continue;
            }

            foreach (ShortcutEntry entry in entries)
            {
                if (entry.Action is ShortcutValue.Enable or ShortcutValue.Disable)
                {
                    events.Add((id, entry, ShortcutPhase.Released));
                }
            }
        }

        foreach (int id in _desiredShortcuts)
        {
            if (!_pressedShortcuts.Add(id))
            {
                continue;
            }

            changed = true;
            if (!_shortcutEntries.TryGetValue(id, out IReadOnlyList<ShortcutEntry>? entries))
            {
                continue;
            }

            foreach (ShortcutEntry entry in entries)
            {
                events.Add((id, entry, ShortcutPhase.Pressed));
            }
        }

        return changed;
    }

    private void AddDesiredPressedShortcuts()
    {
        foreach (int id in _pressedShortcuts)
        {
            if (_shortcutKeys.TryGetValue(id, out KeyboardShortcut shortcut) && ShortcutRemainsHeld(shortcut))
            {
                _ = _desiredShortcuts.Add(id);
            }
        }

        foreach ((int id, KeyboardShortcut shortcut) in _shortcutKeys)
        {
            if (ShortcutMatchesCurrentState(shortcut) && !DesiredHasShortcutForKey(shortcut.VirtualKey))
            {
                _ = _desiredShortcuts.Add(id);
            }
        }
    }

    private readonly record struct KeyChange(ushort VirtualKey, bool Pressed, int RegistrationVersion);
}
