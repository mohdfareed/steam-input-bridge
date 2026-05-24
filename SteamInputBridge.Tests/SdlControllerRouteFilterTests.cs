using SteamInputBridge.Forwarding.Controller;
using SteamInputBridge.Hosting;
using SteamInputBridge.Hosting.Server.Orchestration.Lifetime;
using SteamInputBridge.Inputs.Sdl;

namespace SteamInputBridge.Tests;

/// <summary>Tests SDL controller filtering used by Hosting.</summary>
[TestClass]
public sealed class SdlControllerRouteFilterTests
{
    /// <summary>Xbox VID/PID alone is not enough to identify an owned VIIPER output.</summary>
    [TestMethod]
    public void AllowsXboxControllerWithoutOwnedMarker()
    {
        SdlControllerInfo controller = Controller(0x045e, 0x028e);

        Assert.IsTrue(SdlControllerRoutePolicy.IsForwardable(controller));
    }

    /// <summary>Owned VIIPER virtual controllers are filtered when SDL exposes the owned marker.</summary>
    [TestMethod]
    public void RejectsViiperControllerLoopbackWithOwnedMarker()
    {
        SdlControllerInfo controller = Controller(0x045e, 0x028e, "Steam Input Bridge - Virtual Controller");

        Assert.IsFalse(SdlControllerRoutePolicy.IsForwardable(controller));
    }

    /// <summary>Non-VIIPER controllers remain forwardable.</summary>
    [TestMethod]
    public void AllowsNonViiperController()
    {
        SdlControllerInfo controller = Controller(0x054c, 0x05c4);

        Assert.IsTrue(SdlControllerRoutePolicy.IsForwardable(controller));
    }

    /// <summary>Owned VIIPER outputs are filtered by exact observed SDL path.</summary>
    [TestMethod]
    public void RejectsTrackedOwnedOutput()
    {
        OwnedVirtualControllerRegistry registry = new();
        SdlControllerInfo existing = Controller(0x054c, 0x05c4, path: "real-ds4");
        SdlControllerInfo created = Controller(0x054c, 0x05c4, path: "owned-ds4");
        _ = registry.BeginTrackingOutput(ControllerOutput.Ds4, [existing]);
        registry.ObserveControllers([existing, created]);
        ServerControllerInputFilter filter = new(null, null, registry);

        Assert.IsTrue(filter.Allows(existing));
        Assert.IsFalse(filter.Allows(created));
    }

    /// <summary>Late VIIPER DS4 echoes are rejected by exact path after a pending output is created.</summary>
    [TestMethod]
    public void RejectsPendingOutputWhenClientReportsItBeforeServerOpensIt()
    {
        OwnedVirtualControllerRegistry registry = new();
        SdlControllerInfo existing = Controller(0x054c, 0x05c4, path: "real-ds4");
        _ = registry.BeginTrackingOutput(ControllerOutput.Ds4, [existing]);
        ServerControllerInputFilter filter = new(null, null, registry);

        ClientControllerInfo created = ClientController(
            @"path:\\?\hid#vid_054c&pid_05c4#owned",
            0x054c,
            0x05c4);

        Assert.IsFalse(filter.Allows(created));
        Assert.IsFalse(filter.Allows(Controller(0x054c, 0x05c4, path: @"\\?\hid#vid_054c&pid_05c4#owned")));
    }

    /// <summary>DS4 devices already present before VIIPER output creation are not claimed as owned.</summary>
    [TestMethod]
    public void AllowsDs4ThatExistedBeforeOutputCreation()
    {
        OwnedVirtualControllerRegistry registry = new();
        SdlControllerInfo existing = Controller(0x054c, 0x05c4, path: "real-ds4");
        _ = registry.BeginTrackingOutput(ControllerOutput.Ds4, [existing]);
        ServerControllerInputFilter filter = new(null, null, registry);

        Assert.IsTrue(filter.Allows(existing));
    }

    private static SdlControllerInfo Controller(
        ushort vendorId,
        ushort productId,
        string name = "Controller",
        string path = "controller")
    {
        return new SdlControllerInfo(
            new SdlControllerId($"path:{path}"),
            InstanceId: 1,
            name,
            SdlControllerSource.Physical,
            SteamHandle: 0,
            vendorId,
            productId,
            Path: path,
            HasGyro: false,
            HasAccelerometer: false);
    }

    private static ClientControllerInfo ClientController(
        string routeId,
        ushort vendorId,
        ushort productId)
    {
        return new ClientControllerInfo(
            0,
            routeId,
            "PS4 Controller",
            ControllerFeatures.StandardControls,
            routeId,
            vendorId,
            productId);
    }
}
