using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Inputs.Sdl;
using Outputs.Viiper;

namespace Hosting.Tests;

#pragma warning disable CA1416
#pragma warning disable CA2000
#pragma warning disable CA2007

/// <summary>Tests for local forwarding host behavior.</summary>
[TestClass]
public sealed class ForwardingHostTests
{
    /// <summary>Checks disabled forwarding.</summary>
    [TestMethod]
    public async Task DisabledHostDropsInput()
    {
        MouseReport report = new(MouseButtons.None, 1, 0, 0);
        TestMouseInputSource input = new(new MouseInput(report, "device"));
        TestMouseOutput output = new();
        await using ForwardingHost host = new(new MouseForwardingRoute(input, output));

        host.Run();

        Assert.HasCount(0, output.Reports);
    }

    /// <summary>Checks enabled forwarding.</summary>
    [TestMethod]
    public async Task EnabledHostForwardsInput()
    {
        MouseReport report = new(MouseButtons.None, 1, 0, 0);
        TestMouseInputSource input = new(new MouseInput(report, "device"));
        TestMouseOutput output = new();
        await using ForwardingHost host = new(new MouseForwardingRoute(input, output));

        using (host.Enable())
        {
            host.Run();
        }

        Assert.HasCount(1, output.Reports);
        Assert.AreEqual(report, output.Reports[0]);
    }

    /// <summary>Checks that state changes affect later reports.</summary>
    [TestMethod]
    public async Task DisabledStateStopsLaterReports()
    {
        MouseReport report = new(MouseButtons.None, 1, 0, 0);
        TestMouseInputSource input = new(
            new MouseInput(report, "device"),
            new MouseInput(report, "device"));
        TestMouseOutput output = new();
        await using ForwardingHost host = new(new MouseForwardingRoute(input, output));
        IDisposable? lease = null;
        input.BeforeReport = index =>
        {
            if (index == 1)
            {
                lease?.Dispose();
            }
        };

        using (lease = host.Enable())
        {
            host.Run();
        }

        Assert.HasCount(1, output.Reports);
    }

    /// <summary>Checks control session status.</summary>
    [TestMethod]
    public async Task ControlSessionReportsHostState()
    {
        await using ForwardingHostRuntime runtime = CreateRuntime();
        using ForwardingHostControlSession session = new(runtime, requestStop: null, logger: null);

        ForwardingHostStatus status = await session.GetStatusAsync().ConfigureAwait(false);

        Assert.AreEqual("mouse", status.Mouse.RouteId);
        Assert.AreEqual(0, status.Mouse.EnabledClientCount);
        Assert.IsFalse(status.Mouse.IsConnected);
        Assert.HasCount(0, status.Gamepads);
        Assert.IsTrue(status.EmulationEnabled);
        Assert.IsTrue(status.PhysicalMotionEnabled);
    }

    /// <summary>Checks lease-counted enable state.</summary>
    [TestMethod]
    public async Task EnableLeasesKeepForwardingUntilLastLeaseDisposes()
    {
        await using ForwardingHost host = CreateHost();
        using IDisposable leaseA = host.Enable();
        using IDisposable leaseB = host.Enable();

        Assert.IsTrue(host.IsEnabled);
        Assert.AreEqual(2, host.EnabledLeaseCount);

        leaseA.Dispose();

        Assert.IsTrue(host.IsEnabled);
        Assert.AreEqual(1, host.EnabledLeaseCount);

        leaseB.Dispose();

        Assert.IsFalse(host.IsEnabled);
        Assert.AreEqual(0, host.EnabledLeaseCount);
    }

    /// <summary>Checks control session enable lease disposal.</summary>
    [TestMethod]
    public async Task ControlSessionEnableLeaseReleasesOnDispose()
    {
        await using ForwardingHostRuntime runtime = CreateRuntime();
        using ForwardingHostControlSession session = new(runtime, requestStop: null, logger: null);

        await session.EnableMouseAsync().ConfigureAwait(false);

        ForwardingHostStatus enabled = await session.GetStatusAsync().ConfigureAwait(false);
        Assert.AreEqual(1, enabled.Mouse.EnabledClientCount);
        Assert.IsTrue(enabled.Mouse.IsConnected);

        session.Dispose();

        ForwardingHostStatus disabled = await runtime.GetStatusAsync().ConfigureAwait(false);
        Assert.AreEqual(0, disabled.Mouse.EnabledClientCount);
        Assert.IsFalse(disabled.Mouse.IsConnected);
    }

