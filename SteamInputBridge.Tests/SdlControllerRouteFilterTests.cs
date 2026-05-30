using System.Collections.Generic;
using SteamInputBridge.Hosting;
using SteamInputBridge.Hosting.Client.Run.Controllers;
using SteamInputBridge.Inputs.Sdl;

namespace SteamInputBridge.Tests;

/// <summary>Tests SDL controller filtering used by Hosting.</summary>
[TestClass]
public sealed class SdlControllerRouteFilterTests
{
    /// <summary>Steam Controllers are forwardable.</summary>
    [TestMethod]
    public void AllowsSteamController()
    {
        SdlControllerInfo controller = SteamController();

        Assert.IsTrue(SdlControllerRoutePolicy.IsForwardable(controller));
    }

    /// <summary>Owned VIIPER virtual controllers are not forwardable.</summary>
    [TestMethod]
    public void RejectsViiperControllerLoopbackWithOwnedMarker()
    {
        SdlControllerInfo controller = Controller(0x28de, 0x1302, "Steam Input Bridge - Virtual Controller");

        Assert.IsFalse(SdlControllerRoutePolicy.IsForwardable(controller));
    }

    /// <summary>Non-Steam controllers are outside product scope.</summary>
    [TestMethod]
    public void RejectsNonSteamController()
    {
        SdlControllerInfo controller = Controller(0x054c, 0x05c4);

        Assert.IsFalse(SdlControllerRoutePolicy.IsForwardable(controller));
    }

    /// <summary>Partial Steam rescans are not treated as disconnects.</summary>
    [TestMethod]
    public void KeepsOpenSourceMissingFromPartialScan()
    {
        SdlControllerInfo opened = SteamController();
        Dictionary<SdlControllerId, SdlControllerInfo> current = new()
        {
            [new SdlControllerId("steam:other")] = Controller(0x28de, 0x1302, "Steam Controller", "other"),
        };

        Assert.IsFalse(ClientControllerSourceStaleness.ShouldRemoveOpenedSource(opened, current));
    }

    /// <summary>Reused SDL ids are stale when the controller identity changes.</summary>
    [TestMethod]
    public void RemovesOpenSourceWhenSameIdChangesIdentity()
    {
        SdlControllerInfo opened = SteamController();
        Dictionary<SdlControllerId, SdlControllerInfo> current = new()
        {
            [opened.Id] = opened with { Name = "Different Controller" },
        };

        Assert.IsTrue(ClientControllerSourceStaleness.ShouldRemoveOpenedSource(opened, current));
    }

    /// <summary>Stable matching identity is retained.</summary>
    [TestMethod]
    public void KeepsOpenSourceWhenSameIdStillSameController()
    {
        SdlControllerInfo opened = SteamController();
        Dictionary<SdlControllerId, SdlControllerInfo> current = new()
        {
            [opened.Id] = opened,
        };

        Assert.IsFalse(ClientControllerSourceStaleness.ShouldRemoveOpenedSource(opened, current));
    }

    private static SdlControllerInfo SteamController()
    {
        return Controller(0x28de, 0x1302, "Steam Controller", "steam-controller") with
        {
            Id = new SdlControllerId("steam:0001fa99604010e6"),
            Source = SdlControllerSource.Steam,
            SteamHandle = 0x0001fa99604010e6,
        };
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
}
