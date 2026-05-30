using System;
using SteamInputBridge.Shortcuts;

namespace SteamInputBridge.Tests;

[TestClass]
public sealed class KeyboardShortcutTests
{
    [TestMethod]
    public void ParsesModifierFunctionKeyCombination()
    {
        KeyboardShortcutCombination shortcut = KeyboardShortcutParser.Parse("Ctrl+Alt+F13");

        Assert.AreEqual(
            KeyboardShortcutModifiers.Control | KeyboardShortcutModifiers.Alt,
            shortcut.Modifiers);
        Assert.AreEqual((ushort)0x7c, shortcut.VirtualKey);
    }

    [TestMethod]
    public void RejectsCombinationWithoutKey()
    {
        FormatException exception = Assert.ThrowsExactly<FormatException>(
            static () => KeyboardShortcutParser.Parse("Ctrl+Alt"));

        StringAssert.Contains(exception.Message, "does not contain a key", StringComparison.Ordinal);
    }

    [TestMethod]
    public void ParsesNumpadKey()
    {
        KeyboardShortcutCombination shortcut = KeyboardShortcutParser.Parse("Numpad1");

        Assert.AreEqual(KeyboardShortcutModifiers.None, shortcut.Modifiers);
        Assert.AreEqual((ushort)0x61, shortcut.VirtualKey);
    }

    [TestMethod]
    public void ParsesNumAlias()
    {
        KeyboardShortcutCombination shortcut = KeyboardShortcutParser.Parse("Num9");

        Assert.AreEqual(KeyboardShortcutModifiers.None, shortcut.Modifiers);
        Assert.AreEqual((ushort)0x69, shortcut.VirtualKey);
    }

    [TestMethod]
    public void ParsesNumpadPlusKey()
    {
        KeyboardShortcutCombination shortcut = KeyboardShortcutParser.Parse("Ctrl+Num+");

        Assert.AreEqual(KeyboardShortcutModifiers.Control, shortcut.Modifiers);
        Assert.AreEqual((ushort)0x6B, shortcut.VirtualKey);
    }

    [TestMethod]
    public void FormatsNumpadKey()
    {
        KeyboardShortcutCombination shortcut = KeyboardShortcutParser.Parse("Ctrl+Num2");

        Assert.AreEqual("Ctrl+Num2", shortcut.ToString());
    }
}
