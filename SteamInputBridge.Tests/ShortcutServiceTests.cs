using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using SteamInputBridge.Profiles;
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

    [TestMethod]
    public async Task ProfileShortcutsRegisterOnlyForActiveProfile()
    {
        TestOptionsMonitor<SteamInputBridgeSettings> monitor = new(SettingsWithProfileShortcut());
        using SettingsService settings = new(
            monitor,
            new SettingsFile(@"C:\Tests\appsettings.json"),
            NullLogger<SettingsService>.Instance);
        using TestGlobalShortcutListener listener = new();
        TestActiveProfileSource profiles = new();
        using ShortcutService service = new(
            settings,
            listener,
            NullLogger<ShortcutService>.Instance,
            profiles.ActiveProfileId,
            profiles.Subscribe,
            profiles.Unsubscribe);

        await service.StartAsync(default).ConfigureAwait(false);
        Assert.HasCount(1, listener.Registrations);
        Assert.AreEqual("F1", listener.Registrations[0].Shortcut.ToString());

        profiles.SetActive("game");

        Assert.HasCount(2, listener.Registrations);
        Assert.AreEqual("F1", listener.Registrations[0].Shortcut.ToString());
        Assert.AreEqual("F2", listener.Registrations[1].Shortcut.ToString());

        profiles.SetActive(null);

        Assert.HasCount(1, listener.Registrations);
        Assert.AreEqual("F1", listener.Registrations[0].Shortcut.ToString());
    }

    [TestMethod]
    public async Task ProfileShortcutReleasesWhenProfileStopsBeingActive()
    {
        TestOptionsMonitor<SteamInputBridgeSettings> monitor = new(SettingsWithProfileShortcut(globalShortcut: false));
        using SettingsService settings = new(
            monitor,
            new SettingsFile(@"C:\Tests\appsettings.json"),
            NullLogger<SettingsService>.Instance);
        using TestGlobalShortcutListener listener = new();
        TestActiveProfileSource profiles = new();
        profiles.SetActive("game");
        using ShortcutService service = new(
            settings,
            listener,
            NullLogger<ShortcutService>.Instance,
            profiles.ActiveProfileId,
            profiles.Subscribe,
            profiles.Unsubscribe);
        List<ShortcutEventArgs> events = [];
        service.Shortcut += (_, args) => events.Add(args);
        await service.StartAsync(default).ConfigureAwait(false);
        listener.Press(1);

        profiles.SetActive(null);

        Assert.HasCount(2, events);
        Assert.AreEqual(ShortcutPhase.Pressed, events[0].Phase);
        Assert.AreEqual(ShortcutPhase.Released, events[1].Phase);
        Assert.HasCount(0, listener.Registrations);
    }

    private static SteamInputBridgeSettings SettingsWithShortcuts()
    {
        SteamInputBridgeSettings settings = ValidBaseSettings();
        settings.Shortcuts.Add(new ShortcutEntry
        {
            Keys = "Ctrl+Alt+F1",
            Target = new ShortcutTargetSetting(ShortcutTarget.Microphone, null),
            Action = ShortcutValue.Enable,
        });
        settings.Shortcuts.Add(new ShortcutEntry
        {
            Keys = "Ctrl+Alt+F1",
            Target = new ShortcutTargetSetting(ShortcutTarget.ActionColor, "#808080"),
            Action = ShortcutValue.Toggle,
        });
        return settings;
    }

    private static SteamInputBridgeSettings SettingsWithShortcut(string keys)
    {
        SteamInputBridgeSettings settings = ValidBaseSettings();
        settings.Shortcuts.Add(new ShortcutEntry
        {
            Keys = keys,
            Target = new ShortcutTargetSetting(ShortcutTarget.MousePointer, null),
            Action = ShortcutValue.Toggle,
        });
        return settings;
    }

    private static SteamInputBridgeSettings SettingsWithProfileShortcut(bool globalShortcut = true)
    {
        SteamInputBridgeSettings settings = ValidBaseSettings();
        if (globalShortcut)
        {
            settings.Shortcuts.Add(new ShortcutEntry
            {
                Keys = "F1",
                Target = new ShortcutTargetSetting(ShortcutTarget.MousePointer, null),
                Action = ShortcutValue.Toggle,
            });
        }

        settings.Games["game"].Shortcuts.Add(new ShortcutEntry
        {
            Keys = "F2",
            Target = new ShortcutTargetSetting(ShortcutTarget.Microphone, null),
            Action = ShortcutValue.Enable,
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

    private sealed class TestActiveProfileSource
    {
        private event EventHandler<ActiveProfileChangedEventArgs>? Changed;

        private string? ProfileId { get; set; }

        public string? ActiveProfileId()
        {
            return ProfileId;
        }

        public void Subscribe(EventHandler<ActiveProfileChangedEventArgs> handler)
        {
            Changed += handler;
        }

        public void Unsubscribe(EventHandler<ActiveProfileChangedEventArgs> handler)
        {
            Changed -= handler;
        }

        public void SetActive(string? profileId)
        {
            ProfileId = profileId;
            Changed?.Invoke(this, new(null));
        }
    }
}
