using System;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SteamInputBridge.Settings;
using SteamInputBridge.Settings.Profiles;

namespace SteamInputBridge.Tests;

/// <summary>Tests minimal application settings validation.</summary>
[TestClass]
public sealed class SettingsValidationTests
{
    private static readonly string[] FragPunkReceiverProcess = ["FragPunk.exe"];

    /// <summary>Checks that clearly invalid settings fail when read.</summary>
    [TestMethod]
    public void InvalidSettingsFailWhenLoaded()
    {
        string directory = Path.Combine(Path.GetTempPath(), "SteamInputBridge.Tests", Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(directory);
        string settingsPath = Path.Combine(directory, "appsettings.json");

        try
        {
            File.WriteAllText(settingsPath, InvalidSettings());
            using ServiceProvider services = CreateServices(settingsPath);

            InvalidOperationException exception = Assert.ThrowsExactly<InvalidOperationException>(
                services.GetRequiredService<ApplicationSettingsService>);

            StringAssert.Contains(exception.Message, "viiper:port", StringComparison.Ordinal);
            StringAssert.Contains(exception.Message, "games:bad:receiverProcesses", StringComparison.Ordinal);
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
        string directory = Path.Combine(Path.GetTempPath(), "SteamInputBridge.Tests", Guid.NewGuid().ToString("N"));
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
        string directory = Path.Combine(Path.GetTempPath(), "SteamInputBridge.Tests", Guid.NewGuid().ToString("N"));
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

    /// <summary>Checks that disabled HidHide settings do not require the CLI path.</summary>
    [TestMethod]
    public void DisabledHidHideDoesNotRequireCliPath()
    {
        string directory = Path.Combine(Path.GetTempPath(), "SteamInputBridge.Tests", Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(directory);
        string settingsPath = Path.Combine(directory, "appsettings.json");

        try
        {
            File.WriteAllText(settingsPath, SettingsWithDisabledHidHide());
            using ServiceProvider services = CreateServices(settingsPath);

            ApplicationSettingsService settings = services.GetRequiredService<ApplicationSettingsService>();

            Assert.IsFalse(settings.Current.HidHide.Enabled);
            Assert.AreEqual("", settings.Current.HidHide.CliPath);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>Checks that enabled HidHide settings require the CLI path.</summary>
    [TestMethod]
    public void EnabledHidHideRequiresCliPath()
    {
        string directory = Path.Combine(Path.GetTempPath(), "SteamInputBridge.Tests", Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(directory);
        string settingsPath = Path.Combine(directory, "appsettings.json");

        try
        {
            File.WriteAllText(settingsPath, SettingsWithEnabledHidHideMissingCli());
            using ServiceProvider services = CreateServices(settingsPath);

            InvalidOperationException exception = Assert.ThrowsExactly<InvalidOperationException>(
                services.GetRequiredService<ApplicationSettingsService>);

            StringAssert.Contains(exception.Message, "hidhide:cliPath", StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>Checks that receiver-only profiles are valid.</summary>
    [TestMethod]
    public void AttachOnlyProfileIsAllowed()
    {
        string directory = Path.Combine(Path.GetTempPath(), "SteamInputBridge.Tests", Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(directory);
        string settingsPath = Path.Combine(directory, "appsettings.json");

        try
        {
            File.WriteAllText(settingsPath, SettingsWithAttachOnlyProfile());
            using ServiceProvider services = CreateServices(settingsPath);

            ApplicationSettingsService settings = services.GetRequiredService<ApplicationSettingsService>();
            GameProfile profile = settings.Current.Games["attach"];
            ResolvedGameProfile resolved = ProfileResolver.Resolve("attach", profile);

            Assert.IsNull(resolved.Executable);
            CollectionAssert.AreEqual(FragPunkReceiverProcess, resolved.ReceiverProcesses.ToArray());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>Checks that one key combination can apply multiple shortcut targets.</summary>
    [TestMethod]
    public void ShortcutKeysCanBeSharedAcrossTargets()
    {
        string directory = Path.Combine(Path.GetTempPath(), "SteamInputBridge.Tests", Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(directory);
        string settingsPath = Path.Combine(directory, "appsettings.json");

        try
        {
            File.WriteAllText(settingsPath, SettingsWithGroupedShortcuts());
            using ServiceProvider services = CreateServices(settingsPath);

            ApplicationSettingsService settings = services.GetRequiredService<ApplicationSettingsService>();

            Assert.HasCount(2, settings.Current.Shortcuts);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>Checks that repeated target actions on the same keys are rejected.</summary>
    [TestMethod]
    public void ShortcutKeysCannotRepeatTheSameTarget()
    {
        string directory = Path.Combine(Path.GetTempPath(), "SteamInputBridge.Tests", Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(directory);
        string settingsPath = Path.Combine(directory, "appsettings.json");

        try
        {
            File.WriteAllText(settingsPath, SettingsWithDuplicateShortcutTarget());
            using ServiceProvider services = CreateServices(settingsPath);

            InvalidOperationException exception = Assert.ThrowsExactly<InvalidOperationException>(
                services.GetRequiredService<ApplicationSettingsService>);

            StringAssert.Contains(exception.Message, "shortcuts:1:target", StringComparison.Ordinal);
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
          "SteamInputBridge": {
            "Viiper": {
              "Host": "localhost",
              "Port": 70000
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
          "SteamInputBridge": {
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
          "SteamInputBridge": {
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

    private static string SettingsWithDisabledHidHide()
    {
        return """
        {
          "SteamInputBridge": {
            "HidHide": {
              "Enabled": false,
              "CliPath": ""
            },
            "Games": {
              "game": {
                "Executable": "C:\\Games\\Game\\game.exe"
              }
            }
          }
        }
        """;
    }

    private static string SettingsWithEnabledHidHideMissingCli()
    {
        return """
        {
          "SteamInputBridge": {
            "HidHide": {
              "Enabled": true,
              "CliPath": ""
            },
            "Games": {
              "game": {
                "Executable": "C:\\Games\\Game\\game.exe"
              }
            }
          }
        }
        """;
    }

    private static string SettingsWithAttachOnlyProfile()
    {
        return """
        {
          "SteamInputBridge": {
            "Games": {
              "attach": {
                "ReceiverProcesses": [
                  "FragPunk.exe"
                ]
              }
            }
          }
        }
        """;
    }

    private static string SettingsWithGroupedShortcuts()
    {
        return """
        {
          "SteamInputBridge": {
            "Shortcuts": [
              {
                "Name": "motion-on",
                "Keys": "Ctrl+Alt+F15",
                "Target": "Motion",
                "Value": "Enabled"
              },
              {
                "Name": "pointer-on",
                "Keys": "Ctrl+Alt+F15",
                "Target": "Pointer",
                "Value": "Enabled"
              }
            ],
            "Games": {
              "game": {
                "Executable": "C:\\Games\\Game\\game.exe"
              }
            }
          }
        }
        """;
    }

    private static string SettingsWithDuplicateShortcutTarget()
    {
        return """
        {
          "SteamInputBridge": {
            "Shortcuts": [
              {
                "Name": "motion-on",
                "Keys": "Ctrl+Alt+F15",
                "Target": "Motion",
                "Value": "Enabled"
              },
              {
                "Name": "motion-on-again",
                "Keys": "Ctrl+Alt+F15",
                "Target": "Motion",
                "Value": "Enabled"
              }
            ],
            "Games": {
              "game": {
                "Executable": "C:\\Games\\Game\\game.exe"
              }
            }
          }
        }
        """;
    }
}
