using System.Collections.Generic;
using System.Linq;
using VirtualMouse.Hosting;
using VirtualMouse.Inputs.Sdl;

namespace VirtualMouse.Tests;

/// <summary>Tests SDL controller matching.</summary>
[TestClass]
public sealed class SdlControllerMatcherTests
{
    /// <summary>Exact path match wins for Steam-routed controllers.</summary>
    [TestMethod]
    public void MatchesSteamControllerByPath()
    {
        SdlControllerInfo steam = Controller(SdlControllerSource.Steam, 0x1234, 0x5678, "same");
        SdlControllerInfo physical = Controller(SdlControllerSource.Physical, 0x9999, 0x9999, "same");

        SdlControllerInfo? match = SdlControllerMatcher.FindPhysicalController(steam, [physical]);

        Assert.AreSame(physical, match);
    }

    /// <summary>Unique VID/PID match is used when paths do not match.</summary>
    [TestMethod]
    public void MatchesSteamControllerByUniqueVidPid()
    {
        SdlControllerInfo steam = Controller(SdlControllerSource.Steam, 0x1234, 0x5678, "steam");
        SdlControllerInfo physical = Controller(SdlControllerSource.Physical, 0x1234, 0x5678, "physical");

        SdlControllerInfo? match = SdlControllerMatcher.FindPhysicalController(steam, [physical]);

        Assert.AreSame(physical, match);
    }

    /// <summary>Duplicate VID/PID matches are rejected as ambiguous.</summary>
    [TestMethod]
    public void RejectsAmbiguousVidPidMatch()
    {
        SdlControllerInfo steam = Controller(SdlControllerSource.Steam, 0x1234, 0x5678, "steam");

        SdlControllerInfo? match = SdlControllerMatcher.FindPhysicalController(
            steam,
            [
                Controller(SdlControllerSource.Physical, 0x1234, 0x5678, "one"),
                Controller(SdlControllerSource.Physical, 0x1234, 0x5678, "two"),
            ]);

        Assert.IsNull(match);
    }

    /// <summary>Valve fallback supports one physical Steam Controller with the same name.</summary>
    [TestMethod]
    public void MatchesSingleValveControllerByName()
    {
        SdlControllerInfo steam = Controller(SdlControllerSource.Steam, 0x28de, 0x11ff, "steam", "Steam Controller");
        SdlControllerInfo physical = Controller(SdlControllerSource.Physical, 0x28de, 0x1142, "physical", "Steam Controller");

        SdlControllerInfo? match = SdlControllerMatcher.FindPhysicalController(steam, [physical]);

        Assert.AreSame(physical, match);
    }

    /// <summary>Valve fallback rejects multiple physical Steam Controllers with the same name.</summary>
    [TestMethod]
    public void RejectsMultipleValveControllersByName()
    {
        SdlControllerInfo steam = Controller(SdlControllerSource.Steam, 0x28de, 0x11ff, "steam", "Steam Controller");

        SdlControllerInfo? match = SdlControllerMatcher.FindPhysicalController(
            steam,
            [
                Controller(SdlControllerSource.Physical, 0x28de, 0x1142, "one", "Steam Controller"),
                Controller(SdlControllerSource.Physical, 0x28de, 0x1142, "two", "Steam Controller"),
            ]);

        Assert.IsNull(match);
    }

    /// <summary>Steam-routed controllers suppress their matched physical duplicate.</summary>
    [TestMethod]
    public void ClientSelectionDropsMatchedPhysicalDuplicate()
    {
        SdlControllerInfo steam = Controller(SdlControllerSource.Steam, 0x1234, 0x5678, "same");
        SdlControllerInfo physical = Controller(SdlControllerSource.Physical, 0x1234, 0x5678, "same");
        SdlControllerInfo other = Controller(SdlControllerSource.Physical, 0xabcd, 0xef01, "other");

        IReadOnlyList<SdlControllerInfo> selected =
            ClientControllerStreams.SelectClientControllers([steam, physical, other]);

        CollectionAssert.AreEqual(new[] { steam, other }, selected.ToArray());
    }

    private static SdlControllerInfo Controller(
        SdlControllerSource source,
        ushort vendorId,
        ushort productId,
        string path,
        string name = "Controller")
    {
        return new SdlControllerInfo(
            new SdlControllerId(path),
            InstanceId: 1,
            name,
            source,
            source == SdlControllerSource.Steam ? 1u : 0u,
            vendorId,
            productId,
            path,
            HasGyro: false,
            HasAccelerometer: false);
    }
}
