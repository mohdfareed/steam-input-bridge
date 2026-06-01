using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using SteamInputBridge.Microphone;
using SteamInputBridge.Shortcuts;
using SteamInputBridge.Shortcuts.Runtime;

namespace SteamInputBridge.Tests;

[TestClass]
public sealed class MicrophoneShortcutServiceTests
{
    [TestMethod]
    public async Task EnableShortcutUnmutesOnlyWhileHeld()
    {
        TestShortcutSource shortcuts = new();
        TestMicrophoneControl microphone = new(new(Available: true, Muted: true, IsActive: false));
        using MicrophoneShortcutService service = new(
            shortcuts,
            microphone,
            NullLogger<MicrophoneShortcutService>.Instance);
        await service.StartAsync(default).ConfigureAwait(false);

        shortcuts.Raise(
            1,
            new ShortcutTargetSetting(ShortcutTarget.Microphone, null),
            ShortcutValue.Enable,
            ShortcutPhase.Pressed);
        shortcuts.Raise(
            1,
            new ShortcutTargetSetting(ShortcutTarget.Microphone, null),
            ShortcutValue.Enable,
            ShortcutPhase.Released);

        CollectionAssert.AreEqual(new[] { true, false }, microphone.EnabledCalls);
    }

    [TestMethod]
    public async Task ToggleClearsHeldState()
    {
        TestShortcutSource shortcuts = new();
        TestMicrophoneControl microphone = new(new(Available: true, Muted: true, IsActive: false));
        using MicrophoneShortcutService service = new(
            shortcuts,
            microphone,
            NullLogger<MicrophoneShortcutService>.Instance);
        await service.StartAsync(default).ConfigureAwait(false);

        shortcuts.Raise(
            1,
            new ShortcutTargetSetting(ShortcutTarget.Microphone, null),
            ShortcutValue.Enable,
            ShortcutPhase.Pressed);
        shortcuts.Raise(
            2,
            new ShortcutTargetSetting(ShortcutTarget.Microphone, null),
            ShortcutValue.Toggle,
            ShortcutPhase.Pressed);
        shortcuts.Raise(
            1,
            new ShortcutTargetSetting(ShortcutTarget.Microphone, null),
            ShortcutValue.Enable,
            ShortcutPhase.Released);

        CollectionAssert.AreEqual(new[] { true, false, false }, microphone.EnabledCalls);
    }

    private sealed class TestMicrophoneControl(MicrophoneStatus status) : IMicrophoneControl
    {
        private MicrophoneStatus _status = status;

        public List<bool> EnabledCalls { get; } = [];

        public MicrophoneStatus GetStatus()
        {
            return _status;
        }

        public void SetEnabled(bool enabled)
        {
            EnabledCalls.Add(enabled);
            _status = _status with { Muted = !enabled };
        }
    }
}
