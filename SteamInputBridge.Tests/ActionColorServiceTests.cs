using System.Collections.Generic;
using SteamInputBridge.Shortcuts;
using SteamInputBridge.Shortcuts.Runtime;

namespace SteamInputBridge.Tests;

[TestClass]
public sealed class ActionColorServiceTests
{
    [TestMethod]
    public void ActionColorsStackByLastEnabledSource()
    {
        TestShortcutSource shortcuts = new();
        using ActionColorService colors = new(shortcuts);
        List<string?> changes = [];
        colors.ColorChanged += (_, args) => changes.Add(args.Color);

        shortcuts.Raise(1, Color("#FF0000"), ShortcutValue.Enable, ShortcutPhase.Pressed);
        shortcuts.Raise(2, Color("#00FF00"), ShortcutValue.Enable, ShortcutPhase.Pressed);
        shortcuts.Raise(3, Color("#0000FF"), ShortcutValue.Enable, ShortcutPhase.Pressed);
        shortcuts.Raise(2, Color("#00FF00"), ShortcutValue.Enable, ShortcutPhase.Released);
        shortcuts.Raise(3, Color("#0000FF"), ShortcutValue.Enable, ShortcutPhase.Released);

        Assert.AreEqual("#FF0000", colors.Color);
        CollectionAssert.AreEqual(
            new string?[] { "#FF0000", "#00FF00", "#0000FF", "#FF0000" },
            changes);
    }

    [TestMethod]
    public void DisableMasksSameColorUntilReleased()
    {
        TestShortcutSource shortcuts = new();
        using ActionColorService colors = new(shortcuts);

        shortcuts.Raise(1, Color("#FFFF00"), ShortcutValue.Enable, ShortcutPhase.Pressed);
        shortcuts.Raise(2, Color("#FFFF00"), ShortcutValue.Disable, ShortcutPhase.Pressed);

        Assert.IsNull(colors.Color);

        shortcuts.Raise(2, Color("#FFFF00"), ShortcutValue.Disable, ShortcutPhase.Released);

        Assert.AreEqual("#FFFF00", colors.Color);
    }

    [TestMethod]
    public void NonColorShortcutsAreIgnored()
    {
        TestShortcutSource shortcuts = new();
        using ActionColorService colors = new(shortcuts);

        shortcuts.Raise(
            1,
            new ShortcutTargetSetting(ShortcutTarget.Microphone, null),
            ShortcutValue.Enable,
            ShortcutPhase.Pressed);

        Assert.IsNull(colors.Color);
    }

    private static ShortcutTargetSetting Color(string value)
    {
        return new(ShortcutTarget.ActionColor, value);
    }
}
