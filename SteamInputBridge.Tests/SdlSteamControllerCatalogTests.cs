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
    public void RejectsEightBitDoUltimate2WirelessWithoutSteamHandle()
    {
        Assert.IsFalse(SdlSteamControllerCatalog.IsSupportedSteamInputController(
            steamHandle: 0,
            vendorId: 0x2DC8,
            productId: 0x6012));
    }

    [TestMethod]
    public void RejectsUnknownSteamInputController()
    {
        Assert.IsFalse(SdlSteamControllerCatalog.IsSupportedSteamInputController(
            steamHandle: 1,
            vendorId: 0x2DC8,
            productId: 0x0001));
    }
}
