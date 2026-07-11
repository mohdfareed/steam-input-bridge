using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows.Forms;
using SteamInputBridge.Shortcuts.Runtime;

namespace SteamInputBridge.Tests;

[TestClass]
public sealed class ShortcutPressTrackerTests
{
    private const ushort Control = (ushort)Keys.ControlKey;
    private const ushort LeftControl = (ushort)Keys.LControlKey;
    private const ushort RightControl = (ushort)Keys.RControlKey;
    private const ushort Alt = (ushort)Keys.Menu;

    private static readonly ushort Num7 = KeyboardShortcutParser.Parse("Num7").VirtualKey;
    private static readonly ushort NumPeriod = KeyboardShortcutParser.Parse("Num.").VirtualKey;

    private readonly HashSet<ushort> _down = [];
    private readonly List<int> _pressed = [];
    private readonly List<int> _released = [];
    private long _now;

    [TestMethod]
    public void PlainShortcutPressesWhenNoModifiersAreDown()
    {
        ShortcutPressTracker tracker = CreateTracker(Registration(1, "Num7"));

        Assert.IsTrue(Press(tracker, Num7));

        CollectionAssert.AreEqual(new[] { 1 }, _pressed);
    }

    [TestMethod]
    public void PlainShortcutDoesNotPressWhenExtraModifierIsDown()
    {
        ShortcutPressTracker tracker = CreateTracker(Registration(1, "Num7"));

        Assert.IsFalse(Press(tracker, Control));
        Assert.IsFalse(Press(tracker, Num7));

        Assert.HasCount(0, _pressed);
    }

    [TestMethod]
    public void PlainShortcutPressesWhenExtraModifierReleasesWhileMainKeyIsHeld()
    {
        ShortcutPressTracker tracker = CreateTracker(Registration(1, "Num7"));

        Assert.IsFalse(Press(tracker, Alt));
        Assert.IsFalse(Press(tracker, Num7));
        Assert.IsTrue(Release(tracker, Alt));

        CollectionAssert.AreEqual(new[] { 1 }, _pressed);
    }

    [TestMethod]
    public void PlainShortcutStaysPressedWhenExtraModifierIsPressed()
    {
        ShortcutPressTracker tracker = CreateTracker(Registration(1, "Num7"));

        Assert.IsTrue(Press(tracker, Num7));
        Assert.IsFalse(Press(tracker, Alt));
        Assert.IsTrue(Release(tracker, Num7));

        CollectionAssert.AreEqual(new[] { 1 }, _pressed);
        CollectionAssert.AreEqual(new[] { 1 }, _released);
    }

    [TestMethod]
    public void PlainShortcutPressesWhenModifierReleaseEventHasStaleAsyncState()
    {
        ShortcutPressTracker tracker = CreateTracker(Registration(1, "Num7"));

        Assert.IsFalse(Press(tracker, Alt));
        Assert.IsFalse(Press(tracker, Num7));
        Assert.IsTrue(tracker.KeyReleased(Alt));

        CollectionAssert.AreEqual(new[] { 1 }, _pressed);
    }

    [TestMethod]
    public void PlainShortcutPressReconcilesStaleModifierState()
    {
        ShortcutPressTracker tracker = CreateTracker(Registration(1, "Num7"));

        Assert.IsFalse(tracker.KeyPressed(Alt));
        Assert.IsTrue(Press(tracker, Num7));

        CollectionAssert.AreEqual(new[] { 1 }, _pressed);
    }

    [TestMethod]
    public void ModifierShortcutPressesWhenModifierIsAlreadyDown()
    {
        ShortcutPressTracker tracker = CreateTracker(Registration(1, "Ctrl+Num."));

        Assert.IsFalse(Press(tracker, Control));
        Assert.IsTrue(Press(tracker, NumPeriod));

        CollectionAssert.AreEqual(new[] { 1 }, _pressed);
    }

