using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VirtualMouse.Forwarding;
using VirtualMouse.Hosting;

namespace VirtualMouse.Tests;

/// <summary>Tests mouse forwarding broker behavior.</summary>
[TestClass]
public sealed class MouseBrokerTests
{
    /// <summary>Mouse output connects only while the active client wants it.</summary>
    [TestMethod]
    public void OutputConnectsAndDisconnectsWithActiveClient()
    {
        Guid clientId = Guid.NewGuid();
        FakeMouseOutputFactory factory = new();
        using MouseBroker broker = new(factory);

        broker.RegisterClient(clientId, MouseOutput.Viiper);
        Assert.IsEmpty(factory.Outputs);

        broker.SetActiveClient(clientId);
        Assert.HasCount(1, factory.Outputs);
        Assert.IsFalse(factory.Outputs[0].Disposed);

        broker.SetMouseOutputEnabled(false);
        Assert.IsTrue(factory.Outputs[0].Disposed);
    }

    /// <summary>Mouse reports are sent only when output is active and non-empty.</summary>
    [TestMethod]
    public void SendsOnlyActiveNonEmptyReports()
    {
        Guid clientId = Guid.NewGuid();
        FakeMouseOutputFactory factory = new();
        using MouseBroker broker = new(factory);

        broker.RegisterClient(clientId, MouseOutput.Viiper);
        broker.Send(new MouseInput(new MouseReport(MouseButtons.Left, 1, 2, 0), "mouse"));
        broker.SetActiveClient(clientId);
        broker.Send(new MouseInput(MouseReport.Empty, "mouse"));
        broker.Send(new MouseInput(new MouseReport(MouseButtons.Left, 1, 2, 0), "mouse"));

        Assert.HasCount(1, factory.Outputs[0].Reports);
        Assert.AreEqual(MouseButtons.Left, factory.Outputs[0].Reports[0].Buttons);
    }

    /// <summary>Mouse reports filtered by the active output are not forwarded.</summary>
    [TestMethod]
    public void SkipsReportsFilteredByOutput()
    {
        Guid clientId = Guid.NewGuid();
        FakeMouseOutputFactory factory = new()
        {
            FilterDeviceName = "loopback",
        };
        using MouseBroker broker = new(factory);

        broker.RegisterClient(clientId, MouseOutput.Viiper);
        broker.SetActiveClient(clientId);
        broker.Send(new MouseInput(new MouseReport(MouseButtons.None, 1, 0, 0), "loopback"));
        broker.Send(new MouseInput(new MouseReport(MouseButtons.None, 2, 0, 0), "real"));

        Assert.HasCount(1, factory.Outputs[0].Reports);
        Assert.AreEqual(2, factory.Outputs[0].Reports[0].DeltaX);
    }

    /// <summary>Raw Input mouse forwarding accepts only Steam Input-style mouse packets.</summary>
    [TestMethod]
    public void RawInputMouseFilterAcceptsOnlyUnnamedHandleZeroInput()
    {
        MouseReport report = new(MouseButtons.None, 1, 0, 0);

        Assert.IsTrue(MouseInputPump.ShouldForwardRawInputMouse(new MouseInput(report, null, nint.Zero)));
        Assert.IsTrue(MouseInputPump.ShouldForwardRawInputMouse(new MouseInput(report, "", nint.Zero)));
        Assert.IsFalse(MouseInputPump.ShouldForwardRawInputMouse(new MouseInput(report, "mouse", nint.Zero)));
        Assert.IsFalse(MouseInputPump.ShouldForwardRawInputMouse(new MouseInput(report, null, 123)));
    }

    private sealed class FakeMouseOutputFactory : IMouseOutputFactory
    {
        public List<FakeMouseOutput> Outputs { get; } = [];

        public string? FilterDeviceName { get; init; }

        public IMouseOutput Connect(MouseOutput output)
        {
            FakeMouseOutput connected = new(output, FilterDeviceName);
            Outputs.Add(connected);
            return connected;
        }
    }

    private sealed class FakeMouseOutput(MouseOutput output, string? filterDeviceName) : IMouseOutput
    {
        public MouseOutput Output { get; } = output;

        public bool IsConnected => !Disposed;

        public bool Disposed { get; private set; }

        public List<MouseReport> Reports { get; } = [];

        public bool FilterInput(in MouseInput input)
        {
            return string.Equals(input.DeviceName, filterDeviceName, StringComparison.OrdinalIgnoreCase);
        }

        public ValueTask SendAsync(MouseReport report, CancellationToken cancellationToken = default)
        {
            Reports.Add(report);
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
