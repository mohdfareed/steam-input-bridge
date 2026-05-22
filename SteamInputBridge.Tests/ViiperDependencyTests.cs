using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using global::Viiper.Client;
using global::Viiper.Client.Types;
using SteamInputBridge.Forwarding.Controller;
using SteamInputBridge.Forwarding.Controller.Routing;
using SteamInputBridge.Forwarding.Mouse;
using SteamInputBridge.Outputs.Viiper;
using SteamInputBridge.Outputs.Viiper.Shared;

namespace SteamInputBridge.Tests;

/// <summary>Tests against a real VIIPER server.</summary>
[TestClass]
[TestCategory(TestCategories.Dependency)]
public sealed class ViiperDependencyTests
{
    private static readonly ViiperDeviceDefinition Xbox360Definition = new(
        "xbox360",
        ViiperXbox360Output.OwnedVendorId,
        ViiperXbox360Output.OwnedProductId,
        ViiperDeviceDefinition.FormatOwnedDisplayName("Dependency Test Controller"));

    private static readonly ViiperDeviceDefinition MouseDefinition = new(
        "mouse",
        ViiperMouseOutput.OwnedVendorId,
        ViiperMouseOutput.OwnedProductId,
        ViiperDeviceDefinition.FormatOwnedDisplayName("Dependency Test Mouse"));

    /// <summary>Creates, drives, disposes, and reclaims a real VIIPER Xbox 360 output.</summary>
    [TestMethod]
    public async Task Xbox360OutputConnectsSendsDisconnectsAndReclaims()
    {
        ViiperOptions options = RequireViiperOptions();
        await ViiperOutputFactory.ReclaimDevicesAsync(options, CancellationToken.None).ConfigureAwait(false);
        Assert.AreEqual(0, await CountOwnedDevicesAsync(options, Xbox360Definition).ConfigureAwait(false));

        ViiperXbox360Output output = await ViiperXbox360Output
            .ConnectAsync(options, new ControllerId("dependency:xbox360", "Dependency Test Controller"))
            .ConfigureAwait(false);

        try
        {
            Assert.IsTrue(output.IsConnected);
            Assert.AreEqual(1, await CountOwnedDevicesAsync(options, Xbox360Definition).ConfigureAwait(false));
            output.Send(new ControllerState(
                new ControllerStandardState(ControllerButtons.South, 1, 2, 3, 4, 5, 6),
                null,
                null));
        }
        finally
        {
            await output.DisposeAsync().ConfigureAwait(false);
        }

        Assert.IsFalse(output.IsConnected);
        await ViiperOutputFactory.ReclaimDevicesAsync(options, CancellationToken.None).ConfigureAwait(false);
        Assert.AreEqual(0, await CountOwnedDevicesAsync(options, Xbox360Definition).ConfigureAwait(false));
    }

    /// <summary>Creates, drives, disposes, and reclaims a real VIIPER mouse output.</summary>
    [TestMethod]
    public async Task MouseOutputConnectsSendsDisconnectsAndReclaims()
    {
        ViiperOptions options = RequireViiperOptions();
        await ViiperOutputFactory.ReclaimDevicesAsync(options, CancellationToken.None).ConfigureAwait(false);
        Assert.AreEqual(0, await CountOwnedDevicesAsync(options, MouseDefinition).ConfigureAwait(false));

        ViiperMouseOutput output = await ViiperMouseOutput.ConnectAsync(options).ConfigureAwait(false);

        try
        {
            Assert.IsTrue(output.IsConnected);
            Assert.AreEqual(1, await CountOwnedDevicesAsync(options, MouseDefinition).ConfigureAwait(false));
            await output.SendAsync(new MouseReport(MouseButtons.Left, 1, 1, 0), CancellationToken.None)
                .ConfigureAwait(false);
        }
        finally
        {
            await output.DisposeAsync().ConfigureAwait(false);
        }

        Assert.IsFalse(output.IsConnected);
        await ViiperOutputFactory.ReclaimDevicesAsync(options, CancellationToken.None).ConfigureAwait(false);
        Assert.AreEqual(0, await CountOwnedDevicesAsync(options, MouseDefinition).ConfigureAwait(false));
    }