    [TestMethod]
    public void ModifierShortcutStaysPressedWhenUnrelatedModifierIsPressed()
    {
        ShortcutPressTracker tracker = CreateTracker(Registration(1, "Ctrl+Num."));

        Assert.IsFalse(Press(tracker, Control));
        Assert.IsTrue(Press(tracker, NumPeriod));
        Assert.IsFalse(Press(tracker, Alt));
        Assert.IsTrue(Release(tracker, Control));

        CollectionAssert.AreEqual(new[] { 1 }, _pressed);
        CollectionAssert.AreEqual(new[] { 1 }, _released);
    }

    [TestMethod]
    public void GenericModifierShortcutPressesWithLeftOrRightModifier()
    {
        ShortcutPressTracker leftTracker = CreateTracker(Registration(1, "Ctrl+Num."));
        Assert.IsFalse(Press(leftTracker, LeftControl));
        Assert.IsTrue(Press(leftTracker, NumPeriod));
        CollectionAssert.AreEqual(new[] { 1 }, _pressed);

        _down.Clear();
        _pressed.Clear();
        _released.Clear();
        ShortcutPressTracker rightTracker = CreateTracker(Registration(2, "Ctrl+Num."));
        Assert.IsFalse(Press(rightTracker, RightControl));
        Assert.IsTrue(Press(rightTracker, NumPeriod));
        CollectionAssert.AreEqual(new[] { 2 }, _pressed);
    }

    [TestMethod]
    public void SideSpecificModifierShortcutRequiresThatSide()
    {
        ShortcutPressTracker tracker = CreateTracker(Registration(1, "LCtrl+Num."));

        Assert.IsFalse(Press(tracker, RightControl));
        Assert.IsFalse(Press(tracker, NumPeriod));

        Assert.HasCount(0, _pressed);
    }

    [TestMethod]
    public void SideSpecificModifierShortcutPressesWithMatchingSide()
    {
        ShortcutPressTracker tracker = CreateTracker(Registration(1, "RCtrl+Num."));

        Assert.IsFalse(Press(tracker, RightControl));
        Assert.IsTrue(Press(tracker, NumPeriod));

        CollectionAssert.AreEqual(new[] { 1 }, _pressed);
    }

    [TestMethod]
    public void ReleasingOneSideModifierKeepsOtherSidePressed()
    {
        ShortcutPressTracker tracker = CreateTracker(Registration(1, "Ctrl+Num."));

        Assert.IsFalse(Press(tracker, LeftControl));
        Assert.IsFalse(Press(tracker, RightControl));
        Assert.IsTrue(Press(tracker, NumPeriod));
        Assert.IsFalse(Release(tracker, LeftControl));

        CollectionAssert.AreEqual(new[] { 1 }, _pressed);
        Assert.HasCount(0, _released);
    }

    [TestMethod]
    public void ModifierShortcutDoesNotPressWhenModifierArrivesAfterMainKey()
    {
        ShortcutPressTracker tracker = CreateTracker(Registration(1, "Ctrl+Num."));

        Assert.IsFalse(Press(tracker, NumPeriod));

        AdvanceMilliseconds(6);
        Assert.IsFalse(Press(tracker, Control));

        Assert.HasCount(0, _pressed);
    }

    [TestMethod]
    public void ModifierShortcutPressesWhenModifierArrivesWithinTolerance()
    {
        ShortcutPressTracker tracker = CreateTracker(Registration(1, "Ctrl+Num."));

        Assert.IsFalse(Press(tracker, NumPeriod));

        AdvanceMilliseconds(4);
        Assert.IsTrue(Press(tracker, Control));

        CollectionAssert.AreEqual(new[] { 1 }, _pressed);
    }

    [TestMethod]
    public void ModifierShortcutDoesNotPressFromRepeatAfterModifierArrives()
    {
        ShortcutPressTracker tracker = CreateTracker(Registration(1, "Ctrl+Num."));

        Assert.IsFalse(Press(tracker, NumPeriod));

        AdvanceMilliseconds(6);
        Assert.IsFalse(Press(tracker, Control));
        Assert.IsFalse(Press(tracker, NumPeriod));

        Assert.HasCount(0, _pressed);
    }

