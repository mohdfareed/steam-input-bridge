using System.Collections.Generic;
using System.Linq;
using SteamInputBridge.Hosting;
using SteamInputBridge.Hosting.Client.Run.Controllers;
using SteamInputBridge.Inputs.Sdl;

namespace SteamInputBridge.Tests;

/// <summary>Tests SDL controller matching.</summary>
[TestClass]
public sealed class SdlControllerRoutePolicyTests
{
    /// <summary>Exact path match wins for Steam-routed controllers.</summary>
    [TestMethod]
    public void MatchesSteamControllerByPath()
    {
        SdlControllerInfo steam = Controller(SdlControllerSource.Steam, 0x1234, 0x5678, "same");
        SdlControllerInfo physical = Controller(SdlControllerSource.Physical, 0x9999, 0x9999, "same");

        SdlControllerInfo? match = SdlControllerRoutePolicy.FindPhysicalController(steam, [physical]);

        Assert.AreSame(physical, match);
    }

    /// <summary>Unique VID/PID match is used when paths do not match.</summary>
    [TestMethod]
    public void MatchesSteamControllerByUniqueVidPid()
    {
        SdlControllerInfo steam = Controller(SdlControllerSource.Steam, 0x1234, 0x5678, "steam");
        SdlControllerInfo physical = Controller(SdlControllerSource.Physical, 0x1234, 0x5678, "physical");

        SdlControllerInfo? match = SdlControllerRoutePolicy.FindPhysicalController(steam, [physical]);

        Assert.AreSame(physical, match);
    }

