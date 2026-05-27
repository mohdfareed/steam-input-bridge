using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SteamInputBridge.Forwarding.Controller.Routing;
using SteamInputBridge.Forwarding.Mouse;
using SteamInputBridge.Hosting.Server;
using SteamInputBridge.Hosting.Server.Orchestration;
using SteamInputBridge.Runtime.Audio;
using SteamInputBridge.Settings;
using SteamInputBridge.Shortcuts;

namespace SteamInputBridge.Tests;

/// <summary>Tests shortcut-driven overlay state.</summary>
[TestClass]
public sealed class ShortcutOverlayTests
{
    [TestMethod]
    public void HeldColorsUseLastPressedAndRestoreOnRelease()
    {
        using TestHost host = TestHost.Create(SettingsWithHeldColors());
        host.Service.Start();

        host.Shortcuts.Press(1);
        Assert.AreEqual("#111111", host.Service.GetOverlayStatus().ActionColor);

        host.Shortcuts.Press(2);
        Assert.AreEqual("#222222", host.Service.GetOverlayStatus().ActionColor);

        host.Shortcuts.Release(2);
        Assert.AreEqual("#111111", host.Service.GetOverlayStatus().ActionColor);

        host.Shortcuts.Release(1);
        Assert.IsNull(host.Service.GetOverlayStatus().ActionColor);
    }

    [TestMethod]
    public void MicToggleControlsSystemMicTarget()
    {
        using TestHost host = TestHost.Create(SettingsWithMicToggle());
        host.Service.Start();

        Assert.IsFalse(host.Microphone.Muted);
        host.Shortcuts.Press(1);
        Assert.IsTrue(host.Microphone.Muted);

        host.Shortcuts.Press(1);
        Assert.IsFalse(host.Microphone.Muted);
    }

    private static string SettingsWithHeldColors()
    {
        return """
        {
          "SteamInputBridge": {
            "Shortcuts": [
              {
                "Keys": "Num1",
                "Targets": [
                  "#111111"
                ],
                "Value": "HoldEnabled"
              },
              {
                "Keys": "Num2",
                "Targets": [
                  "#222222"
                ],
                "Value": "HoldEnabled"
              }
            ]
          }
        }
        """;
    }

    private static string SettingsWithMicToggle()
    {
        return """
        {
          "SteamInputBridge": {
            "Shortcuts": [
              {
                "Keys": "Num1",
                "Targets": [
                  "Mic"
                ],
                "Value": "Toggle"
              }
            ]
          }
        }
        """;
    }

    private sealed class TestHost : IDisposable
    {
        private readonly string _directory;
        private readonly ServiceProvider _services;

        private TestHost(
            string directory,
            ServiceProvider services,
            FakeKeyboardShortcutListener shortcuts,
            FakeMicrophoneControl microphone,
            ServerShortcutService service)
        {
            _directory = directory;
            _services = services;
            Shortcuts = shortcuts;
            Microphone = microphone;
            Service = service;
        }

        public FakeKeyboardShortcutListener Shortcuts { get; }

        public FakeMicrophoneControl Microphone { get; }

        public ServerShortcutService Service { get; }

        public static TestHost Create(string settingsJson)
        {
            string directory = Path.Combine(Path.GetTempPath(), "SteamInputBridge.Tests", Guid.NewGuid().ToString("N"));
            _ = Directory.CreateDirectory(directory);
            string settingsPath = Path.Combine(directory, "appsettings.json");
            File.WriteAllText(settingsPath, settingsJson);

            IConfigurationRoot configuration = new ConfigurationBuilder()
                .AddJsonFile(settingsPath, optional: false, reloadOnChange: false)
                .Build();
            FakeKeyboardShortcutListener shortcuts = new();
            FakeMicrophoneControl microphone = new();
            ServiceCollection services = new();
            _ = services.AddSingleton<ILogger<ApplicationSettingsService>>(
                NullLogger<ApplicationSettingsService>.Instance);
            _ = services.AddApplicationSettings(configuration, settingsPath);
            _ = services.AddSingleton<IKeyboardShortcutListener>(shortcuts);
            _ = services.AddSingleton<IMicrophoneControl>(microphone);
            _ = services.AddSingleton<ControllerBroker>(
                static _ => new ControllerBroker(new NoopControllerOutputFactory()));
            _ = services.AddSingleton<MouseBroker>(
                static _ => new MouseBroker(new NoopMouseOutputFactory()));
            _ = services.AddSingleton<ILogger<ServerShortcutService>>(
                NullLogger<ServerShortcutService>.Instance);
            _ = services.AddSingleton<ServerShortcutService>();

            ServiceProvider provider = services.BuildServiceProvider();
            return new TestHost(
                directory,
                provider,
                shortcuts,
                microphone,
                provider.GetRequiredService<ServerShortcutService>());
        }

        public void Dispose()
        {
            Service.Dispose();
            _services.Dispose();
            Directory.Delete(_directory, recursive: true);
        }
    }

    private sealed class FakeKeyboardShortcutListener : IKeyboardShortcutListener
    {
        private Action<int>? _pressed;
        private Action<int>? _released;

        public void Update(
            IReadOnlyList<KeyboardShortcutRegistration> shortcuts,
            Action<int> pressed,
            Action<int> released)
        {
            Assert.AreNotEqual(0, shortcuts.Count);
            _pressed = pressed;
            _released = released;
        }

        public void Press(int id)
        {
            _pressed?.Invoke(id);
        }

        public void Release(int id)
        {
            _released?.Invoke(id);
        }

        public void Dispose()
        {
        }
    }

    private sealed class FakeMicrophoneControl : IMicrophoneControl
    {
        public bool Muted { get; private set; }

        public MicrophoneOverlayStatus GetStatus()
        {
            return new MicrophoneOverlayStatus(
                Available: true,
                Muted,
                ActivityReliable: true,
                InputActive: false);
        }

        public void SetEnabled(bool enabled)
        {
            Muted = !enabled;
        }
    }
}
