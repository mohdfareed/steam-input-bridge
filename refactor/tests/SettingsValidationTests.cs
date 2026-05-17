using System;
using System.IO;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using VirtualMouse.Settings;
using VirtualMouse.Settings.Profiles;

namespace VirtualMouse.Tests;

/// <summary>Tests minimal application settings validation.</summary>
[TestClass]
public sealed class SettingsValidationTests
{
    /// <summary>Checks that clearly invalid settings fail when read.</summary>
    [TestMethod]
    public void InvalidSettingsFailWhenLoaded()
    {
        string directory = Path.Combine(Path.GetTempPath(), "VirtualMouse.Refactor.Tests", Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(directory);
        string settingsPath = Path.Combine(directory, "appsettings.json");

        try
        {
            File.WriteAllText(settingsPath, InvalidSettings());
            using ServiceProvider services = CreateServices(settingsPath);

            InvalidOperationException exception = Assert.ThrowsExactly<InvalidOperationException>(
                services.GetRequiredService<ApplicationSettingsService>);

            StringAssert.Contains(exception.Message, "hosting:pipeName", StringComparison.Ordinal);
            StringAssert.Contains(exception.Message, "general:viiperPort", StringComparison.Ordinal);
            StringAssert.Contains(exception.Message, "games:bad:executable", StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>Checks that title and receiver process defaults are allowed.</summary>
    [TestMethod]
    public void ProfileDefaultsAreAllowed()
    {
        string directory = Path.Combine(Path.GetTempPath(), "VirtualMouse.Refactor.Tests", Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(directory);
        string settingsPath = Path.Combine(directory, "appsettings.json");

        try
        {
            File.WriteAllText(settingsPath, SettingsWithProfileDefaults());
            using ServiceProvider services = CreateServices(settingsPath);

            ApplicationSettingsService settings = services.GetRequiredService<ApplicationSettingsService>();

            Assert.IsTrue(settings.Current.Games.ContainsKey("defaults"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>Checks that JSON null profile outputs mean no output.</summary>
    [TestMethod]
    public void NullProfileOutputsResolveToNone()
    {
        string directory = Path.Combine(Path.GetTempPath(), "VirtualMouse.Refactor.Tests", Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(directory);
        string settingsPath = Path.Combine(directory, "appsettings.json");

        try
        {
            File.WriteAllText(settingsPath, SettingsWithNullOutputs());
            using ServiceProvider services = CreateServices(settingsPath);

            ApplicationSettingsService settings = services.GetRequiredService<ApplicationSettingsService>();
            GameProfile profile = settings.Current.Games["nulls"];
            ResolvedGameProfile resolved = ProfileResolver.Resolve("nulls", profile);

            Assert.IsNull(profile.ControllerOutput);
            Assert.IsNull(profile.MouseOutput);
            Assert.AreEqual(ControllerOutput.None, resolved.ControllerOutput);
            Assert.AreEqual(MouseOutput.None, resolved.MouseOutput);
            Assert.AreEqual("C:\\Games\\Nulls", resolved.WorkingDirectory);
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
        _ = services.AddApplicationSettings(configuration, settingsPath);
        return services.BuildServiceProvider();
    }

    private static string InvalidSettings()
    {
        return """
        {
          "VirtualMouse": {
            "Hosting": {
              "PipeName": "",
              "ReconnectDelayMilliseconds": 0,
              "KeepAliveMilliseconds": 0
            },
            "General": {
              "ViiperHost": "localhost",
              "ViiperPort": 70000
            },
            "Games": {
              "bad": {
                "Title": "Bad"
              }
            }
          }
        }
        """;
    }

    private static string SettingsWithProfileDefaults()
    {
        return """
        {
          "VirtualMouse": {
            "Games": {
              "defaults": {
                "Executable": "C:\\Games\\Default\\game.exe"
              }
            }
          }
        }
        """;
    }

    private static string SettingsWithNullOutputs()
    {
        return """
        {
          "VirtualMouse": {
            "Games": {
              "nulls": {
                "Executable": "C:\\Games\\Nulls\\game.exe",
                "ControllerOutput": null,
                "MouseOutput": null
              }
            }
          }
        }
        """;
    }
}
