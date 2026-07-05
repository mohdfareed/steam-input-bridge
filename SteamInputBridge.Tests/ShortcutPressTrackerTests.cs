using System.Collections.Generic;
using System.Windows.Forms;
using SteamInputBridge.Shortcuts.Runtime;

namespace SteamInputBridge.Tests;

[TestClass]
public sealed class ShortcutPressTrackerTests
{
    private const ushort Control = (ushort)Keys.ControlKey;

    private static readonly ushort Num7 = KeyboardShortcutParser.Parse("Num7").VirtualKey;
    private static readonly ushort NumPeriod = KeyboardShortcutParser.Parse("Num.").VirtualKey;

    private readonly HashSet<ushort> _down = [];
    private readonly List<int> _pressed = [];
    private readonly List<int> _released = [];

    [TestMethod]
    public void PlainShortcutStaysPressedWhenExtraModifierArrivesAfterCommit()
    {
        ShortcutPressTracker tracker = CreateTracker(Registration(1, "Num7"));

        _ = _down.Add(Num7);
        Assert.IsTrue(tracker.HotkeyPressed(1));

        CollectionAssert.AreEqual(new[] { 1 }, _pressed);

        tracker.Refresh();

        _ = _down.Add(Control);
        tracker.Refresh();

        Assert.HasCount(0, _released);

        _ = _down.Remove(Num7);
        tracker.Refresh();

        CollectionAssert.AreEqual(new[] { 1 }, _released);
    }

    [TestMethod]
    public void ModifierShortcutCanWinGraceWindowWhenModifierArrivesBeforeFirstPoll()
    {
        ShortcutPressTracker tracker = CreateTracker(
            Registration(1, "Num."),
            Registration(2, "Ctrl+Num."));

        _ = _down.Add(NumPeriod);
        Assert.IsTrue(tracker.HotkeyPressed(1));
        Assert.HasCount(0, _pressed);

        _ = _down.Add(Control);
        tracker.Refresh();

        CollectionAssert.AreEqual(new[] { 2 }, _pressed);
        Assert.HasCount(0, _released);
    }

    [TestMethod]
    public void CommittedShortcutDoesNotSwitchToModifierVariant()
    {
        ShortcutPressTracker tracker = CreateTracker(
            Registration(1, "Num."),
            Registration(2, "Ctrl+Num."));

        _ = _down.Add(NumPeriod);
        Assert.IsTrue(tracker.HotkeyPressed(1));
        tracker.Refresh();

        _ = _down.Add(Control);
        Assert.IsFalse(tracker.HotkeyPressed(2));
        tracker.Refresh();

        CollectionAssert.AreEqual(new[] { 1 }, _pressed);
        Assert.HasCount(0, _released);
    }

    [TestMethod]
    public void ModifierShortcutDoesNotFallBackToPlainUntilMainKeyIsReleased()
    {
        ShortcutPressTracker tracker = CreateTracker(
            Registration(1, "Num."),
            Registration(2, "Ctrl+Num."));

        _ = _down.Add(Control);
        _ = _down.Add(NumPeriod);
        Assert.IsTrue(tracker.HotkeyPressed(2));
        tracker.Refresh();

        _ = _down.Remove(Control);
        tracker.Refresh();

        Assert.IsFalse(tracker.HotkeyPressed(1));
        tracker.Refresh();

        CollectionAssert.AreEqual(new[] { 2 }, _pressed);
        CollectionAssert.AreEqual(new[] { 2 }, _released);

        _ = _down.Remove(NumPeriod);
        tracker.Refresh();
        _ = _down.Add(NumPeriod);
        Assert.IsTrue(tracker.HotkeyPressed(1));
        tracker.Refresh();

        CollectionAssert.AreEqual(new[] { 2, 1 }, _pressed);
    }

    private ShortcutPressTracker CreateTracker(params KeyboardShortcutRegistration[] registrations)
    {
        ShortcutPressTracker tracker = new(_down.Contains);
        tracker.Update(registrations, _pressed.Add, _released.Add);
        return tracker;
    }

    private static KeyboardShortcutRegistration Registration(int id, string keys)
    {
        return new(id, KeyboardShortcutParser.Parse(keys));
    }
}
