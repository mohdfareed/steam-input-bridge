using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

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

    /// <summary>Checks disabled xpad forwarding.</summary>
    [TestMethod]
    public async Task DisabledXpadHostDropsInput()
    {
        GamepadState state = new(GamepadButtons.South, 0, 0, 0, 0, 0, 0, default);
        TestGamepadInputSource input = new(state);
        TestXbox360Output output = new();
        await using ForwardingHost host = new(new Xbox360ForwardingRoute(input, output));

        host.Run();

        Assert.HasCount(0, output.Reports);
    }

    /// <summary>Checks enabled xpad forwarding.</summary>
    [TestMethod]
    public async Task EnabledXpadHostForwardsInput()
    {
        GamepadState state = new(GamepadButtons.South, 0, 0, 0, 0, 0, 0, default);
        TestGamepadInputSource input = new(state);
        TestXbox360Output output = new();
        await using ForwardingHost host = new(new Xbox360ForwardingRoute(input, output));

        using (host.Enable())
        {
            host.Run();
        }

        Assert.HasCount(1, output.Reports);
        Assert.AreEqual(Xbox360Buttons.A, output.Reports[0].Buttons);
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
        await using ForwardingHost host = CreateHost();
        using ForwardingHostControlSession session = new(host, requestStop: null, logger: null);

        Assert.IsFalse(host.IsEnabled);
        ForwardingStatus status = await session.GetStatusAsync().ConfigureAwait(false);

        Assert.AreEqual("mouse", status.RouteId);
        Assert.IsFalse(status.IsEnabled);
        Assert.IsTrue(status.IsConnected);
        Assert.AreEqual(0, status.EnabledClientCount);
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
        await using ForwardingHost host = CreateHost();
        using ForwardingHostControlSession session = new(host, requestStop: null, logger: null);

        await session.EnableAsync().ConfigureAwait(false);

        Assert.IsTrue(host.IsEnabled);
        Assert.AreEqual(1, host.EnabledLeaseCount);

        session.Dispose();

        Assert.IsFalse(host.IsEnabled);
        Assert.AreEqual(0, host.EnabledLeaseCount);
    }

    /// <summary>Checks control session stop callback.</summary>
    [TestMethod]
    public async Task ControlSessionStopRequestsServerStop()
    {
        await using ForwardingHost host = CreateHost();
        bool stopped = false;
        using ForwardingHostControlSession session = new(host, () => stopped = true, logger: null);

        await session.StopAsync().ConfigureAwait(false);

        Assert.IsTrue(stopped);
    }

    /// <summary>Checks route-specific ownership and pipe names.</summary>
    [TestMethod]
    public void RouteSpecificRuntimeNamesAreDistinct()
    {
        Assert.AreEqual("mouse", ForwardingServer.GetRouteId(ForwardingRouteKind.Mouse));
        Assert.AreEqual("xpad", ForwardingServer.GetRouteId(ForwardingRouteKind.Xpad));
        Assert.AreNotEqual(
            ForwardingServer.GetPipeName(ForwardingRouteKind.Mouse),
            ForwardingServer.GetPipeName(ForwardingRouteKind.Xpad));
        Assert.AreNotEqual(
            ForwardingServer.GetOwnershipName(ForwardingRouteKind.Mouse),
            ForwardingServer.GetOwnershipName(ForwardingRouteKind.Xpad));
        StringAssert.EndsWith(
            ForwardingServer.GetPipeName(ForwardingRouteKind.Xpad),
            ForwardingServer.GetRouteId(ForwardingRouteKind.Xpad),
            StringComparison.Ordinal);
        StringAssert.EndsWith(
            ForwardingServer.GetOwnershipName(ForwardingRouteKind.Xpad),
            ForwardingServer.GetRouteId(ForwardingRouteKind.Xpad),
            StringComparison.Ordinal);
    }

    /// <summary>Checks status through the local pipe control server.</summary>
    [TestMethod]
    public async Task ControlClientGetsStatusFromServer()
    {
        string pipeName = $"Hosting.Tests.{Guid.NewGuid():N}";
        await using ForwardingHost host = CreateHost();
        ForwardingHostServer server = new(host, pipeName);
        using CancellationTokenSource cancellation = new();
        Task serverTask = server.RunAsync(cancellation.Token);
        ForwardingClient client = new(pipeName, TimeSpan.FromSeconds(2));

        ForwardingStatus status = await client.GetStatusAsync().ConfigureAwait(false);

        Assert.AreEqual("mouse", status.RouteId);
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

    private sealed class TestGamepadInputSource(GamepadState state) : IGamepadInputSource
    {
        public bool IsConnected => true;

        public void Run(GamepadInputHandler handler, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            GamepadInput input = new(state, "gamepad");
            handler(in input);
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }

    private sealed class TestXbox360Output : IXbox360Output
    {
        public bool IsConnected => true;

        public List<Xbox360Report> Reports { get; } = [];

        public ValueTask SendAsync(Xbox360Report report, CancellationToken cancellationToken = default)
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
