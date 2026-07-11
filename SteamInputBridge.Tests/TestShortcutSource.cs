using System;
using System.Collections.Generic;
using SteamInputBridge.Hosting;
using SteamInputBridge.Shortcuts;

namespace SteamInputBridge.Tests;

internal sealed class TestShortcutSource : IShortcutSource
{
    public event EventHandler<ShortcutEventArgs>? Shortcut;

    public IReadOnlyList<BridgeShortcutStatus> Status { get; init; } = [];

    public void Raise(
        int shortcutId,
        ShortcutTargetSetting target,
        ShortcutValue action,
        ShortcutPhase phase)
    {
        Shortcut?.Invoke(this, new(shortcutId, "Test", target, action, phase));
    }
}