    /// <summary>Creates multiple real VIIPER Xbox 360 outputs and disposes them independently.</summary>
    [TestMethod]
    public async Task MultipleXbox360OutputsStaySeparateThroughPartialDispose()
    {
        ViiperOptions options = RequireViiperOptions();
        await ViiperOutputFactory.ReclaimDevicesAsync(options, CancellationToken.None).ConfigureAwait(false);
        Assert.AreEqual(0, await CountOwnedDevicesAsync(options, Xbox360Definition).ConfigureAwait(false));

        ViiperXbox360Output? first = null;
        ViiperXbox360Output? second = null;
        try
        {
            first = await ViiperXbox360Output
                .ConnectAsync(options, new ControllerId("dependency:xbox360:first", "Dependency First Controller"))
                .ConfigureAwait(false);
            second = await ViiperXbox360Output
                .ConnectAsync(options, new ControllerId("dependency:xbox360:second", "Dependency Second Controller"))
                .ConfigureAwait(false);

            IReadOnlyList<Device> devices = await GetOwnedDevicesAsync(options, Xbox360Definition)
                .ConfigureAwait(false);
            Assert.HasCount(2, devices);
            Assert.AreEqual(2, devices.Select(CreateDeviceKey).Distinct(StringComparer.Ordinal).Count());
            first.Send(CreateControllerState(ControllerButtons.South));
            second.Send(CreateControllerState(ControllerButtons.East));

            await first.DisposeAsync().ConfigureAwait(false);
            Assert.IsFalse(first.IsConnected);
            Assert.IsTrue(second.IsConnected);
            Assert.AreEqual(1, await CountOwnedDevicesAsync(options, Xbox360Definition).ConfigureAwait(false));
            _ = Assert.ThrowsExactly<InvalidOperationException>(
                () => first.Send(CreateControllerState(ControllerButtons.West)));
            second.Send(CreateControllerState(ControllerButtons.North));
        }
        finally
        {
            if (first is not null)
            {
                await first.DisposeAsync().ConfigureAwait(false);
            }

            if (second is not null)
            {
                await second.DisposeAsync().ConfigureAwait(false);
            }
        }

        await ViiperOutputFactory.ReclaimDevicesAsync(options, CancellationToken.None).ConfigureAwait(false);
        Assert.AreEqual(0, await CountOwnedDevicesAsync(options, Xbox360Definition).ConfigureAwait(false));
    }

    /// <summary>Removes only this app's route-specific VIIPER identities.</summary>
    [TestMethod]
    public async Task ReclaimLeavesForeignDifferentIdentityDevicesAlone()
    {
        ViiperOptions options = RequireViiperOptions();
        await ViiperOutputFactory.ReclaimDevicesAsync(options, CancellationToken.None).ConfigureAwait(false);

        using ViiperClient client = CreateClient(options);
        uint foreignBusId = 0;
        string? foreignDeviceId = null;

        try
        {
            foreignBusId = (await client.BusCreateAsync(null, CancellationToken.None).ConfigureAwait(false)).BusID;
            Device foreignDevice = await client
                .BusDeviceAddAsync(
                    foreignBusId,
                    new DeviceCreateRequest
                    {
                        Type = "xbox360",
                        IdVendor = 0x1234,
                        IdProduct = 0x5678,
                        DeviceSpecific = new Dictionary<string, object?>
                        {
                            ["name"] = "Foreign Dependency Test Controller",
                        },
                    },
                    CancellationToken.None)
                .ConfigureAwait(false);
            foreignDeviceId = foreignDevice.DevId;

            ViiperXbox360Output output = await ViiperXbox360Output
                .ConnectAsync(options, new ControllerId("dependency:xbox360:owned", "Dependency Owned Controller"))
                .ConfigureAwait(false);
            await using (output.ConfigureAwait(false))
            {
                Assert.AreEqual(1, await CountOwnedDevicesAsync(options, Xbox360Definition).ConfigureAwait(false));
                Assert.IsTrue(
                    await DeviceExistsAsync(options, foreignBusId, foreignDeviceId).ConfigureAwait(false),
                    "Foreign different-identity device should exist before reclaim.");

                await ViiperOutputFactory.ReclaimDevicesAsync(options, CancellationToken.None).ConfigureAwait(false);

                Assert.AreEqual(0, await CountOwnedDevicesAsync(options, Xbox360Definition).ConfigureAwait(false));
                Assert.IsTrue(
                    await DeviceExistsAsync(options, foreignBusId, foreignDeviceId).ConfigureAwait(false),
                    "Reclaim removed a different-identity device not created by this app.");
            }
        }
        finally
        {
            if (foreignDeviceId is not null)
            {
                await RemoveDeviceIfPresentAsync(client, foreignBusId, foreignDeviceId).ConfigureAwait(false);
            }

            if (foreignBusId != 0)
            {
                await RemoveBusIfPresentAsync(client, foreignBusId).ConfigureAwait(false);
            }
        }
    }