    /// <summary>Checks control session route disabling without disconnecting.</summary>
    [TestMethod]
    public async Task ControlSessionDisableReleasesMouseWithoutDisconnecting()
    {
        await using ForwardingHostRuntime runtime = CreateRuntime();
        using ForwardingHostControlSession session = new(runtime, requestStop: null, logger: null);

        await session.EnableMouseAsync().ConfigureAwait(false);
        await session.DisableMouseAsync().ConfigureAwait(false);

        ForwardingHostStatus disabled = await session.GetStatusAsync().ConfigureAwait(false);
        Assert.AreEqual(0, disabled.Mouse.EnabledClientCount);
        Assert.IsFalse(disabled.Mouse.IsConnected);
    }

    /// <summary>Checks global host state can change without route lease ownership.</summary>
    [TestMethod]
    public async Task ControlSessionUpdatesGlobalState()
    {
        await using ForwardingHostRuntime runtime = CreateRuntime();
        using ForwardingHostControlSession session = new(runtime, requestStop: null, logger: null);

        await session.SetEmulationEnabledAsync(false).ConfigureAwait(false);
        await session.SetPhysicalMotionEnabledAsync(false).ConfigureAwait(false);

        ForwardingHostStatus disabled = await session.GetStatusAsync().ConfigureAwait(false);
        Assert.IsFalse(disabled.EmulationEnabled);
        Assert.IsFalse(disabled.PhysicalMotionEnabled);

        bool emulationToggled = await session.ToggleEmulationEnabledAsync().ConfigureAwait(false);
        bool physicalMotionToggled = await session.TogglePhysicalMotionEnabledAsync().ConfigureAwait(false);

        ForwardingHostStatus enabled = await session.GetStatusAsync().ConfigureAwait(false);
        Assert.IsTrue(emulationToggled);
        Assert.IsTrue(physicalMotionToggled);
        Assert.IsTrue(enabled.EmulationEnabled);
        Assert.IsTrue(enabled.PhysicalMotionEnabled);
    }

