using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using VirtualMouse.Forwarding;

namespace VirtualMouse.Tests;

/// <summary>Tests controller forwarding broker behavior.</summary>
[TestClass]
public sealed class ControllerBrokerTests
{
    private static readonly ControllerId ControllerId = new("physical-1");

    /// <summary>Steam controls win while physical motion fills the missing feature group.</summary>
    [TestMethod]
    public void ActiveSteamControlsMergeWithPhysicalMotion()
    {
        Guid clientId = Guid.NewGuid();
        FakeControllerOutputFactory factory = new();
        using ControllerBroker broker = new(factory);

        broker.RegisterClient(clientId, ControllerOutput.Xbox360);
        broker.SetActiveClient(clientId);
        broker.UpdatePhysicalController(
            ControllerId,
            new ControllerState(null, Motion(1), null),
            ControllerFeatures.Motion);
        broker.UpdateClientController(
            clientId,
            ControllerId,
            new ControllerState(Standard(ControllerButtons.South), null, null),
            ControllerFeatures.StandardControls);

        FakeControllerOutput output = factory.SingleOutput;
        Assert.AreEqual(ControllerButtons.South, output.LastState.Standard?.Buttons);
        Assert.AreEqual(1, output.LastState.Motion?.GyroX);
    }

    /// <summary>Steam motion wins over physical motion when both are present.</summary>
    [TestMethod]
    public void SteamMotionOverridesPhysicalMotion()
    {
        Guid clientId = Guid.NewGuid();
        FakeControllerOutputFactory factory = new();
        using ControllerBroker broker = new(factory);

        broker.RegisterClient(clientId, ControllerOutput.Xbox360);
        broker.SetActiveClient(clientId);
        broker.UpdatePhysicalController(
            ControllerId,
            new ControllerState(null, Motion(1), null),
            ControllerFeatures.Motion);
        broker.UpdateClientController(
            clientId,
            ControllerId,
            new ControllerState(Standard(ControllerButtons.South), Motion(2), null),
            ControllerFeatures.StandardControls | ControllerFeatures.Motion);

        Assert.AreEqual(2, factory.SingleOutput.LastState.Motion?.GyroX);
    }

    /// <summary>Physical motion fallback can be toggled without removing the physical endpoint.</summary>
    [TestMethod]
    public void PhysicalMotionCanBeDisabledAtRuntime()
    {
        Guid clientId = Guid.NewGuid();
        FakeControllerOutputFactory factory = new();
        using ControllerBroker broker = new(factory);

        broker.RegisterClient(clientId, ControllerOutput.Xbox360);
        broker.SetActiveClient(clientId);
        broker.UpdatePhysicalController(
            ControllerId,
            new ControllerState(null, Motion(1), null),
            ControllerFeatures.Motion | ControllerFeatures.Rumble);
        broker.UpdateClientController(
            clientId,
            ControllerId,
            new ControllerState(Standard(ControllerButtons.South), null, null),
            ControllerFeatures.StandardControls);

        Assert.AreEqual(1, factory.SingleOutput.LastState.Motion?.GyroX);

        broker.SetPhysicalMotionEnabled(false);
        Assert.IsNull(factory.SingleOutput.LastState.Motion);

        broker.SetPhysicalMotionEnabled(true);
        Assert.AreEqual(1, factory.SingleOutput.LastState.Motion?.GyroX);
    }

    /// <summary>Inactive client input connects output but does not drive reports.</summary>
    [TestMethod]
    public void InactiveClientInputConnectsOutputWithoutDrivingReports()
    {
        Guid clientId = Guid.NewGuid();
        FakeControllerOutputFactory factory = new();
        using ControllerBroker broker = new(factory);

        broker.RegisterClient(clientId, ControllerOutput.Xbox360);
        broker.UpdateClientController(
            clientId,
            ControllerId,
            new ControllerState(Standard(ControllerButtons.South), null, null),
            ControllerFeatures.StandardControls);

        Assert.HasCount(1, factory.Outputs);
        Assert.AreEqual(0, factory.SingleOutput.SendCount);
    }

