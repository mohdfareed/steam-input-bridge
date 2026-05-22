using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SteamInputBridge.Forwarding.Controller;
using SteamInputBridge.Hosting;
using SteamInputBridge.Hosting.Client.Run.Controllers;
using SteamInputBridge.Inputs.Sdl;

namespace SteamInputBridge.Tests;

/// <summary>Manual tests for real SDL controller visibility and route identity.</summary>
[TestClass]
[TestCategory(TestCategories.Manual)]
public sealed class ManualSdlControllerTests
{
    /// <summary>Opens real SDL controllers and builds the same client route plan used by forwarding.</summary>
    [TestMethod]
    public async Task RealControllersOpenAndCreateStableRoutePlan()
    {
        int expectedControllers = TestEnvironment.GetInt("SIB_MANUAL_EXPECTED_CONTROLLERS", 0);
        if (expectedControllers <= 0)
        {
            Assert.Inconclusive("Set SIB_MANUAL_EXPECTED_CONTROLLERS to the controller count to verify.");
        }

        IReadOnlyList<SdlControllerInfo> visible = SdlControllerCatalog.GetControllers();
        List<SdlControllerInfo> forwardable = ClientControllerRoutePlanner.FilterForwardable(visible);
        IReadOnlyList<SdlControllerInfo> selected = ClientControllerRoutePlanner.SelectClientControllers(forwardable);
        WriteControllers("visible", visible);
        WriteControllers("selected", selected);

        Assert.IsGreaterThanOrEqualTo(
            selected.Count,
            expectedControllers,
            $"Expected at least {expectedControllers} selected controllers, got {selected.Count}.");

        List<SdlControllerInfo> openForwardable = [];
        IReadOnlyList<SdlGamepadSource> opened = SdlControllerCatalog.OpenControllers(controllers =>
        {
            openForwardable = ClientControllerRoutePlanner.FilterForwardable(controllers);
            return [.. ClientControllerRoutePlanner.SelectClientControllers(openForwardable).Take(expectedControllers)];
        });
        try
        {
            Assert.HasCount(expectedControllers, opened);
            ClientControllerRouteSource[] sources =
                [.. opened.Select((source, index) => new ClientControllerRouteSource((ushort)index, source))];
            ClientControllerRoutePlan plan = ClientControllerRoutePlanner.CreatePlan(
                sources,
                ClientControllerRoutePlanner.GetPhysicalControllers(openForwardable));

            Assert.HasCount(expectedControllers, plan.Controllers);
            Assert.AreEqual(
                expectedControllers,
                plan.Controllers.Select(static controller => controller.PhysicalControllerId).Distinct().Count());
            foreach (ClientControllerInfo controller in plan.Controllers)
            {
                Assert.AreNotEqual(ControllerFeatures.None, controller.Features);
                TestContext.WriteLine(
                    $"route idx={controller.ControllerIndex} id={controller.PhysicalControllerId} physical={controller.PhysicalDeviceId ?? "none"} label={controller.Label} features={controller.Features}");
            }
        }
        finally
        {
            foreach (SdlGamepadSource source in opened)
            {
                await source.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    public TestContext TestContext { get; set; } = null!;

    private void WriteControllers(string label, IReadOnlyList<SdlControllerInfo> controllers)
    {
        TestContext.WriteLine($"{label}: {controllers.Count}");
        foreach (SdlControllerInfo controller in controllers)
        {
            TestContext.WriteLine(
                $"{label}: name=\"{controller.Name}\" id=\"{controller.Id.Value}\" source={controller.Source} steam={controller.SteamHandle:x16} vid={controller.VendorId:x4} pid={controller.ProductId:x4} path=\"{controller.Path}\" motion={controller.HasMotion}");
        }
    }
}
