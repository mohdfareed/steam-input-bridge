using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using SteamInputBridge.Profiles;
using SteamInputBridge.Steam;

namespace SteamInputBridge.Tests;

[TestClass]
public sealed class SteamInputConfigServiceTests
{
    [TestMethod]
    public async Task ApplySteamConfigForcesProfileAppIdAndClearsOnNull()
    {
        List<Uri> openedUrls = [];
        SteamInputConfigService service = CreateService(openedUrls);

        await service.ApplySteamConfigAsync(Profile("game", 123), default).ConfigureAwait(false);
        Assert.AreEqual("game", service.Status.ProfileId);
        Assert.AreEqual<uint?>(123, service.Status.AppId);

        await service.ApplySteamConfigAsync(null, default).ConfigureAwait(false);

        Assert.IsNull(service.Status.ProfileId);
        Assert.IsNull(service.Status.AppId);
        Assert.IsNull(service.Status.LastError);
        CollectionAssert.AreEqual(
            new[]
            {
                "steam://forceinputappid/123",
                "steam://forceinputappid/0",
            },
            openedUrls.ConvertAll(static uri => uri.AbsoluteUri));
    }

    [TestMethod]
    public async Task ApplySteamConfigUpdatesProfileWithoutRefiringSameAppId()
    {
        List<Uri> openedUrls = [];
        SteamInputConfigService service = CreateService(openedUrls);

        await service.ApplySteamConfigAsync(Profile("first", 123), default).ConfigureAwait(false);
        await service.ApplySteamConfigAsync(Profile("second", 123), default).ConfigureAwait(false);

        Assert.AreEqual("second", service.Status.ProfileId);
        Assert.AreEqual<uint?>(123, service.Status.AppId);
        Assert.HasCount(1, openedUrls);
    }

    private static SteamInputConfigService CreateService(List<Uri> openedUrls)
    {
        return new(
            profiles: null!,
            NullLogger<SteamInputConfigService>.Instance,
            new SteamInputClient((url, _) =>
            {
                openedUrls.Add(url);
                return ValueTask.CompletedTask;
            }));
    }

    private static ProfileStatus Profile(string id, uint appId)
    {
        return new(
            id,
            "Game",
            ConfiguredSteamAppId: appId,
            EffectiveSteamAppId: appId,
            MouseOutput: null,
            ControllerOutput: null,
            ReceiverProcesses: [],
            GameProcessIds: [],
            Active: true,
            ClientProcessId: 1,
            ClientConnectionId: Guid.NewGuid());
    }
}
