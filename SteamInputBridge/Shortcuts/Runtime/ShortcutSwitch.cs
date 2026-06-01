using System.Collections.Generic;

namespace SteamInputBridge.Shortcuts.Runtime;

internal sealed class ShortcutSwitch
{
    private readonly List<ShortcutHold> _holds = [];
    private bool? _enabled;

    public bool Apply(int shortcutId, ShortcutValue action, ShortcutPhase phase, bool defaultEnabled)
    {
        bool current = Current(defaultEnabled);
        if (action == ShortcutValue.Toggle)
        {
            if (phase == ShortcutPhase.Pressed)
            {
                _holds.Clear();
                _enabled = !current;
            }

            return Current(defaultEnabled);
        }

        bool enabled = action == ShortcutValue.Enable;
        if (phase == ShortcutPhase.Pressed)
        {
            _ = RemoveHold(shortcutId);
            _holds.Add(new(shortcutId, enabled));
        }
        else if (RemoveHold(shortcutId))
        {
            _enabled = !enabled;
        }

        return Current(defaultEnabled);
    }

    public void Reset()
    {
        _holds.Clear();
        _enabled = null;
    }

    private bool Current(bool defaultEnabled)
    {
        return _holds.Count == 0 ? _enabled ?? defaultEnabled : _holds[^1].Enabled;
    }

    private bool RemoveHold(int shortcutId)
    {
        for (int i = _holds.Count - 1; i >= 0; i--)
        {
            if (_holds[i].ShortcutId == shortcutId)
            {
                _holds.RemoveAt(i);
                return true;
            }
        }

        return false;
    }

    private readonly record struct ShortcutHold(int ShortcutId, bool Enabled);
}
