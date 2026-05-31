using System;
using System.Collections.Generic;
using System.Linq;
using SteamInputBridge.Settings;

namespace SteamInputBridge.Shortcuts.Runtime;

internal sealed record ShortcutBindingSet(
    IReadOnlyDictionary<int, IReadOnlyList<ShortcutEntry>> Shortcuts,
    IReadOnlyList<KeyboardShortcutRegistration> Registrations)
{
    public static ShortcutBindingSet Create(IEnumerable<ShortcutEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        Dictionary<int, List<ShortcutEntry>> shortcuts = [];
        List<KeyboardShortcutRegistration> registrations = [];
        Dictionary<KeyboardShortcut, int> idsByShortcut = [];

        foreach (ShortcutEntry entry in entries)
        {
            KeyboardShortcut shortcut = KeyboardShortcutParser.Parse(entry.Keys);
            if (!idsByShortcut.TryGetValue(shortcut, out int id))
            {
                id = idsByShortcut.Count + 1;
                idsByShortcut[shortcut] = id;
                shortcuts[id] = [];
                registrations.Add(new KeyboardShortcutRegistration(id, shortcut));
            }

            shortcuts[id].Add(entry);
        }

        return new ShortcutBindingSet(
            shortcuts.ToDictionary(static item => item.Key, static item => (IReadOnlyList<ShortcutEntry>)item.Value),
            registrations);
    }
}
