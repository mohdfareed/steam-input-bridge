using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using SteamInputBridge.Forwarding.Mouse;
using SteamInputBridge.Hosting;
using SteamInputBridge.Hosting.Server;
using SteamInputBridge.Inputs.Mouse;
using SteamInputBridge.Outputs.Mouse;
using SteamInputBridge.Settings;
using SteamInputBridge.Shortcuts;
using SteamInputBridge.Shortcuts.Runtime;
using SteamInputBridge.Steam;

namespace SteamInputBridge.Tests;

[TestClass]
public sealed class BridgeServiceTests
{
    [TestMethod]
    public async Task StatusAggregatesProfilesClientsShortcutsMouseControllerAndSteamInput()
    {
        using ForwardingServiceTests.TestProfileRuntime runtime =
            await ForwardingServiceTests.TestProfileRuntime.CreateStartedAsync(MouseOutput.Viiper)
                .ConfigureAwait(false);
        TestOptionsMonitor<SteamInputBridgeSettings> monitor = new(SettingsWithShortcut());
        using SettingsService settings = new(
            monitor,
            new SettingsFile(@"C:\Tests\appsettings.json"),
            NullLogger<SettingsService>.Instance);
        using TestGlobalShortcutListener listener = new();
        using ShortcutService shortcuts = new(settings, listener, NullLogger<ShortcutService>.Instance);
        await shortcuts.StartAsync(default).ConfigureAwait(false);
        await using ServerMouseForwardingService mouse = new(
            runtime.ActiveProfiles,
            shortcuts,
            new TestMouseInputSourceFactory(),
            new TestMouseOutputFactory(),
            NullLogger<ServerMouseForwardingService>.Instance);
        SteamInputConfigService steamInput = new(
            runtime.ActiveProfiles,
            NullLogger<SteamInputConfigService>.Instance,
            new SteamInputClient((_, _) => ValueTask.CompletedTask));
        BridgeService service = new(shortcuts, runtime.Clients, runtime.ActiveProfiles, mouse, steamInput);

        BridgeServerStatus status = await service.GetStatusAsync().ConfigureAwait(false);

        Assert.AreEqual(1, status.ClientsCount);
        Assert.HasCount(1, status.Profiles);
        Assert.HasCount(1, status.Shortcuts);
        Assert.AreEqual("None", status.Mouse.Output);
        Assert.AreEqual(1, status.Controller.SteamControllers);
        Assert.AreEqual(1, status.Controller.VirtualControllers);
    }

    private static SteamInputBridgeSettings SettingsWithShortcut()
    {
        SteamInputBridgeSettings settings = new();
        settings.Games["game"] = new GameProfile
        {
            Executable = @"C:\Games\Game\game.exe",
        };
        settings.Shortcuts.Add(new ShortcutEntry
        {
            Keys = "F13",
            Action = ShortcutValue.Toggle,
            Targets =
            {
                new ShortcutTargetSetting(ShortcutTarget.MousePointer, null),
            },
        });
        return settings;
    }

    private sealed class TestGlobalShortcutListener : IGlobalShortcutListener
    {
        public void Update(
            IReadOnlyList<KeyboardShortcutRegistration> shortcuts,
            Action<int> pressed,
            Action<int> released)
        {
            _ = shortcuts;
            _ = pressed;
            _ = released;
        }

        public void Dispose()
        {
        }
    }

    private sealed class TestMouseInputSourceFactory : IMouseInputSourceFactory
    {
        public ValueTask<IMouseInputSource> ConnectAsync(CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;
            throw new NotSupportedException();
        }
    }

    private sealed class TestMouseOutputFactory : IMouseOutputFactory
    {
        public ValueTask<IMouseOutput> ConnectAsync(MouseOutput output, CancellationToken cancellationToken = default)
        {
            _ = output;
            _ = cancellationToken;
            throw new NotSupportedException();
        }
    }
}