    [TestMethod]
    public void ExactModifierShortcutDoesNotPressPlainShortcut()
    {
        ShortcutPressTracker tracker = CreateTracker(
            Registration(1, "Num."),
            Registration(2, "Ctrl+Num."));

        Assert.IsFalse(Press(tracker, Control));
        Assert.IsTrue(Press(tracker, NumPeriod));

        CollectionAssert.AreEqual(new[] { 2 }, _pressed);
    }

    [TestMethod]
    public void RepeatKeyDownDoesNotPressAgainUntilRelease()
    {
        ShortcutPressTracker tracker = CreateTracker(Registration(1, "Num7"));

        Assert.IsTrue(Press(tracker, Num7));
        Assert.IsFalse(Press(tracker, Num7));

        CollectionAssert.AreEqual(new[] { 1 }, _pressed);
    }

    [TestMethod]
    public void MainKeyReleaseReleasesPressedShortcut()
    {
        ShortcutPressTracker tracker = CreateTracker(Registration(1, "Num7"));

        Assert.IsTrue(Press(tracker, Num7));

        Assert.IsTrue(Release(tracker, Num7));

        CollectionAssert.AreEqual(new[] { 1 }, _released);
    }

    [TestMethod]
    public void ModifierReleaseReleasesModifierShortcut()
    {
        ShortcutPressTracker tracker = CreateTracker(Registration(1, "Ctrl+Num."));

        Assert.IsFalse(Press(tracker, Control));
        Assert.IsTrue(Press(tracker, NumPeriod));

        Assert.IsTrue(Release(tracker, Control));

        CollectionAssert.AreEqual(new[] { 1 }, _released);
    }

    [TestMethod]
    public void CaptureCurrentlyHeldKeysReadsHeldStateAfterRegistration()
    {
        _ = _down.Add(Control);
        _ = _down.Add(NumPeriod);
        ShortcutPressTracker tracker = CreateTracker(
            Registration(1, "Num."),
            Registration(2, "Ctrl+Num."));

        IReadOnlyList<int> pressed = tracker.CaptureCurrentlyHeldKeys();

        CollectionAssert.AreEqual(new[] { 2 }, pressed.ToArray());
        Assert.HasCount(0, _pressed);
        Assert.IsFalse(Press(tracker, NumPeriod));
    }

    [TestMethod]
    public void CaptureCurrentlyHeldKeysRequiresExactModifiers()
    {
        _ = _down.Add(Control);
        _ = _down.Add(NumPeriod);
        ShortcutPressTracker tracker = CreateTracker(Registration(1, "Num."));

        IReadOnlyList<int> pressed = tracker.CaptureCurrentlyHeldKeys();

        Assert.HasCount(0, pressed);
        Assert.HasCount(0, _pressed);
    }

    private ShortcutPressTracker CreateTracker(params KeyboardShortcutRegistration[] registrations)
    {
        ShortcutPressTracker tracker = new(_down.Contains, () => _now);
        tracker.Update(
            registrations,
            (id, pressed) =>
            {
                if (pressed)
                {
                    _pressed.Add(id);
                }
                else
                {
                    _released.Add(id);
                }
            });
        return tracker;
    }

    private bool Press(ShortcutPressTracker tracker, ushort virtualKey)
    {
        _ = _down.Add(virtualKey);
        return tracker.KeyPressed(virtualKey);
    }

    private bool Release(ShortcutPressTracker tracker, ushort virtualKey)
    {
        _ = _down.Remove(virtualKey);
        return tracker.KeyReleased(virtualKey);
    }

    private void AdvanceMilliseconds(int milliseconds)
    {
        _now += Stopwatch.Frequency * milliseconds / 1000;
    }

    private static KeyboardShortcutRegistration Registration(int id, string keys)
    {
        return new(id, KeyboardShortcutParser.Parse(keys));
    }
}
