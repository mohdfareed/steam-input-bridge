using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using SteamInputBridge.Hosting;
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
        using TestShortcutRuntime runtime = await TestShortcutRuntime.CreateStartedAsync(SettingsWithShortcuts())
            .ConfigureAwait(false);
        using TestGlobalShortcutListener listener = new();
        using ShortcutService service = new(runtime.Settings, runtime.Profiles, listener, NullLogger<ShortcutService>.Instance);
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
        using TestShortcutRuntime runtime = await TestShortcutRuntime.CreateStartedAsync(SettingsWithShortcuts())
            .ConfigureAwait(false);
        using TestGlobalShortcutListener listener = new();
        using ShortcutService service = new(runtime.Settings, runtime.Profiles, listener, NullLogger<ShortcutService>.Instance);
        await service.StartAsync(default).ConfigureAwait(false);
        listener.Press(1);

        runtime.Monitor.Set(SettingsWithShortcut("Alt+F2"));

        Assert.HasCount(1, listener.Registrations);
        Assert.AreEqual("Alt+F2", service.Status[0].Keys);
        Assert.IsFalse(service.Status[0].Pressed);
    }

    [TestMethod]
    public async Task ProfileShortcutsRegisterOnlyForActiveProfile()
    {
        using TestShortcutRuntime runtime = await TestShortcutRuntime.CreateStartedAsync(SettingsWithProfileShortcut())
            .ConfigureAwait(false);
        using TestGlobalShortcutListener listener = new();
        using ShortcutService service = new(runtime.Settings, runtime.Profiles, listener, NullLogger<ShortcutService>.Instance);

        await service.StartAsync(default).ConfigureAwait(false);
        Assert.HasCount(1, listener.Registrations);
        Assert.AreEqual("F1", listener.Registrations[0].Shortcut.ToString());

        await runtime.ActivateAsync("game").ConfigureAwait(false);

        Assert.HasCount(2, listener.Registrations);
        Assert.AreEqual("F1", listener.Registrations[0].Shortcut.ToString());
        Assert.AreEqual("F2", listener.Registrations[1].Shortcut.ToString());

        await runtime.DeactivateAsync().ConfigureAwait(false);

        Assert.HasCount(1, listener.Registrations);
        Assert.AreEqual("F1", listener.Registrations[0].Shortcut.ToString());
    }

    [TestMethod]
    public async Task ProfileShortcutReleasesWhenProfileStopsBeingActive()
    {
        using TestShortcutRuntime runtime = await TestShortcutRuntime
            .CreateStartedAsync(SettingsWithProfileShortcut(globalShortcut: false))
            .ConfigureAwait(false);
        using TestGlobalShortcutListener listener = new();
        await runtime.ActivateAsync("game").ConfigureAwait(false);
        using ShortcutService service = new(runtime.Settings, runtime.Profiles, listener, NullLogger<ShortcutService>.Instance);
        List<ShortcutEventArgs> events = [];
        service.Shortcut += (_, args) => events.Add(args);
        await service.StartAsync(default).ConfigureAwait(false);
        listener.Press(1);

        await runtime.DeactivateAsync().ConfigureAwait(false);
        await WaitUntilAsync(() => events.Count == 2).ConfigureAwait(false);

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
            Title = "Game",
        };
        settings.Games["game"].ReceiverProcesses.Add(Process.GetCurrentProcess().ProcessName);
        return settings;
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(3));
        while (!condition())
        {
            await Task.Delay(10, timeout.Token).ConfigureAwait(false);
        }
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

    private sealed class TestShortcutRuntime : IDisposable
    {
        private readonly ProfileCatalogService _catalog;
        private readonly ProfileClientsService _clients;
        private readonly TestForeground _foreground;
        private readonly TestClientApi _client = new();
        private Guid? _connectionId;

        private TestShortcutRuntime(
            TestOptionsMonitor<SteamInputBridgeSettings> monitor,
            SettingsService settings,
            ProfileCatalogService catalog,
            ProfileClientsService clients,
            ActiveProfileService profiles,
            TestForeground foreground)
        {
            Monitor = monitor;
            Settings = settings;
            _catalog = catalog;
            _clients = clients;
            Profiles = profiles;
            _foreground = foreground;
        }

        public TestOptionsMonitor<SteamInputBridgeSettings> Monitor { get; }

        public SettingsService Settings { get; }

        public ActiveProfileService Profiles { get; }

        public static async Task<TestShortcutRuntime> CreateStartedAsync(SteamInputBridgeSettings initialSettings)
        {
            TestOptionsMonitor<SteamInputBridgeSettings> monitor = new(initialSettings);
            SettingsService settings = new(
                monitor,
                new SettingsFile(@"C:\Tests\appsettings.json"),
                NullLogger<SettingsService>.Instance);
            ProfileCatalogService catalog = new(settings);
            ProfileClientsService clients = new(catalog, NullLogger<ProfileClientsService>.Instance);
            TestForeground foreground = new();
            ActiveProfileService profiles = new(
                catalog,
                clients,
                () => foreground.ProcessId,
                TimeSpan.FromMilliseconds(10));

            await catalog.StartAsync(default).ConfigureAwait(false);
            await profiles.StartAsync(default).ConfigureAwait(false);
            return new(monitor, settings, catalog, clients, profiles, foreground);
        }

        public async Task ActivateAsync(string profileId)
        {
            if (!_connectionId.HasValue)
            {
                _connectionId = Guid.NewGuid();
                _ = await _clients
                    .ConnectClientAsync(_connectionId.Value, 1234, profileId, steamAppId: null, _client)
                    .ConfigureAwait(false);
            }

            _foreground.ProcessId = Environment.ProcessId;
            await WaitUntilAsync(() => Profiles.ActiveProfile?.Id == profileId).ConfigureAwait(false);
        }

        public async Task DeactivateAsync()
        {
            _foreground.ProcessId = null;
            await WaitUntilAsync(() => Profiles.ActiveProfile is null).ConfigureAwait(false);
        }

        public void Dispose()
        {
            Profiles.Dispose();
            _clients.Dispose();
            _catalog.Dispose();
            Settings.Dispose();
        }
    }

    private sealed class TestForeground
    {
        public int? ProcessId { get; set; }
    }

    private sealed class TestClientApi : IBridgeClientApi
    {
        public Task StopAsync()
        {
            return Task.CompletedTask;
        }

        public Task<BridgeClientRuntimeStatus> GetStatusAsync()
        {
            return Task.FromResult(new BridgeClientRuntimeStatus(new(false, 0, 0)));
        }

        public Task SetActiveAsync(bool active)
        {
            _ = active;
            return Task.CompletedTask;
        }
    }
}
