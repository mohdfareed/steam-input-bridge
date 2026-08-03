using System;
using System.IO;
using SteamInputBridge.Diagnostics;
using SteamInputBridge.Hosting;
using SteamInputBridge.Settings;

namespace SteamInputBridge.Tests;

[TestClass]
public sealed class BridgeHostTests
{
    [TestMethod]
    public void ProductDataDirectoryUsesLocalApplicationDataAndProductMetadata()
    {
        string expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            ProductMetadata.Name);

        Assert.AreEqual(expected, ProductMetadata.ResolveDataDirectory());
    }

    [TestMethod]
    public void SettingsFileResolutionUsesPrecedenceAndDataFallback()
    {
        const string settingsFileName = "appsettings.json";
        string root = Path.Combine(Path.GetTempPath(), $"SteamInputBridge.Tests.Config.{Guid.NewGuid():N}");
        string currentDirectory = Directory.CreateDirectory(Path.Combine(root, "current")).FullName;
        string baseDirectory = Directory.CreateDirectory(Path.Combine(root, "binary")).FullName;
        string dataDirectory = Directory.CreateDirectory(Path.Combine(root, "data")).FullName;

        try
        {
            SettingsFile fallback = BridgeHost.ResolveSettingsFile(currentDirectory, baseDirectory, dataDirectory);
            Assert.AreEqual(Path.Combine(dataDirectory, settingsFileName), fallback.Path);
            Assert.IsFalse(File.Exists(fallback.Path));

            File.WriteAllText(Path.Combine(dataDirectory, settingsFileName), "{}");
            SettingsFile data = BridgeHost.ResolveSettingsFile(currentDirectory, baseDirectory, dataDirectory);
            Assert.AreEqual(Path.Combine(dataDirectory, settingsFileName), data.Path);

            File.WriteAllText(Path.Combine(baseDirectory, settingsFileName), "{}");
            SettingsFile binary = BridgeHost.ResolveSettingsFile(currentDirectory, baseDirectory, dataDirectory);
            Assert.AreEqual(Path.Combine(baseDirectory, settingsFileName), binary.Path);

            File.WriteAllText(Path.Combine(currentDirectory, settingsFileName), "{}");
            SettingsFile current = BridgeHost.ResolveSettingsFile(currentDirectory, baseDirectory, dataDirectory);
            Assert.AreEqual(Path.Combine(currentDirectory, settingsFileName), current.Path);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void ConfiguredPathsResolveFromSettingsDirectory()
    {
        string settingsDirectory = Path.Combine(Path.GetTempPath(), $"SteamInputBridge.Tests.Paths.{Guid.NewGuid():N}");
        SettingsFile settingsFile = new(Path.Combine(settingsDirectory, "appsettings.json"));
        string relativeDirectory = Path.Combine(settingsDirectory, "custom", "logs");
        string absoluteDirectory = Path.Combine(Path.GetTempPath(), $"SteamInputBridge.Tests.Logs.{Guid.NewGuid():N}");

        Assert.AreEqual(relativeDirectory, settingsFile.ResolvePath("./custom/logs"));
        Assert.AreEqual(Path.GetFullPath(absoluteDirectory), settingsFile.ResolvePath(absoluteDirectory));
    }

    [TestMethod]
    public void LogPathsDefaultBesideSettingsAndSupportOverrides()
    {
        string settingsDirectory = Path.Combine(Path.GetTempPath(), $"SteamInputBridge.Tests.Paths.{Guid.NewGuid():N}");
        SettingsFile settingsFile = new(Path.Combine(settingsDirectory, "appsettings.json"));
        string relativeDirectory = Path.Combine(settingsDirectory, "custom", "logs");
        string absoluteDirectory = Path.Combine(Path.GetTempPath(), $"SteamInputBridge.Tests.Logs.{Guid.NewGuid():N}");

        Assert.AreEqual(
            Path.Combine(settingsDirectory, "logs"),
            Path.GetDirectoryName(FileLoggerProvider.CreateLogPath(settingsFile, new LoggingSettings().LogDirectory)));
        Assert.AreEqual(
            relativeDirectory,
            Path.GetDirectoryName(FileLoggerProvider.CreateLogPath(settingsFile, logDirectory: "./custom/logs")));
        Assert.AreEqual(
            Path.GetFullPath(absoluteDirectory),
            Path.GetDirectoryName(FileLoggerProvider.CreateLogPath(settingsFile, logDirectory: absoluteDirectory)));
    }
}
