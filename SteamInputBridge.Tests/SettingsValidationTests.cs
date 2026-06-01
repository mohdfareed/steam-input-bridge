using System;
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
        settings.Shortcuts.Add(new ShortcutEntry
        {
            Keys = "Ctrl+Alt",
            Action = (ShortcutValue)99,
        });
        settings.Games["bad"] = new GameProfile
        {
            ControllerOutput = (ControllerOutput)99,
            MouseOutput = (MouseOutput)99,
        };

        bool valid = SettingsValidation.TryValidate(settings, out string errors);

        Assert.IsFalse(valid);
        StringAssert.Contains(errors, "viiper:host is required.", StringComparison.Ordinal);
        StringAssert.Contains(errors, "viiper:port must be between 1 and 65535.", StringComparison.Ordinal);
        StringAssert.Contains(errors, "shortcuts:Ctrl+Alt:keys is invalid", StringComparison.Ordinal);
        StringAssert.Contains(errors, "shortcuts:Ctrl+Alt:targets is required.", StringComparison.Ordinal);
        StringAssert.Contains(errors, "shortcuts:Ctrl+Alt:action is invalid.", StringComparison.Ordinal);
        StringAssert.Contains(errors, "games:bad:receiverProcesses is required when executable is missing.", StringComparison.Ordinal);
        StringAssert.Contains(errors, "games:bad:controllerOutput is invalid.", StringComparison.Ordinal);
        StringAssert.Contains(errors, "games:bad:mouseOutput is invalid.", StringComparison.Ordinal);
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
            new ShortcutTargetSetting(ShortcutTarget.ActionColor, "#80A0FF"),
            ShortcutTargetSetting.Parse(" #80a0ff "));

        _ = Assert.ThrowsExactly<FormatException>(() => ShortcutTargetSetting.Parse("ActionColor"));
    }
}
