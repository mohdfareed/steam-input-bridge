using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using SteamInputBridge.Hosting;
using SteamInputBridge.Outputs.Controller;
using SteamInputBridge.Outputs.Mouse;
using SteamInputBridge.Profiles;
using SteamInputBridge.Settings;

namespace SteamInputBridge.Tests;

[TestClass]
public sealed class ProfileServicesTests
{
    [TestMethod]
    public async Task ProfileCatalogServiceReloadsProfiles()
    {
        TestOptionsMonitor<SteamInputBridgeSettings> monitor = new(SettingsWithProfile("one"));
        using SettingsService settings = CreateSettings(monitor);
        using ProfileCatalogService catalog = new(settings);
        int changes = 0;
        catalog.ProfilesChanged += (_, _) => changes++;

        await catalog.StartAsync(default).ConfigureAwait(false);
        monitor.Set(SettingsWithProfile("two"));

        Assert.AreEqual(1, changes);
        Assert.IsTrue(catalog.ContainsProfile("two"));
        Assert.IsFalse(catalog.ContainsProfile("one"));
    }

    [TestMethod]
    public async Task ProfileClientsServiceConnectsOneClientPerProfileAndUpdatesStatus()
    {
        TestOptionsMonitor<SteamInputBridgeSettings> monitor = new(SettingsWithProfile("game"));
        using SettingsService settings = CreateSettings(monitor);
        using ProfileCatalogService catalog = new(settings);
        using ProfileClientsService clients = new(catalog, NullLogger<ProfileClientsService>.Instance);
        using ActiveProfileService activeProfiles = new(catalog, clients);
        FakeClientApi control = new();
        Guid connectionId = Guid.NewGuid();

        ProfileClientStatus connected = await clients
            .ConnectClientAsync(connectionId, processId: 1234, "game", steamAppId: 5678, control)
            .ConfigureAwait(false);

        ProfileStatus profile = AssertSingle(activeProfiles.Profiles);
        Assert.AreEqual(connectionId, connected.ConnectionId);
        Assert.AreEqual(1234, profile.ClientProcessId);
        Assert.AreEqual<uint?>(5678, profile.EffectiveSteamAppId);
        Assert.AreEqual(MouseOutput.Viiper, profile.Definition.MouseOutput);
        Assert.AreEqual(ControllerOutput.Xbox360, profile.Definition.ControllerOutput);
        Assert.HasCount(1, control.SetActiveCalls);
        Assert.IsFalse(control.SetActiveCalls[0]);

        _ = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
                async () => await clients
                    .ConnectClientAsync(Guid.NewGuid(), 4321, "game", null, new FakeClientApi())
                    .ConfigureAwait(false))
            .ConfigureAwait(false);

        _ = clients.DisconnectClient(connectionId);
        Assert.IsNull(AssertSingle(activeProfiles.Profiles).ClientProcessId);
    }

    private static ProfileStatus AssertSingle(IReadOnlyList<ProfileStatus> profiles)
    {
        Assert.HasCount(1, profiles);
        return profiles[0];
    }

    private static SettingsService CreateSettings(TestOptionsMonitor<SteamInputBridgeSettings> monitor)
    {
        return new SettingsService(
            monitor,
            new SettingsFile(@"C:\Tests\appsettings.json"),
            NullLogger<SettingsService>.Instance);
    }

    private static SteamInputBridgeSettings SettingsWithProfile(string profileId)
    {
        SteamInputBridgeSettings settings = new();
        settings.Games[profileId] = new GameProfile
        {
            Title = "Game",
            SteamAppId = 1111,
            MouseOutput = MouseOutput.Viiper,
            ControllerOutput = ControllerOutput.Xbox360,
        };
        settings.Games[profileId].ReceiverProcesses.Add("definitely-not-running-test-receiver.exe");
        return settings;
    }

    private sealed class FakeClientApi : IBridgeClientApi
    {
        public List<bool> SetActiveCalls { get; } = [];

        public int StopCalls { get; private set; }

        public Task StopAsync()
        {
            StopCalls++;
            return Task.CompletedTask;
        }

        public Task<BridgeClientRuntimeStatus> GetStatusAsync()
        {
            return Task.FromResult(new BridgeClientRuntimeStatus(new(false, 0, 0)));
        }

        public Task SetActiveAsync(bool active)
        {
            SetActiveCalls.Add(active);
            return Task.CompletedTask;
        }
    }
}
