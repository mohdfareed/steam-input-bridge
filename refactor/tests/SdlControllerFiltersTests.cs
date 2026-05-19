using VirtualMouse.Hosting;
using VirtualMouse.Inputs.Sdl;

namespace VirtualMouse.Tests;

/// <summary>Tests SDL controller filtering used by Hosting.</summary>
[TestClass]
public sealed class SdlControllerFiltersTests
{
    /// <summary>Owned VIIPER virtual controllers are filtered from SDL forwarding.</summary>
    [TestMethod]
    public void RejectsViiperControllerLoopback()
    {
        SdlControllerInfo controller = Controller(0x045e, 0x028e);

        Assert.IsFalse(SdlControllerFilters.IsForwardable(controller));
    }

    /// <summary>Non-VIIPER controllers remain forwardable.</summary>
    [TestMethod]
    public void AllowsNonViiperController()
    {
        SdlControllerInfo controller = Controller(0x054c, 0x05c4);

        Assert.IsTrue(SdlControllerFilters.IsForwardable(controller));
    }

    private static SdlControllerInfo Controller(ushort vendorId, ushort productId)
    {
        return new SdlControllerInfo(
            new SdlControllerId("controller"),
            InstanceId: 1,
            "Controller",
            SdlControllerSource.Physical,
            SteamHandle: 0,
            vendorId,
            productId,
            Path: "controller",
            HasGyro: false,
            HasAccelerometer: false);
    }
}
