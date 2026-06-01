using System;
using Microsoft.Extensions.Logging.Abstractions;
using SteamInputBridge.Settings;

namespace SteamInputBridge.Tests;

[TestClass]
public sealed class SettingsServiceTests
{
    [TestMethod]
    public void SettingsServiceAcceptsValidReloadAndRejectsInvalidReload()
    {
        TestOptionsMonitor<SteamInputBridgeSettings> monitor = new(ValidSettings("first"));
        using SettingsService service = new(
            monitor,
            new SettingsFile(@"C:\Tests\appsettings.json"),
            NullLogger<SettingsService>.Instance);
        int changes = 0;
        service.Changed += (_, _) => changes++;

        monitor.Set(ValidSettings("second"));
        monitor.Set(InvalidSettings());

        Assert.AreEqual(1, changes);
        Assert.IsTrue(service.Current.Games.ContainsKey("second"));
        Assert.IsFalse(service.Current.Games.ContainsKey("invalid"));
    }

    private static SteamInputBridgeSettings ValidSettings(string profileId)
    {
        SteamInputBridgeSettings settings = new();
        settings.Games[profileId] = new GameProfile
        {
            Executable = @"C:\Games\Game\game.exe",
        };
        return settings;
    }

    private static SteamInputBridgeSettings InvalidSettings()
    {
        SteamInputBridgeSettings settings = new();
        settings.Games["invalid"] = new GameProfile();
        return settings;
    }
}
