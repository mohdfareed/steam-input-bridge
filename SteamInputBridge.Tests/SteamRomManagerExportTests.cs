using System.Collections.Generic;
using System.Text.Json;
using SteamInputBridge.Settings;
using SteamInputBridge.Steam;

namespace SteamInputBridge.Tests;

[TestClass]
public sealed class SteamRomManagerExportTests
{
    [TestMethod]
    public void CreateJsonWritesShortcutEntriesForProfiles()
    {
        Dictionary<string, GameProfile> profiles = new()
        {
            ["simple"] = new() { Title = "Simple Game" },
            ["space profile"] = new() { Title = "Space Game" },
        };

        string json = SteamRomManagerExport.CreateJson(profiles, @"C:\Tools\SteamInputBridge.exe");

        using JsonDocument document = JsonDocument.Parse(json);
        Assert.AreEqual(2, document.RootElement.GetArrayLength());
        JsonElement first = document.RootElement[0];
        JsonElement second = document.RootElement[1];
        Assert.AreEqual("Simple Game", first.GetProperty("title").GetString());
        Assert.AreEqual(@"C:\Tools\SteamInputBridge.exe", first.GetProperty("target").GetString());
        Assert.AreEqual(@"C:\Tools", first.GetProperty("startIn").GetString());
        Assert.AreEqual("shortcut simple", first.GetProperty("launchOptions").GetString());
        Assert.IsFalse(first.GetProperty("appendArgsToExecutable").GetBoolean());
        Assert.AreEqual(@"shortcut ""space profile""", second.GetProperty("launchOptions").GetString());
    }
}
