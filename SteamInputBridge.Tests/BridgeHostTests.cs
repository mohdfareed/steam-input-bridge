using System;
using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SteamInputBridge.Hosting;
using SteamInputBridge.Settings;

namespace SteamInputBridge.Tests;

[TestClass]
public sealed class BridgeHostTests
{
    [TestMethod]
    public void CreateSettingsUsesDefaultContentRootForSettingsFile()
    {
        string previous = Environment.CurrentDirectory;
        string directory = Path.Combine(Path.GetTempPath(), $"SteamInputBridge.Tests.Config.{Guid.NewGuid():N}");
        _ = Directory.CreateDirectory(directory);
        try
        {
            Environment.CurrentDirectory = directory;

            using IHost host = BridgeHost.CreateSettings(static (logging, _, _) => logging.ClearProviders());
            SettingsFile settingsFile = host.Services.GetRequiredService<SettingsFile>();

            Assert.AreEqual(Path.Combine(Path.GetFullPath(directory), "appsettings.json"), settingsFile.Path);
        }
        finally
        {
            Environment.CurrentDirectory = previous;
            Directory.Delete(directory, true);
        }
    }
}