    /// <summary>Output feedback prefers the Steam endpoint and falls back to physical.</summary>
    [TestMethod]
    public void FeedbackUsesSteamThenPhysicalFallback()
    {
        Guid clientId = Guid.NewGuid();
        FakeControllerOutputFactory factory = new();
        FakeFeedbackSink steamFeedback = new(accept: true);
        FakeFeedbackSink physicalFeedback = new(accept: true);
        using ControllerBroker broker = new(factory);

        broker.RegisterClient(clientId, ControllerOutput.Xbox360);
        broker.SetActiveClient(clientId);
        broker.UpdatePhysicalController(
            ControllerId,
            new ControllerState(null, Motion(1), null),
            ControllerFeatures.Motion | ControllerFeatures.Rumble,
            physicalFeedback);
        broker.UpdateClientController(
            clientId,
            ControllerId,
            new ControllerState(Standard(ControllerButtons.South), null, null),
            ControllerFeatures.StandardControls | ControllerFeatures.Rumble,
            steamFeedback);

        factory.SingleOutput.EmitFeedback(new ControllerFeedback(new ControllerRumble(10, 20)));
        Assert.HasCount(1, steamFeedback.Feedback);
        Assert.IsEmpty(physicalFeedback.Feedback);

        steamFeedback.Accept = false;
        factory.SingleOutput.EmitFeedback(new ControllerFeedback(new ControllerRumble(30, 40)));
        Assert.HasCount(2, steamFeedback.Feedback);
        Assert.HasCount(1, physicalFeedback.Feedback);
    }

    /// <summary>Feedback returns to the active client endpoint for the output slot.</summary>
    [TestMethod]
    public void FeedbackUsesControllerEndpointIndex()
    {
        Guid clientId = Guid.NewGuid();
        FakeControllerOutputFactory factory = new();
        FakeFeedbackSink firstFeedback = new(accept: true);
        FakeFeedbackSink secondFeedback = new(accept: true);
        using ControllerBroker broker = new(factory);

        broker.RegisterClient(clientId, ControllerOutput.Xbox360);
        broker.SetActiveClient(clientId);
        broker.UpdateClientController(
            clientId,
            controllerIndex: 0,
            new ControllerId("physical-1"),
            new ControllerState(Standard(ControllerButtons.South), null, null),
            ControllerFeatures.StandardControls | ControllerFeatures.Rumble,
            firstFeedback);
        broker.UpdateClientController(
            clientId,
            controllerIndex: 1,
            new ControllerId("physical-2"),
            new ControllerState(Standard(ControllerButtons.East), null, null),
            ControllerFeatures.StandardControls | ControllerFeatures.Rumble,
            secondFeedback);

        factory.Outputs[0].EmitFeedback(new ControllerFeedback(new ControllerRumble(10, 20)));
        factory.Outputs[1].EmitFeedback(new ControllerFeedback(new ControllerRumble(30, 40)));

        Assert.AreEqual((ushort)10, firstFeedback.Feedback[0].Rumble?.LowFrequency);
        Assert.AreEqual((ushort)30, secondFeedback.Feedback[0].Rumble?.LowFrequency);
    }

