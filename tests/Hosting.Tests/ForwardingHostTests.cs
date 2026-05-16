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

    /// <summary>Checks protocol commands.</summary>
    [TestMethod]
    public async Task ProtocolUpdatesHostState()
    {
        await using ForwardingHost host = CreateHost();

        Assert.IsFalse(host.IsEnabled);
        Assert.AreEqual(
            "STATUS route=mouse enabled=false connected=true enabledClients=0",
            ForwardingHostControlProtocol.Execute(host, "STATUS"));
        Assert.AreEqual("ERR unknown command", ForwardingHostControlProtocol.Execute(host, "unknown"));
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

    /// <summary>Checks status parsing.</summary>
    [TestMethod]
    public void ParseStatusReturnsValues()
    {
        ForwardingStatus status = ForwardingHostControlProtocol.ParseStatus(
            "STATUS route=xpad enabled=true connected=false enabledClients=2");

        Assert.AreEqual("xpad", status.RouteId);
        Assert.IsTrue(status.IsEnabled);
        Assert.IsFalse(status.IsConnected);
        Assert.AreEqual(2, status.EnabledClientCount);
    }

    /// <summary>Checks route-specific ownership and pipe names.</summary>
    [TestMethod]
    public void RouteSpecificRuntimeNamesAreDistinct()
    {
        Assert.AreNotEqual(
            ForwardingServer.GetPipeName(ForwardingRouteKind.Mouse),
            ForwardingServer.GetPipeName(ForwardingRouteKind.Xpad));
        Assert.AreNotEqual(
            ForwardingServer.GetOwnershipName(ForwardingRouteKind.Mouse),
            ForwardingServer.GetOwnershipName(ForwardingRouteKind.Xpad));
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
