using System.Collections.Generic;
using System.Linq;
using SteamInputBridge.Forwarding.Controller;
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
        SdlControllerInfo steam = Controller(SdlControllerSource.Steam, 0x28de, 0x1302, "same", "Steam Controller");
        SdlControllerInfo physical = Controller(SdlControllerSource.Physical, 0x28de, 0x1142, "same", "Steam Controller");

        SdlControllerInfo? match = SdlControllerRoutePolicy.FindPhysicalController(steam, [physical]);

        Assert.AreSame(physical, match);
    }

    /// <summary>Unique Steam Controller VID/PID match is used when paths do not match.</summary>
    [TestMethod]
    public void MatchesSteamControllerByUniqueVidPid()
    {
        SdlControllerInfo steam = Controller(SdlControllerSource.Steam, 0x28de, 0x1302, "steam", "Steam Controller");
        SdlControllerInfo physical = Controller(SdlControllerSource.Physical, 0x28de, 0x1302, "physical", "Steam Controller");

        SdlControllerInfo? match = SdlControllerRoutePolicy.FindPhysicalController(steam, [physical]);

        Assert.AreSame(physical, match);
    }

    /// <summary>Duplicate Steam Controller VID/PID matches are rejected as ambiguous.</summary>
    [TestMethod]
    public void RejectsAmbiguousVidPidMatch()
    {
        SdlControllerInfo steam = Controller(SdlControllerSource.Steam, 0x28de, 0x1302, "steam", "Steam Controller");

        SdlControllerInfo? match = SdlControllerRoutePolicy.FindPhysicalController(
            steam,
            [
                Controller(SdlControllerSource.Physical, 0x28de, 0x1302, "one", "Steam Controller"),
                Controller(SdlControllerSource.Physical, 0x28de, 0x1302, "two", "Steam Controller"),
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

    /// <summary>Host-side physical matching is limited to Steam Controllers.</summary>
    [TestMethod]
    public void MatchesPhysicalControllerByDeviceIdentity()
    {
        SdlControllerInfo physical = Controller(SdlControllerSource.Physical, 0x28de, 0x1302, "physical", "Steam Controller");

        SdlControllerInfo? match = SdlControllerRoutePolicy.FindPhysicalControllerByDeviceIdentity(
            0x28de,
            0x1302,
            "Steam Controller",
            [physical]);

        Assert.AreSame(physical, match);
    }

    /// <summary>Candidate narrowing accepts only Steam Controller pairs.</summary>
    [TestMethod]
    public void PhysicalCounterpartCandidateRequiresCompatibleIdentity()
    {
        SdlControllerInfo steamController = Controller(
            SdlControllerSource.Physical,
            0x28de,
            0x1142,
            "steam",
            "Steam Controller");

        Assert.IsFalse(SdlControllerRoutePolicy.CanBePhysicalCounterpart(
            0x054c,
            0x0ce6,
            "Generic Controller",
            steamController));
        Assert.IsTrue(SdlControllerRoutePolicy.CanBePhysicalCounterpart(
            0x28de,
            0x1302,
            "Steam Controller",
            steamController));
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
        SdlControllerInfo steam = Controller(SdlControllerSource.Steam, 0x28de, 0x1302, "same", "Steam Controller");
        SdlControllerInfo physical = Controller(SdlControllerSource.Physical, 0x28de, 0x1302, "same", "Steam Controller");
        SdlControllerInfo other = Controller(SdlControllerSource.Physical, 0x28de, 0x1142, "other", "Steam Controller 2");

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
            "Steam Controller",
            id: "steam:0001fa99604010e6",
            steamHandle: 0x0001fa99604010e6);

        List<SdlControllerInfo> forwardable =
            ClientControllerRoutePlanner.FilterForwardable([fallback, steam]);

        CollectionAssert.AreEqual(new[] { steam }, forwardable.ToArray());
    }

    /// <summary>Non-Steam controllers are not forwardable.</summary>
    [TestMethod]
    public void ForwardableFilterDropsNonSteamController()
    {
        SdlControllerInfo realDs4 = Controller(
            SdlControllerSource.Physical,
            0x054c,
            0x05c4,
            @"\\?\hid#vid_054c&pid_05c4",
            "Wireless Controller");

        List<SdlControllerInfo> forwardable =
            ClientControllerRoutePlanner.FilterForwardable([realDs4]);

        Assert.HasCount(0, forwardable);
    }

    /// <summary>App-owned DS4 virtual outputs are dropped when SDL exposes their name.</summary>
    [TestMethod]
    public void ForwardableFilterDropsOwnedDs4VirtualOutput()
    {
        SdlControllerInfo virtualDs4 = Controller(
            SdlControllerSource.Physical,
            0x054c,
            0x05c4,
            @"\\?\hid#vid_054c&pid_05c4#virtual",
            "Steam Input Bridge - Virtual Controller");

        List<SdlControllerInfo> forwardable =
            ClientControllerRoutePlanner.FilterForwardable([virtualDs4]);

        Assert.HasCount(0, forwardable);
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

    /// <summary>Duplicate SDL ids keep the route with touchpad feature data.</summary>
    [TestMethod]
    public void ClientSelectionPrefersTouchpadFeature()
    {
        SdlControllerInfo basic = Controller(
            SdlControllerSource.Steam,
            0x28de,
            0x1302,
            "basic",
            "Steam Controller",
            id: "steam:0001fa99604010e6",
            steamHandle: 0x0001fa99604010e6);
        SdlControllerInfo touchpad = Controller(
            SdlControllerSource.Steam,
            0x28de,
            0x1302,
            "touchpad",
            "Steam Controller",
            id: "steam:0001fa99604010e6",
            steamHandle: 0x0001fa99604010e6,
            hasTouchpad: true);

        IReadOnlyList<SdlControllerInfo> selected =
            ClientControllerRoutePlanner.SelectClientControllers([basic, touchpad]);

        CollectionAssert.AreEqual(new[] { touchpad }, selected.ToArray());
    }

    /// <summary>A unique Steam virtual XInput fallback is not forwardable.</summary>
    [TestMethod]
    public void ForwardableFilterDropsUniqueSteamVirtualXInputFallback()
    {
        SdlControllerInfo fallback = Controller(
            SdlControllerSource.Steam,
            0x28de,
            0x11ff,
            "XInput#0",
            "XInput Controller #1",
            id: "steam:0001fa99604010e6",
            steamHandle: 0x0001fa99604010e6);

        List<SdlControllerInfo> selected =
            ClientControllerRoutePlanner.FilterForwardable([fallback]);

        Assert.HasCount(0, selected);
    }

    /// <summary>Steam-routed XInput paths are not treated as physical route identity.</summary>
    [TestMethod]
    public void SteamRouteUsesSteamHandleBeforeXInputPath()
    {
        SdlControllerInfo steam = Controller(
            SdlControllerSource.Steam,
            0x28de,
            0x1302,
            "XInput#0",
            "Steam Controller",
            id: "steam:05de143a9a0d5235",
            steamHandle: 0x05de143a9a0d5235);

        SdlControllerRouteIdentity identity = SdlControllerRoutePolicy.CreateIdentity(0, steam, []);

        Assert.AreEqual("steam:05de143a9a0d5235", identity.RouteId);
        Assert.IsNull(identity.PhysicalDeviceId);
    }

    /// <summary>Only the exact real Steam Controller identity can own output without a physical match.</summary>
    [TestMethod]
    public void RealSteamControllerCanOwnOutputWithoutPhysical()
    {
        ClientControllerInfo controller = new(
            0,
            "steam:0001fa99604010e6",
            "Steam Controller",
            ControllerFeatures.StandardControls,
            PhysicalDeviceId: null,
            VendorId: 0x28de,
            ProductId: 0x1302);

        Assert.IsTrue(SdlControllerRoutePolicy.CanOwnOutputWithoutPhysical(controller));
    }

    /// <summary>Generic unresolved Steam DS4 streams cannot own output without a physical match.</summary>
    [TestMethod]
    public void SteamDs4CannotOwnOutputWithoutPhysical()
    {
        ClientControllerInfo controller = new(
            0,
            "steam:0654c5c41534ef2f",
            "PS4 Controller",
            ControllerFeatures.StandardControls,
            PhysicalDeviceId: null,
            VendorId: 0x054c,
            ProductId: 0x05c4);

        Assert.IsFalse(SdlControllerRoutePolicy.CanOwnOutputWithoutPhysical(controller));
    }

    /// <summary>Strict physical matches still own route and physical device identity.</summary>
    [TestMethod]
    public void SteamRouteUsesStrictPhysicalMatchWhenAvailable()
    {
        SdlControllerInfo steam = Controller(
            SdlControllerSource.Steam,
            0x28de,
            0x1302,
            "XInput#0",
            "Steam Controller",
            id: "steam:05de143a9a0d5235",
            steamHandle: 0x05de143a9a0d5235);
        SdlControllerInfo physical = Controller(
            SdlControllerSource.Physical,
            0x28de,
            0x1302,
            @"\\?\hid#vid_28de&pid_1302",
            "Steam Controller");

        SdlControllerRouteIdentity identity = SdlControllerRoutePolicy.CreateIdentity(0, steam, [physical]);

        Assert.AreEqual(@"path:\\?\hid#vid_28de&pid_1302", identity.RouteId);
        Assert.AreEqual(@"path:\\?\hid#vid_28de&pid_1302", identity.PhysicalDeviceId);
    }

    /// <summary>Steam id reuse with a different controller identity is treated as stale.</summary>
    [TestMethod]
    public void SameConnectedControllerRejectsSteamIdReuseWithDifferentIdentity()
    {
        SdlControllerInfo first = Controller(
            SdlControllerSource.Steam,
            0x28de,
            0x11ff,
            "XInput#0",
            "XInput Controller #1",
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
        ulong? steamHandle = null,
        bool hasTouchpad = false)
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
            HasAccelerometer: false,
            HasTouchpad: hasTouchpad);
    }
}