    /// <summary>Output devices connect only while a client actively needs them.</summary>
    [TestMethod]
    public void OutputConnectsAndDisconnectsWithUse()
    {
        Guid clientId = Guid.NewGuid();
        FakeControllerOutputFactory factory = new();
        using ControllerBroker broker = new(factory);

        broker.RegisterClient(clientId, ControllerOutput.Xbox360);
        broker.SetActiveClient(clientId);
        broker.UpdateClientController(
            clientId,
            ControllerId,
            new ControllerState(Standard(ControllerButtons.South), null, null),
            ControllerFeatures.StandardControls);

        FakeControllerOutput output = factory.SingleOutput;
        Assert.IsFalse(output.Disposed);
        ControllerBrokerStatus status = broker.GetStatus();
        Assert.AreEqual(clientId, status.ActiveClientId);
        Assert.HasCount(1, status.Slots);
        Assert.AreEqual(ControllerOutput.Xbox360, status.Slots[0].Output);
        Assert.IsTrue(status.Slots[0].OutputConnected);
        Assert.AreEqual(1, status.Slots[0].SteamEndpointCount);
        Assert.AreEqual(ControllerFeatures.StandardControls, status.Slots[0].ActiveSteamFeatures);

        broker.SetActiveClient(null);
        Assert.IsFalse(output.Disposed);
        Assert.IsTrue(broker.GetStatus().Slots[0].OutputConnected);

        broker.SetActiveClient(clientId);
        Assert.AreSame(output, factory.SingleOutput);
        Assert.IsFalse(output.Disposed);

        broker.SetControllerOutputEnabled(false);
        Assert.IsTrue(output.Disposed);

        broker.SetControllerOutputEnabled(true);
        Assert.HasCount(2, factory.Outputs);

        broker.RemoveClient(clientId);
        Assert.IsTrue(factory.Outputs[1].Disposed);
    }

    /// <summary>Output devices are created with stable physical-controller labels.</summary>
    [TestMethod]
    public void OutputUsesPhysicalControllerLabel()
    {
        Guid clientId = Guid.NewGuid();
        FakeControllerOutputFactory factory = new();
        using ControllerBroker broker = new(factory);

        broker.RegisterClient(clientId, ControllerOutput.Xbox360);
        broker.SetActiveClient(clientId);
        ControllerId controllerId = new(ControllerId.Value, "Steam Controller");
        broker.UpdatePhysicalController(
            controllerId,
            new ControllerState(null, Motion(1), null),
            ControllerFeatures.Motion);
        broker.UpdateClientController(
            clientId,
            controllerId,
            new ControllerState(Standard(ControllerButtons.South), null, null),
            ControllerFeatures.StandardControls);

        Assert.AreEqual("Steam Controller", factory.SingleOutput.ControllerId.DisplayName);
    }

    private static ControllerStandardState Standard(ControllerButtons buttons)
    {
        return new ControllerStandardState(buttons, 1, 2, 3, 4, 5, 6);
    }

    private static ControllerMotionState Motion(float gyroX)
    {
        return new ControllerMotionState(true, gyroX, 0, 0, false, 0, 0, 0);
    }

    private sealed class FakeControllerOutputFactory : IControllerOutputFactory
    {
        public List<FakeControllerOutput> Outputs { get; } = [];

        public FakeControllerOutput SingleOutput => Outputs.Count == 1
            ? Outputs[0]
            : throw new InvalidOperationException($"Expected one output, got {Outputs.Count}.");

        public IControllerOutput Connect(ControllerId controllerId, ControllerOutput output)
        {
            FakeControllerOutput connected = new(controllerId, output);
            Outputs.Add(connected);
            return connected;
        }
    }

    private sealed class FakeControllerOutput(
        ControllerId controllerId,
        ControllerOutput output) : IControllerOutput
    {
        private Action<ControllerFeedback>? _feedback;

        public ControllerId ControllerId { get; } = controllerId;

        public ControllerOutput Output { get; } = output;

        public ControllerState LastState { get; private set; }

        public int SendCount { get; private set; }

        public bool Disposed { get; private set; }

        public void Send(in ControllerState state)
        {
            SendCount++;
            LastState = state;
        }

        public IDisposable ListenFeedback(Action<ControllerFeedback> handler)
        {
            _feedback += handler;
            return new Subscription(() => _feedback -= handler);
        }

        public void EmitFeedback(ControllerFeedback feedback)
        {
            _feedback?.Invoke(feedback);
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeFeedbackSink(bool accept) : IControllerFeedbackSink
    {
        public bool Accept { get; set; } = accept;

        public List<ControllerFeedback> Feedback { get; } = [];

        public bool TrySendFeedback(ControllerFeedback feedback)
        {
            Feedback.Add(feedback);
            return Accept;
        }
    }

    private sealed class Subscription(Action dispose) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            dispose();
        }
    }
}
