using SteamInput;

namespace Cli.Tests;

/// <summary>Tests for Steam CLI helpers.</summary>
[TestClass]
public sealed class SteamCommandsTests
{
    /// <summary>Checks Steam game kind formatting.</summary>
    [TestMethod]
    public void DisplayKindFormatsShortcut()
    {
        Assert.AreEqual("shortcut", SteamCommands.DisplayKind(SteamGameKind.NonSteamShortcut));
    }
}
