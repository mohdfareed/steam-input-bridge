using System;
using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using VirtualMouse.Settings;
using VirtualMouse.Settings.Profiles;
using VirtualMouse.Steam;

namespace VirtualMouse.Tests;

/// <summary>Tests Steam ROM Manager manifest export.</summary>
[TestClass]
public sealed class SteamRomManagerExportTests
{
    /// <summary>Checks SRM entries launch the shortcut mode.</summary>
    [TestMethod]
    public void CreateJsonUsesClientRunLaunchOptions()
    {
        string directory = Path.Combine(Path.GetTempPath(), "VirtualMouse.Refactor.Tests", Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(directory);
        string settingsPath = Path.Combine(directory, "appsettings.json");

        try
        {
            File.WriteAllText(settingsPath, SettingsJson());
            using ServiceProvider services = CreateServices(settingsPath);
            ProfilesService profiles = services.GetRequiredService<ProfilesService>();

            string json = SteamRomManagerExport.CreateJson(
                profiles,
                @"C:\Tools\vm\VirtualMouse.exe");

            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement entry = document.RootElement[0];
            Assert.AreEqual("Frag Punk", entry.GetProperty("title").GetString());
            Assert.AreEqual(
                @"C:\Tools\vm\VirtualMouse.exe",
                entry.GetProperty("target").GetString());
            Assert.AreEqual(@"shortcut ""frag punk""", entry.GetProperty("launchOptions").GetString());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static ServiceProvider CreateServices(string settingsPath)
    {
        IConfigurationRoot configuration = new ConfigurationBuilder()
            .AddJsonFile(settingsPath, optional: false, reloadOnChange: true)
            .Build();

        ServiceCollection services = new();
        _ = services.AddSingleton<ILogger<ApplicationSettingsService>>(NullLogger<ApplicationSettingsService>.Instance);
        _ = services.AddSingleton<ILogger<ProfilesService>>(NullLogger<ProfilesService>.Instance);
        _ = services.AddApplicationSettings(configuration, settingsPath);
        _ = services.AddProfiles();
        return services.BuildServiceProvider();
    }

    private static string SettingsJson()
    {
        return """
        {
          "VirtualMouse": {
            "Games": {
              "frag punk": {
                "Title": "Frag Punk",
                "Executable": "C:\\Games\\FragPunk\\FragPunk.exe"
              }
            }
          }
        }
        """;
    }
}
