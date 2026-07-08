using System;
using System.Collections.Generic;
using SteamInputBridge.Hosting;

namespace SteamInputBridge.Shortcuts.Runtime;

internal enum ShortcutPhase
{
    Pressed,
    Released,
}

internal sealed class ShortcutEventArgs(
    int shortcutId,
    string keys,
    ShortcutTargetSetting target,
    ShortcutValue action,
    ShortcutPhase phase) : EventArgs
{
    public int ShortcutId { get; } = shortcutId;

    public string Keys { get; } = keys;

    public ShortcutTargetSetting Target { get; } = target;

    public ShortcutValue Action { get; } = action;

    public ShortcutPhase Phase { get; } = phase;
}

internal interface IShortcutSource
{
    event EventHandler<ShortcutEventArgs>? Shortcut;

    IReadOnlyList<BridgeShortcutStatus> Status { get; }
}

internal interface IGlobalShortcutListener : IDisposable
{
    void Update(
        IReadOnlyList<KeyboardShortcutRegistration> shortcuts,
        Action<int> pressed,
        Action<int> released);
}