    /// <summary>Duplicate VID/PID matches are rejected as ambiguous.</summary>
    [TestMethod]
    public void RejectsAmbiguousVidPidMatch()
    {
        SdlControllerInfo steam = Controller(SdlControllerSource.Steam, 0x1234, 0x5678, "steam");

        SdlControllerInfo? match = SdlControllerRoutePolicy.FindPhysicalController(
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

        SdlControllerInfo? match = SdlControllerRoutePolicy.FindPhysicalController(steam, [physical]);

        Assert.AreSame(physical, match);
    }

    /// <summary>Host-side physical matching uses the same unique VID/PID heuristic.</summary>
    [TestMethod]
    public void MatchesPhysicalControllerByDeviceIdentity()
    {
        SdlControllerInfo physical = Controller(SdlControllerSource.Physical, 0x054c, 0x0df2, "physical");

        SdlControllerInfo? match = SdlControllerRoutePolicy.FindPhysicalControllerByDeviceIdentity(
            0x054c,
            0x0df2,
            "DualSense Edge",
            [physical]);

        Assert.AreSame(physical, match);
    }

    /// <summary>Valve fallback rejects multiple physical Steam Controllers with the same name.</summary>
    [TestMethod]
    public void RejectsMultipleValveControllersByName()
    {
        SdlControllerInfo steam = Controller(SdlControllerSource.Steam, 0x28de, 0x11ff, "steam", "Steam Controller");

        SdlControllerInfo? match = SdlControllerRoutePolicy.FindPhysicalController(
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
            ClientControllerRoutePlanner.SelectClientControllers([steam, physical, other]);

        CollectionAssert.AreEqual(new[] { steam, other }, selected.ToArray());
    }

    /// <summary>Steam virtual XInput fallback devices are not forwardable routes.</summary>
    [TestMethod]
    public void ForwardableFilterDropsSteamVirtualXInputFallback()
    {
        SdlControllerInfo fallback = Controller(SdlControllerSource.Physical, 0x28de, 0x11ff, "XInput#2");
        SdlControllerInfo steam = Controller(
            SdlControllerSource.Steam,
            0x28de,
            0x1302,
            "XInput#0",
            id: "steam:0001fa99604010e6",
            steamHandle: 0x0001fa99604010e6);

        List<SdlControllerInfo> forwardable =
            ClientControllerRoutePlanner.FilterForwardable([fallback, steam]);

        CollectionAssert.AreEqual(new[] { steam }, forwardable.ToArray());
    }

    /// <summary>Duplicate Steam handles keep the non-XInput fallback route.</summary>
    [TestMethod]
    public void ClientSelectionDropsDuplicateSteamVirtualXInputFallback()
    {
        SdlControllerInfo fallback = Controller(
            SdlControllerSource.Steam,
            0x28de,
            0x11ff,
            "XInput#0",
            "XInput Controller #1",
            id: "steam:0001fa99604010e6",
            steamHandle: 0x0001fa99604010e6);
        SdlControllerInfo steam = Controller(
            SdlControllerSource.Steam,
            0x28de,
            0x1302,
            "XInput#2",
            "Steam Controller",
            id: "steam:0001fa99604010e6",
            steamHandle: 0x0001fa99604010e6);

        IReadOnlyList<SdlControllerInfo> selected =
            ClientControllerRoutePlanner.SelectClientControllers([fallback, steam]);

        CollectionAssert.AreEqual(new[] { steam }, selected.ToArray());
    }

    /// <summary>A unique Steam virtual XInput fallback is kept when it is the only route.</summary>
    [TestMethod]
    public void ClientSelectionKeepsUniqueSteamVirtualXInputFallback()
    {
        SdlControllerInfo fallback = Controller(
            SdlControllerSource.Steam,
            0x28de,
            0x11ff,
            "XInput#0",
            "XInput Controller #1",
            id: "steam:0001fa99604010e6",
            steamHandle: 0x0001fa99604010e6);

        IReadOnlyList<SdlControllerInfo> selected =
            ClientControllerRoutePlanner.SelectClientControllers([fallback]);

        CollectionAssert.AreEqual(new[] { fallback }, selected.ToArray());
    }

    /// <summary>Steam-routed XInput paths are not treated as physical route identity.</summary>
    [TestMethod]
    public void SteamRouteUsesSteamHandleBeforeXInputPath()
    {
        SdlControllerInfo steam = Controller(
            SdlControllerSource.Steam,
            0x054c,
            0x0df2,
            "XInput#0",
            id: "steam:05de143a9a0d5235",
            steamHandle: 0x05de143a9a0d5235);

        SdlControllerRouteIdentity identity = SdlControllerRoutePolicy.CreateIdentity(0, steam, []);

        Assert.AreEqual("steam:05de143a9a0d5235", identity.RouteId);
        Assert.IsNull(identity.PhysicalDeviceId);
    }

    /// <summary>Strict physical matches still own route and physical device identity.</summary>
    [TestMethod]
    public void SteamRouteUsesStrictPhysicalMatchWhenAvailable()
    {
        SdlControllerInfo steam = Controller(
            SdlControllerSource.Steam,
            0x054c,
            0x0df2,
            "XInput#0",
            id: "steam:05de143a9a0d5235",
            steamHandle: 0x05de143a9a0d5235);
        SdlControllerInfo physical = Controller(
            SdlControllerSource.Physical,
            0x054c,
            0x0df2,
            @"\\?\hid#vid_054c&pid_0df2");

        SdlControllerRouteIdentity identity = SdlControllerRoutePolicy.CreateIdentity(0, steam, [physical]);

        Assert.AreEqual(@"path:\\?\hid#vid_054c&pid_0df2", identity.RouteId);
        Assert.AreEqual(@"path:\\?\hid#vid_054c&pid_0df2", identity.PhysicalDeviceId);
    }

    /// <summary>Steam id reuse with a different controller identity is treated as stale.</summary>
    [TestMethod]
    public void SameConnectedControllerRejectsSteamIdReuseWithDifferentIdentity()
    {
        SdlControllerInfo first = Controller(
            SdlControllerSource.Steam,
            0x054c,
            0x0df2,
            "XInput#0",
            "DualSense Edge Wireless Controller",
            id: "steam:05de143a9a0d5235",
            steamHandle: 0x05de143a9a0d5235);
        SdlControllerInfo reused = Controller(
            SdlControllerSource.Steam,
            0x28de,
            0x1302,
            "XInput#0",
            "Steam Controller",
            id: "steam:05de143a9a0d5235",
            steamHandle: 0x05de143a9a0d5235);

        Assert.IsFalse(SdlControllerRoutePolicy.IsSameConnectedController(first, reused));
    }

    private static SdlControllerInfo Controller(
        SdlControllerSource source,
        ushort vendorId,
        ushort productId,
        string path,
        string name = "Controller",
        string? id = null,
        ulong? steamHandle = null)
    {
        return new SdlControllerInfo(
            new SdlControllerId(id ?? path),
            InstanceId: 1,
            name,
            source,
            steamHandle ?? (source == SdlControllerSource.Steam ? 1u : 0u),
            vendorId,
            productId,
            path,
            HasGyro: false,
            HasAccelerometer: false);
    }
}
