using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using SteamInputBridge.Forwarding.Mouse;
using SteamInputBridge.Outputs.Controller;
using SteamInputBridge.Outputs.Mouse;
using SteamInputBridge.Settings;
using SteamInputBridge.Shortcuts;

namespace SteamInputBridge.Tests;

[TestClass]
public sealed class SettingsValidationTests
{
    [TestMethod]
    public void TryValidateReportsGroupedFailures()
    {
        SteamInputBridgeSettings settings = new();
        settings.Viiper.Host = "";
        settings.Viiper.Port = 70_000;
        settings.Teensy.Port = "USB1";
        settings.MouseSensitivity = 0;
        settings.Shortcuts.Add(new ShortcutEntry
        {
            Keys = "Ctrl+Alt",
        });
        settings.Shortcuts.Add(new ShortcutEntry
        {
            Keys = "F13",
            Action = (ShortcutValue)99,
        });
        settings.Games["bad"] = new GameProfile
        {
            ControllerOutput = (ControllerOutput)99,
            MouseOutput = (MouseOutput)99,
            MouseInput = (MouseInputMode)99,
            MouseSensitivity = double.PositiveInfinity,
        };
        settings.Games["bad"].Shortcuts.Add(new ShortcutEntry
        {
            Keys = "Ctrl+Alt",
        });
        settings.Games["bad"].Shortcuts.Add(new ShortcutEntry
        {
            Keys = "F14",
            Action = (ShortcutValue)99,
        });

        bool valid = SettingsValidation.TryValidate(settings, out string errors);

        Assert.IsFalse(valid);
        StringAssert.Contains(errors, "viiper:host is required.", StringComparison.Ordinal);
        StringAssert.Contains(errors, "viiper:port must be between 1 and 65535.", StringComparison.Ordinal);
        StringAssert.Contains(errors, "teensy:port must be a COM port name such as COM5.", StringComparison.Ordinal);
        StringAssert.Contains(errors, "mouseSensitivity must be a finite value greater than zero.", StringComparison.Ordinal);
        StringAssert.Contains(errors, "shortcuts:Ctrl+Alt:keys is invalid", StringComparison.Ordinal);
        StringAssert.Contains(errors, "shortcuts:Ctrl+Alt:target is required.", StringComparison.Ordinal);
        StringAssert.Contains(errors, "shortcuts:F13:target is required.", StringComparison.Ordinal);
        StringAssert.Contains(errors, "shortcuts:F13:action is invalid.", StringComparison.Ordinal);
        StringAssert.Contains(errors, "games:bad:receiverProcesses is required when executable is missing.", StringComparison.Ordinal);
        StringAssert.Contains(errors, "games:bad:controllerOutput is invalid.", StringComparison.Ordinal);
        StringAssert.Contains(errors, "games:bad:mouseOutput is invalid.", StringComparison.Ordinal);
        StringAssert.Contains(errors, "games:bad:mouseInput is invalid.", StringComparison.Ordinal);
        StringAssert.Contains(
            errors,
            "games:bad:mouseSensitivity must be a finite value greater than zero.",
            StringComparison.Ordinal);
        StringAssert.Contains(errors, "games:bad:shortcuts:Ctrl+Alt:keys is invalid", StringComparison.Ordinal);
        StringAssert.Contains(errors, "games:bad:shortcuts:Ctrl+Alt:target is required.", StringComparison.Ordinal);
        StringAssert.Contains(errors, "games:bad:shortcuts:F14:target is required.", StringComparison.Ordinal);
        StringAssert.Contains(errors, "games:bad:shortcuts:F14:action is invalid.", StringComparison.Ordinal);
    }

    [TestMethod]
    public void TryValidateAllowsExecutableProfileAndReceiverOnlyProfile()
    {
        SteamInputBridgeSettings settings = new();
        settings.Games["launched"] = new GameProfile
        {
            Executable = @"C:\Games\Game\game.exe",
        };
        settings.Games["attach"] = new GameProfile();
        settings.Games["attach"].ReceiverProcesses.Add("Game.exe");

        bool valid = SettingsValidation.TryValidate(settings, out string errors);

        Assert.IsTrue(valid, errors);
    }

    [TestMethod]
    public void ShortcutTargetSettingParsesNamedTargetsAndColors()
    {
        Assert.AreEqual(
            new ShortcutTargetSetting(ShortcutTarget.Microphone, null),
            ShortcutTargetSetting.Parse("microphone"));
        Assert.AreEqual(
            new ShortcutTargetSetting(ShortcutTarget.Steam, null),
            ShortcutTargetSetting.Parse("steam"));
        Assert.AreEqual(
            new ShortcutTargetSetting(ShortcutTarget.Tray, null),
            ShortcutTargetSetting.Parse("tray"));
        Assert.AreEqual(
            new ShortcutTargetSetting(ShortcutTarget.ActionColor, "#80A0FF"),
            ShortcutTargetSetting.Parse(" #80a0ff "));

        _ = Assert.ThrowsExactly<FormatException>(() => ShortcutTargetSetting.Parse("ActionColor"));
    }

    [TestMethod]
    public void ConfigurationBindsShortcutTargetAction()
    {
        Dictionary<string, string?> values = new()
        {
            ["SteamInputBridge:Shortcuts:0:Keys"] = "F13",
            ["SteamInputBridge:Shortcuts:0:Target"] = "Microphone",
            ["SteamInputBridge:Shortcuts:0:Action"] = "Toggle",
        };
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
        SteamInputBridgeSettings settings = new();

        configuration.GetSection(SteamInputBridgeSettings.SectionName).Bind(settings);

        Assert.HasCount(1, settings.Shortcuts);
        Assert.AreEqual(new ShortcutTargetSetting(ShortcutTarget.Microphone, null), settings.Shortcuts[0].Target);
        Assert.AreEqual(ShortcutValue.Toggle, settings.Shortcuts[0].Action);
    }

    [TestMethod]
    public void ConfigurationBindsSteamMouseInput()
    {
        Dictionary<string, string?> values = new()
        {
            ["SteamInputBridge:Games:test:ReceiverProcesses:0"] = "Game.exe",
            ["SteamInputBridge:Games:test:MouseInput"] = "Steam",
        };
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
        SteamInputBridgeSettings settings = new();

        configuration.GetSection(SteamInputBridgeSettings.SectionName).Bind(settings);

        Assert.AreEqual(MouseInputMode.Steam, settings.Games["test"].MouseInput);
    }

    [TestMethod]
    public void ConfigurationBindsAndResolvesMouseSensitivity()
    {
        Dictionary<string, string?> values = new()
        {
            ["SteamInputBridge:MouseSensitivity"] = "4200",
            ["SteamInputBridge:Games:default:ReceiverProcesses:0"] = "Default.exe",
            ["SteamInputBridge:Games:override:ReceiverProcesses:0"] = "Override.exe",
            ["SteamInputBridge:Games:override:MouseSensitivity"] = "5100",
        };
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
        SteamInputBridgeSettings settings = new();

        configuration.GetSection(SteamInputBridgeSettings.SectionName).Bind(settings);

        Assert.AreEqual(4200, settings.MouseSensitivity);
        Assert.IsNull(settings.Games["default"].MouseSensitivity);
        Assert.AreEqual(5100, settings.Games["override"].MouseSensitivity);
        Assert.AreEqual(4200, ClientSteamMouseForwardingService.ResolveMouseSensitivity(settings, "default"));
        Assert.AreEqual(5100, ClientSteamMouseForwardingService.ResolveMouseSensitivity(settings, "override"));
    }
}
