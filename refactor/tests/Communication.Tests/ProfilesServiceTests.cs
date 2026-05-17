using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using VirtualMouse.Settings;
using VirtualMouse.Settings.Profiles;

namespace Communication.Tests;

/// <summary>Tests profile settings loading and reload behavior.</summary>
[TestClass]
public sealed class ProfilesServiceTests
{
    private static readonly string[] OneProfileId = ["one"];
    private static readonly string[] TwoProfileId = ["two"];

    /// <summary>Checks that the root settings service owns reload notifications.</summary>
    [TestMethod]
    public async Task ApplicationSettingsReloadWhenSettingsFileChanges()
    {
        string directory = Path.Combine(Path.GetTempPath(), "VirtualMouse.Refactor.Tests", Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(directory);
        string settingsPath = Path.Combine(directory, "appsettings.json");

        try
        {
            await File.WriteAllTextAsync(settingsPath, SettingsWithGame("one", "One")).ConfigureAwait(false);
            using ServiceProvider services = CreateServices(settingsPath);
            ApplicationSettingsService settings = services.GetRequiredService<ApplicationSettingsService>();

            Assert.AreEqual("One", settings.Current.Games["one"].Title);

            TaskCompletionSource<VirtualMouseSettings> changed = new(
                TaskCreationOptions.RunContinuationsAsynchronously);
            void OnChanged(object? sender, ApplicationSettingsChangedEventArgs args)
            {
                _ = changed.TrySetResult(args.Settings);
            }

            settings.Changed += OnChanged;
            try
            {
                await File.WriteAllTextAsync(settingsPath, SettingsWithGame("two", "Two")).ConfigureAwait(false);

                VirtualMouseSettings updated = await changed.Task
                    .WaitAsync(TimeSpan.FromSeconds(5))
                    .ConfigureAwait(false);
                Assert.AreEqual("Two", updated.Games["two"].Title);
            }
            finally
            {
                settings.Changed -= OnChanged;
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>Checks that profiles observe root settings reloads.</summary>
    [TestMethod]
    public async Task ProfilesObserveApplicationSettingsReload()
    {
        string directory = Path.Combine(Path.GetTempPath(), "VirtualMouse.Refactor.Tests", Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(directory);
        string settingsPath = Path.Combine(directory, "appsettings.json");

        try
        {
            await File.WriteAllTextAsync(settingsPath, SettingsWithGame("one", "One")).ConfigureAwait(false);
            using ServiceProvider services = CreateServices(settingsPath);
            ApplicationSettingsService settings = services.GetRequiredService<ApplicationSettingsService>();
            ProfilesService profiles = services.GetRequiredService<ProfilesService>();

            CollectionAssert.AreEqual(OneProfileId, profiles.ListProfileIds().ToArray());

            TaskCompletionSource<VirtualMouseSettings> changed = new(
                TaskCreationOptions.RunContinuationsAsynchronously);
            void OnChanged(object? sender, ApplicationSettingsChangedEventArgs args)
            {
                _ = changed.TrySetResult(args.Settings);
            }

            settings.Changed += OnChanged;
            try
            {
                await File.WriteAllTextAsync(settingsPath, SettingsWithGame("two", "Two")).ConfigureAwait(false);

                _ = await changed.Task
                    .WaitAsync(TimeSpan.FromSeconds(5))
                    .ConfigureAwait(false);
                CollectionAssert.AreEqual(TwoProfileId, profiles.ListProfileIds().ToArray());
                Assert.AreEqual("Two", profiles.GetProfile("two")?.Title);
                Assert.AreEqual(ControllerOutput.Ds4, profiles.GetProfile("two")?.ControllerOutput);
                Assert.AreEqual(MouseOutput.None, profiles.GetProfile("two")?.MouseOutput);
            }
            finally
            {
                settings.Changed -= OnChanged;
            }
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
        _ = services.AddSingleton<IConfiguration>(configuration);
        _ = services.AddSingleton<ILogger<ApplicationSettingsService>>(NullLogger<ApplicationSettingsService>.Instance);
        _ = services.AddSingleton<ILogger<ProfilesService>>(NullLogger<ProfilesService>.Instance);
        _ = services.AddApplicationSettings(configuration, settingsPath);
        _ = services.AddProfiles();
        return services.BuildServiceProvider();
    }

    private static string SettingsWithGame(string id, string title)
    {
        return $$"""
        {
          "VirtualMouse": {
            "Games": {
            "{{id}}": {
              "Title": "{{title}}",
              "Executable": "C:\\Games\\{{title}}.exe",
              "ControllerOutput": "Ds4",
              "MouseOutput": "None",
              "ReceiverProcesses": [ "{{title}}.exe" ]
            }
            }
          }
        }
        """;
    }
}
