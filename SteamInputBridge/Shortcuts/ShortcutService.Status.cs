using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using SteamInputBridge.Hosting;
using SteamInputBridge.Settings;

namespace SteamInputBridge.Shortcuts;

public sealed partial class ShortcutService
{
    // MARK: Status
    // ============================================================================

    private List<BridgeShortcutStatus> GetStatus()
    {
        lock (_gate)
        {
            List<BridgeShortcutStatus> status = [];
            foreach ((int shortcutId, IReadOnlyList<ShortcutEntry> entries) in _shortcutEntries)
            {
                foreach (ShortcutEntry entry in entries)
                {
                    if (!entry.Target.HasValue)
                    {
                        continue;
                    }

                    status.Add(new(
                        entry.Keys,
                        entry.Target.Value.ToString(),
                        entry.Action.ToString(),
                        _pressedShortcuts.Contains(shortcutId)));
                }
            }

            return status;
        }
    }

    // MARK: Logging
    // ============================================================================

    private static readonly Action<ILogger, int, Exception?> LogShortcutsRegistered =
        LoggerMessage.Define<int>(
            LogLevel.Information,
            new EventId(1, nameof(LogShortcutsRegistered)),
            "Registered {ShortcutCount} keyboard shortcut(s).");

    private static readonly Action<ILogger, string, Exception?> LogShortcutRegistrationFailed =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(2, nameof(LogShortcutRegistrationFailed)),
            "Keyboard shortcut registration failed: {Message}");
}
