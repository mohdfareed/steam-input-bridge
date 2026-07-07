using SteamInputBridge.Inputs.Sdl;

namespace SteamInputBridge.Tests;

[TestClass]
public sealed class SdlSteamControllerCatalogTests
{
    [TestMethod]
    public void SupportsValveSteamControllerWithSteamHandle()
    {
        Assert.IsTrue(SdlSteamControllerCatalog.IsSupportedSteamInputController(
            steamHandle: 1,
            vendorId: 0x28DE,
            productId: 0x1302));
    }

    [TestMethod]
    public void SupportsEightBitDoUltimate2WirelessWithSteamHandle()
    {
        Assert.IsTrue(SdlSteamControllerCatalog.IsSupportedSteamInputController(
            steamHandle: 1,
            vendorId: 0x2DC8,
            productId: 0x6012));
    }

    [TestMethod]
    public void SupportsOtherSteamInputControllerWithSteamHandle()
    {
        Assert.IsTrue(SdlSteamControllerCatalog.IsSupportedSteamInputController(
            steamHandle: 1,
            vendorId: 0x2DC8,
            productId: 0x310B));
    }

    [TestMethod]
    public void RejectsControllerWithoutSteamHandle()
    {
        Assert.IsFalse(SdlSteamControllerCatalog.IsSupportedSteamInputController(
            steamHandle: 0,
            vendorId: 0x2DC8,
            productId: 0x6012));
    }

    [TestMethod]
    public void RejectsMirroredXbox360ControllerWithSteamHandle()
    {
        Assert.IsFalse(SdlSteamControllerCatalog.IsSupportedSteamInputController(
            steamHandle: 1,
            vendorId: 0x045E,
            productId: 0x028E));
    }
}
