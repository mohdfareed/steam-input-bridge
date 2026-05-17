using Inputs.Sdl;

namespace Inputs.Tests;

/// <summary>Tests SDL controller identity and matching.</summary>
[TestClass]
public sealed class SdlControllerTests
{
    /// <summary>Steam controllers use the Steam handle as their stable id.</summary>
    [TestMethod]
    public void SteamControllerIdUsesSteamHandle()
    {
        SdlControllerInfo controller = Controller(
            SdlControllerSource.Steam,
            steamHandle: 0x1234,
            vendorId: 0x054c,
            productId: 0x0df2);

        SdlControllerId id = SdlControllerId.Create(controller);

        Assert.AreEqual("steam:0000000000001234", id.Value);
    }

    /// <summary>Physical controllers use the SDL path as their stable id.</summary>
    [TestMethod]
    public void PhysicalControllerIdUsesPath()
    {
        SdlControllerInfo controller = Controller(
            SdlControllerSource.Physical,
            path: "hid-path");

        SdlControllerId id = SdlControllerId.Create(controller);

        Assert.AreEqual("path:hid-path", id.Value);
    }

    /// <summary>Steam controllers match a unique physical controller by VID/PID.</summary>
    [TestMethod]
    public void MatcherUsesExactVidPid()
    {
        SdlControllerInfo steam = Controller(
            SdlControllerSource.Steam,
            vendorId: 0x054c,
            productId: 0x0df2);
        SdlControllerInfo physical = Controller(
            SdlControllerSource.Physical,
            vendorId: 0x054c,
            productId: 0x0df2,
            path: "dualsense");

        SdlControllerInfo? match = SdlControllerMatcher.FindPhysicalController(steam, [physical]);

        Assert.AreSame(physical, match);
    }

    /// <summary>Steam controllers prefer a strict path match before broader VID/PID matching.</summary>
    [TestMethod]
    public void MatcherPrefersExactPath()
    {
        SdlControllerInfo steam = Controller(
            SdlControllerSource.Steam,
            vendorId: 0x054c,
            productId: 0x0df2,
            path: "shared-path");
        SdlControllerInfo pathMatch = Controller(
            SdlControllerSource.Physical,
            vendorId: 0x054c,
            productId: 0x0df2,
            path: "shared-path");
        SdlControllerInfo other = Controller(
            SdlControllerSource.Physical,
            vendorId: 0x054c,
            productId: 0x0df2,
            path: "other-path");

        SdlControllerInfo? match = SdlControllerMatcher.FindPhysicalController(steam, [pathMatch, other]);

        Assert.AreSame(pathMatch, match);
    }

    /// <summary>Ambiguous VID/PID matches are rejected instead of guessing a rumble target.</summary>
    [TestMethod]
    public void MatcherRejectsAmbiguousExactVidPid()
    {
        SdlControllerInfo steam = Controller(
            SdlControllerSource.Steam,
            vendorId: 0x054c,
            productId: 0x0df2,
            path: "steam-path");

        SdlControllerInfo? match = SdlControllerMatcher.FindPhysicalController(
            steam,
            [
                Controller(SdlControllerSource.Physical, vendorId: 0x054c, productId: 0x0df2, path: "a"),
                Controller(SdlControllerSource.Physical, vendorId: 0x054c, productId: 0x0df2, path: "b"),
            ]);

        Assert.IsNull(match);
    }

    /// <summary>Valve controllers can match by unique Valve name when Steam changes the product id.</summary>
    [TestMethod]
    public void MatcherUsesUniqueValveNameFallback()
    {
        SdlControllerInfo steam = Controller(
            SdlControllerSource.Steam,
            name: "Steam Controller",
            vendorId: 0x28de,
            productId: 0x1302);
        SdlControllerInfo physical = Controller(
            SdlControllerSource.Physical,
            name: "Steam Controller",
            vendorId: 0x28de,
            productId: 0x1304,
            path: "steam-controller");

        SdlControllerInfo? match = SdlControllerMatcher.FindPhysicalController(steam, [physical]);

        Assert.AreSame(physical, match);
    }

    /// <summary>Ambiguous Valve fallback matches are rejected.</summary>
    [TestMethod]
    public void MatcherRejectsAmbiguousValveFallback()
    {
        SdlControllerInfo steam = Controller(
            SdlControllerSource.Steam,
            name: "Steam Controller",
            vendorId: 0x28de,
            productId: 0x1302);

        SdlControllerInfo? match = SdlControllerMatcher.FindPhysicalController(
            steam,
            [
                Controller(SdlControllerSource.Physical, name: "Steam Controller", vendorId: 0x28de, path: "a"),
                Controller(SdlControllerSource.Physical, name: "Steam Controller", vendorId: 0x28de, path: "b"),
            ]);

        Assert.IsNull(match);
    }

    /// <summary>Client auto-open only accepts Steam Input controllers.</summary>
    [TestMethod]
    public void CatalogOpenSteamControllersSkipsPhysicalControllers()
    {
        Assert.IsTrue(SdlControllerCatalog.ShouldOpenSteamController(Controller(SdlControllerSource.Steam)));
        Assert.IsFalse(SdlControllerCatalog.ShouldOpenSteamController(Controller(SdlControllerSource.Physical)));
    }

    private static SdlControllerInfo Controller(
        SdlControllerSource source,
        string name = "Controller",
        ulong steamHandle = 1,
        ushort vendorId = 0x045e,
        ushort productId = 0x028e,
        string? path = "path")
    {
        SdlControllerInfo controller = new(
            default,
            InstanceId: 1,
            name,
            source,
            source == SdlControllerSource.Steam ? steamHandle : 0,
            vendorId,
            productId,
            path,
            HasGyro: true,
            HasAccelerometer: true);

        return controller with { Id = SdlControllerId.Create(controller) };
    }
}
