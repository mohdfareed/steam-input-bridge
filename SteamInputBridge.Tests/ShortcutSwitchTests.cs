using SteamInputBridge.Shortcuts;
using SteamInputBridge.Shortcuts.Runtime;

namespace SteamInputBridge.Tests;

[TestClass]
public sealed class ShortcutSwitchTests
{
    [TestMethod]
    public void EnableHoldLeavesTargetDisabledWhenReleased()
    {
        ShortcutSwitch shortcutSwitch = new();

        Assert.IsTrue(shortcutSwitch.Apply(1, ShortcutValue.Enable, ShortcutPhase.Pressed, defaultEnabled: false));
        Assert.IsFalse(shortcutSwitch.Apply(1, ShortcutValue.Enable, ShortcutPhase.Released, defaultEnabled: false));
    }

    [TestMethod]
    public void DisableHoldLeavesTargetEnabledWhenReleased()
    {
        ShortcutSwitch shortcutSwitch = new();

        Assert.IsFalse(shortcutSwitch.Apply(1, ShortcutValue.Disable, ShortcutPhase.Pressed, defaultEnabled: true));
        Assert.IsTrue(shortcutSwitch.Apply(1, ShortcutValue.Disable, ShortcutPhase.Released, defaultEnabled: true));
    }

    [TestMethod]
    public void LastHeldShortcutWinsUntilReleased()
    {
        ShortcutSwitch shortcutSwitch = new();

        Assert.IsTrue(shortcutSwitch.Apply(1, ShortcutValue.Enable, ShortcutPhase.Pressed, defaultEnabled: false));
        Assert.IsFalse(shortcutSwitch.Apply(2, ShortcutValue.Disable, ShortcutPhase.Pressed, defaultEnabled: false));
        Assert.IsTrue(shortcutSwitch.Apply(2, ShortcutValue.Disable, ShortcutPhase.Released, defaultEnabled: false));
    }

    [TestMethod]
    public void ToggleClearsHeldShortcutsBeforeToggling()
    {
        ShortcutSwitch shortcutSwitch = new();

        Assert.IsTrue(shortcutSwitch.Apply(1, ShortcutValue.Enable, ShortcutPhase.Pressed, defaultEnabled: false));
        Assert.IsFalse(shortcutSwitch.Apply(2, ShortcutValue.Toggle, ShortcutPhase.Pressed, defaultEnabled: false));
        Assert.IsFalse(shortcutSwitch.Apply(1, ShortcutValue.Enable, ShortcutPhase.Released, defaultEnabled: false));
    }
}