    /// <summary>Checks that gamepad host attachment rejects direct physical controllers.</summary>
    [TestMethod]
    public async Task AttachSteamControllerRejectsPhysicalController()
    {
        await using ForwardingHostRuntime runtime = CreateRuntime();
        SdlControllerInfo controller = CreateController(SdlControllerSource.Physical);

        _ = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
                () => runtime.AttachSteamControllerAsync(controller, CancellationToken.None))
            .ConfigureAwait(false);
    }

    /// <summary>Checks control session stop callback.</summary>
    [TestMethod]
    public async Task ControlSessionStopRequestsServerStop()
    {
        await using ForwardingHostRuntime runtime = CreateRuntime();
        bool stopped = false;
        using ForwardingHostControlSession session = new(runtime, () => stopped = true, logger: null);

        await session.StopAsync().ConfigureAwait(false);

        Assert.IsTrue(stopped);
    }

    /// <summary>Checks status through the local pipe control server.</summary>
    [TestMethod]
    public async Task ControlClientGetsStatusFromServer()
    {
        string pipeName = $"Hosting.Tests.{Guid.NewGuid():N}";
        await using ForwardingHostRuntime runtime = CreateRuntime();
        ForwardingHostServer server = new(runtime, pipeName);
        using CancellationTokenSource cancellation = new();
        Task serverTask = server.RunAsync(cancellation.Token);
        ForwardingClient client = new(pipeName, TimeSpan.FromSeconds(2));

        ForwardingHostStatus status = await client.GetStatusAsync().ConfigureAwait(false);

        Assert.AreEqual("mouse", status.Mouse.RouteId);
        await cancellation.CancelAsync().ConfigureAwait(false);
        try
        {
            await serverTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <summary>Checks mouse disabling through a connected local client session.</summary>
    [TestMethod]
    public async Task ControlClientSessionCanDisableAndReuseConnection()
    {
        string pipeName = $"Hosting.Tests.{Guid.NewGuid():N}";
        await using ForwardingHostRuntime runtime = CreateRuntime();
        ForwardingHostServer server = new(runtime, pipeName);
        using CancellationTokenSource cancellation = new();
        Task serverTask = server.RunAsync(cancellation.Token);
        ForwardingClient client = new(pipeName, TimeSpan.FromSeconds(2));

        await using ForwardingClientSession session = await client.ConnectAsync().ConfigureAwait(false);
        await session.EnableMouseAsync().ConfigureAwait(false);
        await session.DisableMouseAsync().ConfigureAwait(false);

        ForwardingHostStatus status = await client.GetStatusAsync().ConfigureAwait(false);
        Assert.AreEqual(0, status.Mouse.EnabledClientCount);
        Assert.IsFalse(status.Mouse.IsConnected);

        await cancellation.CancelAsync().ConfigureAwait(false);
        try
        {
            await serverTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <summary>Checks single-instance locking.</summary>
    [TestMethod]
    public void SingleInstanceRejectsSecondOwner()
    {
        string ownershipName = $@"Local\Hosting.Tests.{Guid.NewGuid():N}";
        using HostSingleInstance? first = HostSingleInstance.TryAcquire(ownershipName);

        Assert.IsNotNull(first);
        Task<bool> secondAcquireTask = Task.Run(() =>
        {
            using HostSingleInstance? second = HostSingleInstance.TryAcquire(ownershipName);
            return second is not null;
        });

        Assert.IsFalse(secondAcquireTask.GetAwaiter().GetResult());
    }

    /// <summary>Checks same-thread reentry rejection.</summary>
    [TestMethod]
    public void SingleInstanceRejectsSecondOwnerInSameThread()
    {
        string ownershipName = $@"Local\Hosting.Tests.{Guid.NewGuid():N}";
        using HostSingleInstance? first = HostSingleInstance.TryAcquire(ownershipName);
        using HostSingleInstance? second = HostSingleInstance.TryAcquire(ownershipName);

        Assert.IsNotNull(first);
        Assert.IsNull(second);
    }

    private static ForwardingHostRuntime CreateRuntime()
    {
        ForwardingHostState hostState = new();
        HostedRouteController mouse = new(
            ForwardingRouteIds.Mouse,
            _ => Task.FromResult<IForwardingRoute>(new MouseForwardingRoute(
                new TestMouseInputSource(),
                new TestMouseOutput())),
            logger: null,
            () => hostState.EmulationEnabled);
        GamepadControllerRegistry gamepads = new(new ViiperOptions(), hostState, logger: null);
        return new ForwardingHostRuntime(mouse, gamepads, hostState);
    }

    private static SdlControllerInfo CreateController(SdlControllerSource source)
    {
        SdlControllerInfo controller = new(
            new SdlControllerId(
                source == SdlControllerSource.Steam ? "steam:0000000000000001" : "path:controller-path"),
            InstanceId: 1,
            Name: "Controller",
            source,
            source == SdlControllerSource.Steam ? 1UL : 0UL,
            VendorId: 0x045e,
            ProductId: 0x028e,
            Path: "controller-path",
            HasGyro: true,
            HasAccelerometer: true);
        return controller;
    }

    private static ForwardingHost CreateHost()
    {
        return new ForwardingHost(new MouseForwardingRoute(
            new TestMouseInputSource(),
            new TestMouseOutput()));
    }

    private sealed class TestMouseInputSource(params MouseInput[] inputs) : IMouseInputSource
    {
        public bool IsConnected => true;

        public Action<int>? BeforeReport { get; set; }

        public void Run(MouseInputHandler handler, CancellationToken cancellationToken = default)
        {
            for (int i = 0; i < inputs.Length; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                BeforeReport?.Invoke(i);
                MouseInput input = inputs[i];
                handler(in input);
            }
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }

    private sealed class TestMouseOutput : IMouseOutput
    {
        public bool IsConnected => true;

        public List<MouseReport> Reports { get; } = [];

        public bool FilterInput(string? deviceName)
        {
            _ = deviceName;
            return true;
        }

        public ValueTask SendAsync(MouseReport report, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Reports.Add(report);
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }
}

#pragma warning restore CA2007
#pragma warning restore CA2000
#pragma warning restore CA1416