    /// <summary>Creates and releases real VIIPER outputs through the controller route broker.</summary>
    [TestMethod]
    public async Task ControllerBrokerCreatesKeepsReconnectsAndReleasesViiperOutputs()
    {
        ViiperOptions options = RequireViiperOptions();
        await ViiperOutputFactory.ReclaimDevicesAsync(options, CancellationToken.None).ConfigureAwait(false);

        await using ControllerBroker broker = new(new ViiperOutputFactory(options));
        try
        {
            Guid clientId = Guid.NewGuid();
            broker.RegisterClient(clientId, ControllerOutput.Xbox360);
            broker.SetActiveClient(clientId);
            broker.UpdateClientController(
                clientId,
                controllerIndex: 0,
                new ControllerId("dependency:physical:first", "Dependency First Controller"),
                CreateControllerState(ControllerButtons.South),
                ControllerFeatures.StandardControls);
            broker.UpdateClientController(
                clientId,
                controllerIndex: 1,
                new ControllerId("dependency:physical:second", "Dependency Second Controller"),
                CreateControllerState(ControllerButtons.East),
                ControllerFeatures.StandardControls);

            Assert.AreEqual(2, await CountOwnedDevicesAsync(options, Xbox360Definition).ConfigureAwait(false));

            broker.SetActiveClient(null);
            Assert.AreEqual(2, await CountOwnedDevicesAsync(options, Xbox360Definition).ConfigureAwait(false));

            broker.SetControllerOutputEnabled(false);
            Assert.AreEqual(0, await CountOwnedDevicesAsync(options, Xbox360Definition).ConfigureAwait(false));

            broker.SetControllerOutputEnabled(true);
            Assert.AreEqual(2, await CountOwnedDevicesAsync(options, Xbox360Definition).ConfigureAwait(false));

            broker.RemoveClient(clientId);
            Assert.AreEqual(0, await CountOwnedDevicesAsync(options, Xbox360Definition).ConfigureAwait(false));
        }
        finally
        {
            await ViiperOutputFactory.ReclaimDevicesAsync(options, CancellationToken.None).ConfigureAwait(false);
        }
    }

    /// <summary>Creates one real VIIPER mouse output and keeps it connected through pointer gating.</summary>
    [TestMethod]
    public async Task MouseBrokerKeepsViiperOutputConnectedThroughPointerGating()
    {
        ViiperOptions options = RequireViiperOptions();
        await ViiperOutputFactory.ReclaimDevicesAsync(options, CancellationToken.None).ConfigureAwait(false);

        await using MouseBroker broker = new(new ViiperOutputFactory(options));
        try
        {
            Guid clientId = Guid.NewGuid();
            broker.RegisterClient(clientId, MouseOutput.Viiper);
            Assert.AreEqual(1, await CountOwnedDevicesAsync(options, MouseDefinition).ConfigureAwait(false));

            broker.SetActiveClient(clientId);
            broker.Send(new MouseInput(new MouseReport(MouseButtons.Left, 1, 2, 0), "dependency-mouse"));

            broker.SetPointerOutputEnabled(false);
            Assert.AreEqual(1, await CountOwnedDevicesAsync(options, MouseDefinition).ConfigureAwait(false));
            broker.Send(new MouseInput(new MouseReport(MouseButtons.Left, 3, 4, 0), "dependency-mouse"));

            broker.SetPointerOutputEnabled(true);
            Assert.AreEqual(1, await CountOwnedDevicesAsync(options, MouseDefinition).ConfigureAwait(false));
            broker.Send(new MouseInput(new MouseReport(MouseButtons.None, 5, 6, 0), "dependency-mouse"));

            broker.RemoveClient(clientId);
            Assert.AreEqual(0, await CountOwnedDevicesAsync(options, MouseDefinition).ConfigureAwait(false));
        }
        finally
        {
            await ViiperOutputFactory.ReclaimDevicesAsync(options, CancellationToken.None).ConfigureAwait(false);
        }
    }

