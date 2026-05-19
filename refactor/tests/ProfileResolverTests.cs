using System;
using System.IO;
using System.Linq;
using VirtualMouse.Settings.Profiles;

namespace VirtualMouse.Tests;

/// <summary>Tests profile resolution defaults.</summary>
[TestClass]
public sealed class ProfileResolverTests
{
    private static readonly string[] GameReceiverProcess = ["game.exe"];

    /// <summary>Checks that raw profile settings resolve into runtime-ready values.</summary>
    [TestMethod]
    public void ResolveAppliesRuntimeDefaults()
    {
        string variableName = "VIRTUAL_MOUSE_TEST_GAME_ROOT";
        string root = Path.Combine(Path.GetTempPath(), "VirtualMouse.Refactor.Tests");
        Environment.SetEnvironmentVariable(variableName, root);

        try
        {
            GameProfile profile = new()
            {
                Executable = $"%{variableName}%\\Games\\DemoGame\\game.exe",
                Arguments = "--demo",
                SteamAppId = 123,
                ControllerOutput = ControllerOutput.Ds4,
                MouseOutput = MouseOutput.None,
            };

            ResolvedGameProfile resolved = ProfileResolver.Resolve("demo_game", profile);

            Assert.AreEqual("Demo Game", resolved.Title);
            Assert.AreEqual(Path.Combine(root, "Games", "DemoGame", "game.exe"), resolved.Executable);
            Assert.AreEqual(Path.Combine(root, "Games", "DemoGame"), resolved.WorkingDirectory);
            Assert.AreEqual("--demo", resolved.Arguments);
            Assert.AreEqual(123u, resolved.SteamAppId);
            CollectionAssert.AreEqual(GameReceiverProcess, resolved.ReceiverProcesses.ToArray());
            Assert.AreEqual(ControllerOutput.Ds4, resolved.ControllerOutput);
            Assert.AreEqual(MouseOutput.None, resolved.MouseOutput);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variableName, null);
        }
    }

    /// <summary>Checks that explicit values are trimmed and normalized.</summary>
    [TestMethod]
    public void ResolveNormalizesExplicitValues()
    {
        GameProfile profile = new()
        {
            Title = "  Custom Title  ",
            Executable = " C:\\Games\\Game\\launcher.exe ",
            WorkingDirectory = " C:\\Games\\Game ",
        };
        profile.ReceiverProcesses.Add(" C:\\Games\\Game\\game.exe ");

        ResolvedGameProfile resolved = ProfileResolver.Resolve("custom", profile);

        Assert.AreEqual("Custom Title", resolved.Title);
        Assert.AreEqual("C:\\Games\\Game\\launcher.exe", resolved.Executable);
        Assert.AreEqual("C:\\Games\\Game", resolved.WorkingDirectory);
        CollectionAssert.AreEqual(GameReceiverProcess, resolved.ReceiverProcesses.ToArray());
    }

    /// <summary>Checks that omitted outputs resolve to no output.</summary>
    [TestMethod]
    public void ResolveDefaultsOutputsToNone()
    {
        GameProfile profile = new()
        {
            Executable = "C:\\Games\\Game\\game.exe",
        };

        ResolvedGameProfile resolved = ProfileResolver.Resolve("default", profile);

        Assert.AreEqual(ControllerOutput.None, resolved.ControllerOutput);
        Assert.AreEqual(MouseOutput.None, resolved.MouseOutput);
    }

    /// <summary>Checks that JSON null outputs resolve to no output.</summary>
    [TestMethod]
    public void ResolveNullOutputsToNone()
    {
        GameProfile profile = new()
        {
            Executable = "C:\\Games\\Game\\game.exe",
            ControllerOutput = null,
            MouseOutput = null,
        };

        ResolvedGameProfile resolved = ProfileResolver.Resolve("default", profile);

        Assert.AreEqual(ControllerOutput.None, resolved.ControllerOutput);
        Assert.AreEqual(MouseOutput.None, resolved.MouseOutput);
    }
}
