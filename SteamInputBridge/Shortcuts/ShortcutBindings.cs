using System;
using System.Collections.Generic;
using System.Linq;
using SteamInputBridge.Settings;

namespace SteamInputBridge.Shortcuts;

/// <summary>Compiled keyboard shortcut registrations and settings entries.</summary>
internal sealed record KeyboardShortcutBindingSet(
    IReadOnlyDictionary<int, IReadOnlyList<ShortcutEntry>> Shortcuts,
    IReadOnlyList<KeyboardShortcutRegistration> Registrations)
{
    internal static KeyboardShortcutBindingSet Create(IEnumerable<ShortcutEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        Dictionary<int, List<ShortcutEntry>> shortcuts = [];
        List<KeyboardShortcutRegistration> registrations = [];
        Dictionary<KeyboardShortcutCombination, int> idsByCombination = [];

        foreach (ShortcutEntry entry in entries)
        {
            KeyboardShortcutCombination combination = KeyboardShortcutParser.Parse(entry.Keys);
            if (!idsByCombination.TryGetValue(combination, out int id))
            {
                id = idsByCombination.Count + 1;
                idsByCombination[combination] = id;
                shortcuts[id] = [];
                registrations.Add(new KeyboardShortcutRegistration(id, combination));
            }

            shortcuts[id].Add(entry);
        }

        return new KeyboardShortcutBindingSet(
            shortcuts.ToDictionary(
                static item => item.Key,
                static item => (IReadOnlyList<ShortcutEntry>)item.Value),
            registrations);
    }
}
