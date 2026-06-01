using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using SteamInputBridge.Settings;
using SteamInputBridge.Shortcuts;
using SteamInputBridge.Shortcuts.Runtime;

namespace SteamInputBridge.Tests;

[TestClass]
public sealed class ShortcutServiceTests
{
    [TestMethod]
    public async Task StartRegistersConfiguredShortcutsAndPublishesStatus()
    {
        TestOptionsMonitor<SteamInputBridgeSettings> monitor = new(SettingsWithShortcuts());
        using SettingsService settings = new(
            monitor,
            new SettingsFile(@"C:\Tests\appsettings.json"),
            NullLogger<SettingsService>.Instance);
        using TestGlobalShortcutListener listener = new();
        using ShortcutService service = new(settings, listener, NullLogger<ShortcutService>.Instance);
        List<ShortcutEventArgs> events = [];
        service.Shortcut += (_, args) => events.Add(args);

        await service.StartAsync(default).ConfigureAwait(false);
        listener.Press(1);
        listener.Release(1);

        Assert.HasCount(1, listener.Registrations);
        Assert.HasCount(2, service.Status);
        Assert.IsFalse(service.Status[0].Pressed);
        Assert.HasCount(3, events);
        Assert.AreEqual(ShortcutTarget.Microphone, events[0].Target.Target);
        Assert.AreEqual(ShortcutPhase.Pressed, events[0].Phase);
        Assert.AreEqual(ShortcutTarget.ActionColor, events[1].Target.Target);
        Assert.AreEqual(ShortcutPhase.Pressed, events[1].Phase);
        Assert.AreEqual(ShortcutTarget.Microphone, events[2].Target.Target);
        Assert.AreEqual(ShortcutPhase.Released, events[2].Phase);
    }

    [TestMethod]
    public async Task ReloadReplacesRegistrationsAndClearsPressedStatus()
    {
        TestOptionsMonitor<SteamInputBridgeSettings> monitor = new(SettingsWithShortcuts());
        using SettingsService settings = new(
            monitor,
            new SettingsFile(@"C:\Tests\appsettings.json"),
            NullLogger<SettingsService>.Instance);
        using TestGlobalShortcutListener listener = new();
        using ShortcutService service = new(settings, listener, NullLogger<ShortcutService>.Instance);
        await service.StartAsync(default).ConfigureAwait(false);
        listener.Press(1);

        monitor.Set(SettingsWithShortcut("Alt+F2"));

        Assert.HasCount(1, listener.Registrations);
        Assert.AreEqual("Alt+F2", service.Status[0].Keys);
        Assert.IsFalse(service.Status[0].Pressed);
    }

    private static SteamInputBridgeSettings SettingsWithShortcuts()
    {
        SteamInputBridgeSettings settings = ValidBaseSettings();
        settings.Shortcuts.Add(new ShortcutEntry
        {
            Keys = "Ctrl+Alt+F1",
            Action = ShortcutValue.Enable,
            Targets =
            {
                new ShortcutTargetSetting(ShortcutTarget.Microphone, null),
            },
        });
        settings.Shortcuts.Add(new ShortcutEntry
        {
            Keys = "Ctrl+Alt+F1",
            Action = ShortcutValue.Toggle,
            Targets =
            {
                new ShortcutTargetSetting(ShortcutTarget.ActionColor, "#808080"),
            },
        });
        return settings;
    }

    private static SteamInputBridgeSettings SettingsWithShortcut(string keys)
    {
        SteamInputBridgeSettings settings = ValidBaseSettings();
        settings.Shortcuts.Add(new ShortcutEntry
        {
            Keys = keys,
            Action = ShortcutValue.Toggle,
            Targets =
            {
                new ShortcutTargetSetting(ShortcutTarget.MousePointer, null),
            },
        });
        return settings;
    }

    private static SteamInputBridgeSettings ValidBaseSettings()
    {
        SteamInputBridgeSettings settings = new();
        settings.Games["game"] = new GameProfile
        {
            Executable = @"C:\Games\Game\game.exe",
        };
        return settings;
    }

    private sealed class TestGlobalShortcutListener : IGlobalShortcutListener
    {
        private Action<int>? _pressed;
        private Action<int>? _released;

        public IReadOnlyList<KeyboardShortcutRegistration> Registrations { get; private set; } = [];

        public void Update(
            IReadOnlyList<KeyboardShortcutRegistration> shortcuts,
            Action<int> pressed,
            Action<int> released)
        {
            Registrations = shortcuts;
            _pressed = pressed;
            _released = released;
        }

        public void Press(int id)
        {
            _pressed?.Invoke(id);
        }

        public void Release(int id)
        {
            _released?.Invoke(id);
        }

        public void Dispose()
        {
        }
    }
}
