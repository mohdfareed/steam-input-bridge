using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using SteamInputBridge.Outputs.Mouse;
using SteamInputBridge.Profiles;
using SteamInputBridge.Settings;
using SteamInputBridge.Shortcuts;
using SteamInputBridge.Shortcuts.Runtime;
using SteamInputBridge.Steam;

namespace SteamInputBridge.Tests;

[TestClass]
public sealed class SteamInputConfigShortcutServiceTests
{
    [TestMethod]
    public async Task SteamShortcutOpensActiveProfileConfig()
    {
        TestShortcutSource shortcuts = new();
        List<Uri> opened = [];
        using ForwardingServiceTests.TestProfileRuntime runtime =
            await ForwardingServiceTests.TestProfileRuntime.CreateStartedAsync(MouseOutput.None).ConfigureAwait(false);
        await runtime.WaitForActiveProfileAsync().ConfigureAwait(false);
        using SteamInputConfigShortcutService service = CreateService(shortcuts, runtime.ActiveProfiles, opened);
        await service.StartAsync(default).ConfigureAwait(false);

        shortcuts.Raise(
            1,
            new ShortcutTargetSetting(ShortcutTarget.Steam, null),
            ShortcutValue.Toggle,
            ShortcutPhase.Pressed);

        Assert.HasCount(1, opened);
        Assert.AreEqual("steam://controllerconfig/123", opened[0].AbsoluteUri);
    }

    [TestMethod]
    public async Task SteamShortcutFallsBackToDesktopConfig()
    {
        TestShortcutSource shortcuts = new();
        List<Uri> opened = [];
        using SettingsService settings = TestSettings(new SteamInputBridgeSettings());
        using ProfileCatalogService catalog = new(settings);
        using ProfileClientsService clients = new(catalog, NullLogger<ProfileClientsService>.Instance);
        using ActiveProfileService profiles = new(catalog, clients, static () => null, TimeSpan.FromMilliseconds(10));
        using SteamInputConfigShortcutService service = CreateService(shortcuts, profiles, opened);
        await service.StartAsync(default).ConfigureAwait(false);

        shortcuts.Raise(
            1,
            new ShortcutTargetSetting(ShortcutTarget.Steam, null),
            ShortcutValue.Toggle,
            ShortcutPhase.Pressed);

        Assert.HasCount(1, opened);
        Assert.AreEqual("steam://controllerconfig/413080", opened[0].AbsoluteUri);
    }

    [TestMethod]
    public async Task SteamShortcutIgnoresReleaseEvents()
    {
        TestShortcutSource shortcuts = new();
        List<Uri> opened = [];
        using ForwardingServiceTests.TestProfileRuntime runtime =
            await ForwardingServiceTests.TestProfileRuntime.CreateStartedAsync(MouseOutput.None).ConfigureAwait(false);
        await runtime.WaitForActiveProfileAsync().ConfigureAwait(false);
        using SteamInputConfigShortcutService service = CreateService(shortcuts, runtime.ActiveProfiles, opened);
        await service.StartAsync(default).ConfigureAwait(false);

        shortcuts.Raise(
            1,
            new ShortcutTargetSetting(ShortcutTarget.Steam, null),
            ShortcutValue.Enable,
            ShortcutPhase.Released);

        Assert.HasCount(0, opened);
    }

    private static SteamInputConfigShortcutService CreateService(
        TestShortcutSource shortcuts,
        ActiveProfileService profiles,
        List<Uri> opened)
    {
        SteamInputClient steam = new((uri, cancellationToken) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            opened.Add(uri);
            return ValueTask.CompletedTask;
        });

        return new(shortcuts, profiles, NullLogger<SteamInputConfigShortcutService>.Instance, steam);
    }

    private static SettingsService TestSettings(SteamInputBridgeSettings settings)
    {
        return new(
            new TestOptionsMonitor<SteamInputBridgeSettings>(settings),
            new SettingsFile(@"C:\Tests\appsettings.json"),
            NullLogger<SettingsService>.Instance);
    }
}