    /// <summary>Repeatedly creates and removes real VIIPER outputs without leaving stale devices.</summary>
    [TestMethod]
    public async Task RepeatedLifecycleLeavesNoStaleViiperDevices()
    {
        ViiperOptions options = RequireViiperOptions();
        int iterations = TestEnvironment.GetInt("SIB_VIIPER_STRESS_ITERATIONS", 10);
        await ViiperOutputFactory.ReclaimDevicesAsync(options, CancellationToken.None).ConfigureAwait(false);

        for (int index = 0; index < iterations; index++)
        {
            ViiperXbox360Output controller = await ViiperXbox360Output
                .ConnectAsync(
                    options,
                    new ControllerId($"dependency:xbox360:stress:{index}", $"Dependency Stress Controller {index}"))
                .ConfigureAwait(false);
            ViiperMouseOutput mouse = await ViiperMouseOutput.ConnectAsync(options).ConfigureAwait(false);

            try
            {
                controller.Send(CreateControllerState(ControllerButtons.South | ControllerButtons.East));
                await mouse.SendAsync(new MouseReport(MouseButtons.Left, index + 1, index + 2, 0), CancellationToken.None)
                    .ConfigureAwait(false);
                Assert.AreEqual(1, await CountOwnedDevicesAsync(options, Xbox360Definition).ConfigureAwait(false));
                Assert.AreEqual(1, await CountOwnedDevicesAsync(options, MouseDefinition).ConfigureAwait(false));
            }
            finally
            {
                await controller.DisposeAsync().ConfigureAwait(false);
                await mouse.DisposeAsync().ConfigureAwait(false);
            }

            Assert.AreEqual(0, await CountOwnedDevicesAsync(options, Xbox360Definition).ConfigureAwait(false));
            Assert.AreEqual(0, await CountOwnedDevicesAsync(options, MouseDefinition).ConfigureAwait(false));
        }
    }

    private static ViiperOptions RequireViiperOptions()
    {
        if (!TestEnvironment.GetBool("SIB_TEST_VIIPER"))
        {
            Assert.Inconclusive("Set SIB_TEST_VIIPER=1 to run VIIPER dependency tests.");
        }

        return new ViiperOptions
        {
            Host = TestEnvironment.Get("SIB_VIIPER_HOST") ?? "127.0.0.1",
            Port = TestEnvironment.GetInt("SIB_VIIPER_PORT", 3242),
            Password = TestEnvironment.Get("SIB_VIIPER_PASSWORD") ?? string.Empty,
        };
    }

    private static async Task<int> CountOwnedDevicesAsync(
        ViiperOptions options,
        ViiperDeviceDefinition definition)
    {
        return (await GetOwnedDevicesAsync(options, definition).ConfigureAwait(false)).Count;
    }

    private static async Task<IReadOnlyList<Device>> GetOwnedDevicesAsync(
        ViiperOptions options,
        ViiperDeviceDefinition definition)
    {
        using ViiperClient client = new(options.Host, options.Port, options.Password);
        BusListResponse buses = await client.BusListAsync(CancellationToken.None).ConfigureAwait(false);
        List<Device> ownedDevices = [];

        foreach (uint busId in buses.Buses)
        {
            DevicesListResponse devices = await client
                .BusDevicesListAsync(busId, CancellationToken.None)
                .ConfigureAwait(false);
            foreach (Device device in devices.Devices)
            {
                if (definition.IsOwnedDevice(device))
                {
                    ownedDevices.Add(device);
                }
            }
        }

        return ownedDevices;
    }

    private static ControllerState CreateControllerState(ControllerButtons buttons)
    {
        return new ControllerState(
            new ControllerStandardState(buttons, 1, 2, 3, 4, 5, 6),
            null,
            null);
    }

    private static ViiperClient CreateClient(ViiperOptions options)
    {
        return new ViiperClient(options.Host, options.Port, options.Password);
    }

    private static string CreateDeviceKey(Device device)
    {
        return $"{device.BusID}:{device.DevId}";
    }

    private static async Task<bool> DeviceExistsAsync(ViiperOptions options, uint busId, string deviceId)
    {
        using ViiperClient client = CreateClient(options);
        BusListResponse buses = await client.BusListAsync(CancellationToken.None).ConfigureAwait(false);
        if (!buses.Buses.Contains(busId))
        {
            return false;
        }

        DevicesListResponse devices = await client.BusDevicesListAsync(busId, CancellationToken.None)
            .ConfigureAwait(false);
        return devices.Devices.Any(device => string.Equals(device.DevId, deviceId, StringComparison.Ordinal));
    }

    private static async Task RemoveDeviceIfPresentAsync(ViiperClient client, uint busId, string deviceId)
    {
        try
        {
            _ = await client.BusDeviceRemoveAsync(busId, deviceId, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (
            exception is System.IO.IOException or InvalidOperationException or ObjectDisposedException)
        {
        }
    }

    private static async Task RemoveBusIfPresentAsync(ViiperClient client, uint busId)
    {
        try
        {
            _ = await client.BusRemoveAsync(busId, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception) when (
            exception is System.IO.IOException or InvalidOperationException or ObjectDisposedException)
        {
        }
    }
}
