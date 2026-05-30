using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SteamInputBridge.Forwarding.Controller.Routing;
using SteamInputBridge.Forwarding.Mouse;
using SteamInputBridge.Hosting.Server;
using SteamInputBridge.Hosting.Server.Orchestration;
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
        host.Service.Start(CancellationToken.None);

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
    public void HeldColorsKeepLastPressedWhenMiddleShortcutReleases()
    {
        using TestHost host = TestHost.Create(SettingsWithThreeHeldColors());
        host.Service.Start(CancellationToken.None);

        host.Shortcuts.Press(1);
        host.Shortcuts.Press(2);
        host.Shortcuts.Press(3);
        Assert.AreEqual("#333333", host.Service.GetOverlayStatus().ActionColor);

        host.Shortcuts.Release(2);
        Assert.AreEqual("#333333", host.Service.GetOverlayStatus().ActionColor);

        host.Shortcuts.Release(3);
        Assert.AreEqual("#111111", host.Service.GetOverlayStatus().ActionColor);
    }

    [TestMethod]
    public void ShortcutStatusListsConfiguredShortcutsAndHeldState()
    {
        using TestHost host = TestHost.Create(SettingsWithHeldColors());
        host.Service.Start(CancellationToken.None);

        IReadOnlyList<ShortcutStatus> shortcuts = host.Service.GetShortcutStatus().Shortcuts;
        Assert.HasCount(2, shortcuts);
        Assert.AreEqual("Num1", shortcuts[0].Keys);
        Assert.IsFalse(shortcuts[0].Held);
        Assert.AreEqual("Num2", shortcuts[1].Keys);
        Assert.IsFalse(shortcuts[1].Held);

        host.Shortcuts.Press(2);

        shortcuts = host.Service.GetShortcutStatus().Shortcuts;
        Assert.HasCount(2, shortcuts);
        Assert.IsFalse(shortcuts[0].Held);
        Assert.IsTrue(shortcuts[1].Held);
    }

    [TestMethod]
    public void ShortcutStatusUsesPhysicalPressedStateForToggleShortcuts()
    {
        using TestHost host = TestHost.Create(SettingsWithMicToggle());
        host.Service.Start(CancellationToken.None);

        host.Shortcuts.Press(1);

        IReadOnlyList<ShortcutStatus> shortcuts = host.Service.GetShortcutStatus().Shortcuts;
        Assert.HasCount(1, shortcuts);
        Assert.IsTrue(shortcuts[0].Held);

        host.Shortcuts.Release(1);

        shortcuts = host.Service.GetShortcutStatus().Shortcuts;
        Assert.IsFalse(shortcuts[0].Held);
    }

    [TestMethod]
    public void MotionAndPointerShortcutsUpdateStatusWithoutActiveClient()
    {
        using TestHost host = TestHost.Create(SettingsWithMotionPointerHold());
        host.Service.Start(CancellationToken.None);

        Assert.IsTrue(host.Controllers.GetStatus().PhysicalMotionEnabled);
        Assert.IsTrue(host.Mouse.GetStatus().PointerOutputEnabled);

        host.Shortcuts.Press(1);
        Assert.IsFalse(host.Controllers.GetStatus().PhysicalMotionEnabled);
        Assert.IsFalse(host.Mouse.GetStatus().PointerOutputEnabled);

        host.Shortcuts.Release(1);
        Assert.IsTrue(host.Controllers.GetStatus().PhysicalMotionEnabled);
        Assert.IsTrue(host.Mouse.GetStatus().PointerOutputEnabled);
    }

    [TestMethod]
    public void MicToggleControlsSystemMicTarget()
    {
        using TestHost host = TestHost.Create(SettingsWithMicToggle());
        host.Service.Start(CancellationToken.None);

        Assert.IsFalse(host.Microphone.Muted);
        host.Shortcuts.Press(1);
        Assert.IsTrue(host.Microphone.Muted);

        host.Shortcuts.Press(1);
        Assert.IsFalse(host.Microphone.Muted);
    }

    [TestMethod]
    public void MicrophoneStatusChangeRaisesShortcutStateChanged()
    {
        using TestHost host = TestHost.Create(SettingsWithMicToggle());
        int changes = 0;
        host.Service.StateChanged += () => changes++;
        host.Service.Start(CancellationToken.None);

        host.Microphone.RaiseStatusChanged();

        Assert.AreEqual(1, changes);
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

    private static string SettingsWithThreeHeldColors()
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
              },
              {
                "Keys": "Num3",
                "Targets": [
                  "#333333"
                ],
                "Value": "HoldEnabled"
              }
            ]
          }
        }
        """;
    }

    private static string SettingsWithMotionPointerHold()
    {
        return """
        {
          "SteamInputBridge": {
            "Shortcuts": [
              {
                "Keys": "Num1",
                "Targets": [
                  "Motion",
                  "Pointer"
                ],
                "Value": "HoldDisabled"
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
            ControllerBroker controllers,
            MouseBroker mouse,
            ServerShortcutService service)
        {
            _directory = directory;
            _services = services;
            Shortcuts = shortcuts;
            Microphone = microphone;
            Controllers = controllers;
            Mouse = mouse;
            Service = service;
        }

        public FakeKeyboardShortcutListener Shortcuts { get; }

        public FakeMicrophoneControl Microphone { get; }

        public ControllerBroker Controllers { get; }

        public MouseBroker Mouse { get; }

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
                provider.GetRequiredService<ControllerBroker>(),
                provider.GetRequiredService<MouseBroker>(),
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
        public event Action? StatusChanged;

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

        public void StartMonitoring(CancellationToken cancellationToken)
        {
            _ = cancellationToken;
            _ = StatusChanged;
        }

        public void RaiseStatusChanged()
        {
            StatusChanged?.Invoke();
        }
    }
}
