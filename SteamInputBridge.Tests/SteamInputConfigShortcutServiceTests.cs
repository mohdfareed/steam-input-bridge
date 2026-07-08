using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
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
        using SteamInputConfigShortcutService service = CreateService(shortcuts, () => 123456, opened);
        await service.StartAsync(default).ConfigureAwait(false);

        shortcuts.Raise(
            1,
            new ShortcutTargetSetting(ShortcutTarget.Steam, null),
            ShortcutValue.Toggle,
            ShortcutPhase.Pressed);

        Assert.HasCount(1, opened);
        Assert.AreEqual("steam://controllerconfig/123456", opened[0].AbsoluteUri);
    }

    [TestMethod]
    public async Task SteamShortcutFallsBackToDesktopConfig()
    {
        TestShortcutSource shortcuts = new();
        List<Uri> opened = [];
        using SteamInputConfigShortcutService service = CreateService(shortcuts, () => null, opened);
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
        using SteamInputConfigShortcutService service = CreateService(shortcuts, () => 123456, opened);
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
        Func<uint?> activeSteamAppId,
        List<Uri> opened)
    {
        SteamInputClient steam = new((uri, cancellationToken) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            opened.Add(uri);
            return ValueTask.CompletedTask;
        });

        return new(shortcuts, activeSteamAppId, NullLogger<SteamInputConfigShortcutService>.Instance, steam);
    }
}
