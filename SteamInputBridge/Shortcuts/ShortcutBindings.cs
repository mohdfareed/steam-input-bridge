using System;
using System.Collections.Generic;
using System.Linq;
using SteamInputBridge.Settings;

namespace SteamInputBridge.Shortcuts;

/// <summary>Compiled keyboard shortcut registrations and settings entries.</summary>
internal sealed record KeyboardShortcutBindingSet(
    IReadOnlyDictionary<int, IReadOnlyList<ShortcutEntry>> Shortcuts,
    IReadOnlyDictionary<int, KeyboardShortcutCombination> Combinations,
    IReadOnlyList<KeyboardShortcutRegistration> Registrations)
{
    internal static KeyboardShortcutBindingSet Create(
        IEnumerable<ShortcutEntry> entries,
        Action<ShortcutEntry, int, FormatException> skipped)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(skipped);

        Dictionary<int, List<ShortcutEntry>> shortcuts = [];
        List<KeyboardShortcutRegistration> registrations = [];
        Dictionary<KeyboardShortcutCombination, int> idsByCombination = [];
        int index = 0;

        foreach (ShortcutEntry entry in entries)
        {
            KeyboardShortcutCombination combination;
            try
            {
                combination = KeyboardShortcutParser.Parse(entry.Keys);
            }
            catch (FormatException exception)
            {
                skipped(entry, index, exception);
                index++;
                continue;
            }

            if (!idsByCombination.TryGetValue(combination, out int id))
            {
                id = idsByCombination.Count + 1;
                idsByCombination[combination] = id;
                shortcuts[id] = [];
                registrations.Add(new KeyboardShortcutRegistration(id, combination));
            }

            shortcuts[id].Add(entry);
            index++;
        }

        return new KeyboardShortcutBindingSet(
            shortcuts.ToDictionary(
                static item => item.Key,
                static item => (IReadOnlyList<ShortcutEntry>)item.Value),
            idsByCombination.ToDictionary(
                static item => item.Value,
                static item => item.Key),
            registrations);
    }
}
