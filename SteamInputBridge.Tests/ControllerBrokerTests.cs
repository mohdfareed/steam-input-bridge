using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SteamInputBridge.Forwarding.Controller;
using SteamInputBridge.Forwarding.Controller.Routing;

namespace SteamInputBridge.Tests;

/// <summary>Tests controller forwarding broker behavior.</summary>
[TestClass]
public sealed class ControllerBrokerTests
{
    private static readonly ControllerId ControllerId = new("physical-1");

    /// <summary>Client controls win while physical motion fills the missing feature group.</summary>
    [TestMethod]
    public void ActiveClientControlsMergeWithPhysicalMotion()
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

    /// <summary>Client motion wins over physical motion when both are present.</summary>
    [TestMethod]
    public void ClientMotionOverridesPhysicalMotion()
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

    /// <summary>Inactive client frames do not become another run's stored output state.</summary>
    [TestMethod]
    public void InactiveClientFramesAreIgnored()
    {
        Guid first = Guid.NewGuid();
        Guid second = Guid.NewGuid();
        FakeControllerOutputFactory factory = new();
        using ControllerBroker broker = new(factory);

        broker.RegisterClient(first, ControllerOutput.Xbox360);
        broker.RegisterClient(second, ControllerOutput.Xbox360);
        broker.UpdatePhysicalController(ControllerId, ControllerState.Empty, ControllerFeatures.StandardControls);
        broker.RegisterClientController(first, 0, ControllerId, ControllerFeatures.StandardControls);
        broker.RegisterClientController(second, 0, ControllerId, ControllerFeatures.StandardControls);

        broker.SetActiveClient(first);
        broker.UpdateClientController(
            first,
            0,
            new ControllerState(Standard(ControllerButtons.South), null, null),
            ControllerFeatures.StandardControls);
        Assert.AreEqual(ControllerButtons.South, factory.SingleOutput.LastState.Standard?.Buttons);

        broker.UpdateClientController(
            second,
            0,
            new ControllerState(Standard(ControllerButtons.East), null, null),
            ControllerFeatures.StandardControls);
        Assert.AreEqual(ControllerButtons.South, factory.SingleOutput.LastState.Standard?.Buttons);

        broker.SetActiveClient(second);
        Assert.AreEqual(ControllerState.Empty, factory.SingleOutput.LastState);
    }

    /// <summary>Physical trigger axes fill Steam/client streams that omit only the trigger values.</summary>
    [TestMethod]
    public void PhysicalTriggersFillClientStandardTriggerGaps()
    {
        Guid clientId = Guid.NewGuid();
        FakeControllerOutputFactory factory = new();
        using ControllerBroker broker = new(factory);

        broker.RegisterClient(clientId, ControllerOutput.Ds4);
        broker.SetActiveClient(clientId);
        broker.UpdatePhysicalController(
            ControllerId,
            new ControllerState(
                new ControllerStandardState(ControllerButtons.North, 100, 200, 300, 400, 12000, 24000),
                null,
                null),
            ControllerFeatures.StandardControls);
        broker.UpdateClientController(
            clientId,
            ControllerId,
            new ControllerState(
                new ControllerStandardState(ControllerButtons.South, 1, 2, 3, 4, 0, 0),
                null,
                null),
            ControllerFeatures.StandardControls);

        ControllerStandardState? standard = factory.SingleOutput.LastState.Standard;
        Assert.AreEqual(ControllerButtons.South, standard?.Buttons);
        Assert.AreEqual((short)1, standard?.LeftX);
        Assert.AreEqual((short)2, standard?.LeftY);
        Assert.AreEqual((ushort)12000, standard?.LeftTrigger);
        Assert.AreEqual((ushort)24000, standard?.RightTrigger);
    }

    /// <summary>Client trigger values still win when Steam/client reports them.</summary>
    [TestMethod]
    public void ClientTriggersOverridePhysicalTriggers()
    {
        Guid clientId = Guid.NewGuid();
        FakeControllerOutputFactory factory = new();
        using ControllerBroker broker = new(factory);

        broker.RegisterClient(clientId, ControllerOutput.Ds4);
        broker.SetActiveClient(clientId);
        broker.UpdatePhysicalController(
            ControllerId,
            new ControllerState(
                new ControllerStandardState(ControllerButtons.None, 0, 0, 0, 0, 12000, 24000),
                null,
                null),
            ControllerFeatures.StandardControls);
        broker.UpdateClientController(
            clientId,
            ControllerId,
            new ControllerState(
                new ControllerStandardState(ControllerButtons.None, 0, 0, 0, 0, 3000, 4000),
                null,
                null),
            ControllerFeatures.StandardControls);

        ControllerStandardState? standard = factory.SingleOutput.LastState.Standard;
        Assert.AreEqual((ushort)3000, standard?.LeftTrigger);
        Assert.AreEqual((ushort)4000, standard?.RightTrigger);
    }

    /// <summary>Physical touch contacts fill client streams that expose only one contact.</summary>
    [TestMethod]
    public void PhysicalTouchpadFillsClientSecondContactGap()
    {
        Guid clientId = Guid.NewGuid();
        FakeControllerOutputFactory factory = new();
        using ControllerBroker broker = new(factory);

        broker.RegisterClient(clientId, ControllerOutput.Ds4);
        broker.SetActiveClient(clientId);
        broker.UpdatePhysicalController(
            ControllerId,
            new ControllerState(
                null,
                null,
                new ControllerTouchpadState(
                    new ControllerTouchContact(true, 0.10f, 0.20f, 0.30f),
                    new ControllerTouchContact(true, 0.70f, 0.80f, 0.90f))),
            ControllerFeatures.Touchpad);
        broker.UpdateClientController(
            clientId,
            ControllerId,
            new ControllerState(
                null,
                null,
                new ControllerTouchpadState(
                    new ControllerTouchContact(true, 0.40f, 0.50f, 0.60f),
                    default)),
            ControllerFeatures.Touchpad);

        ControllerTouchpadState? touchpad = factory.SingleOutput.LastState.Touchpad;
        Assert.AreEqual(0.40f, touchpad?.Touch1.X);
        Assert.AreEqual(0.50f, touchpad?.Touch1.Y);
        Assert.AreEqual(0.60f, touchpad?.Touch1.Pressure);
        Assert.AreEqual(0.70f, touchpad?.Touch2.X);
        Assert.AreEqual(0.80f, touchpad?.Touch2.Y);
        Assert.AreEqual(0.90f, touchpad?.Touch2.Pressure);
    }

    /// <summary>Client touch contacts still win when both contacts are present.</summary>
    [TestMethod]
    public void ClientTouchpadContactsOverridePhysicalContacts()
    {
        Guid clientId = Guid.NewGuid();
        FakeControllerOutputFactory factory = new();
        using ControllerBroker broker = new(factory);

        broker.RegisterClient(clientId, ControllerOutput.Ds4);
        broker.SetActiveClient(clientId);
        broker.UpdatePhysicalController(
            ControllerId,
            new ControllerState(
                null,
                null,
                new ControllerTouchpadState(
                    new ControllerTouchContact(true, 0.10f, 0.20f),
                    new ControllerTouchContact(true, 0.30f, 0.40f))),
            ControllerFeatures.Touchpad);
        broker.UpdateClientController(
            clientId,
            ControllerId,
            new ControllerState(
                null,
                null,
                new ControllerTouchpadState(
                    new ControllerTouchContact(true, 0.50f, 0.60f),
                    new ControllerTouchContact(true, 0.70f, 0.80f))),
            ControllerFeatures.Touchpad);

        ControllerTouchpadState? touchpad = factory.SingleOutput.LastState.Touchpad;
        Assert.AreEqual(0.50f, touchpad?.Touch1.X);
        Assert.AreEqual(0.60f, touchpad?.Touch1.Y);
        Assert.AreEqual(0.70f, touchpad?.Touch2.X);
        Assert.AreEqual(0.80f, touchpad?.Touch2.Y);
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
        Assert.IsFalse(factory.SingleOutput.Disposed);
    }

    /// <summary>Motion gating also suppresses motion from a physical controller exposed to the client.</summary>
    [TestMethod]
    public void MotionCanBeDisabledForClientEndpoint()
    {
        Guid clientId = Guid.NewGuid();
        FakeControllerOutputFactory factory = new();
        using ControllerBroker broker = new(factory);

        broker.RegisterClient(clientId, ControllerOutput.Xbox360);
        broker.SetActiveClient(clientId);
        broker.UpdatePhysicalController(
            ControllerId,
            ControllerState.Empty,
            ControllerFeatures.None);
        broker.UpdateClientController(
            clientId,
            ControllerId,
            new ControllerState(Standard(ControllerButtons.South), Motion(2), null),
            ControllerFeatures.StandardControls | ControllerFeatures.Motion);

        Assert.AreEqual(2, factory.SingleOutput.LastState.Motion?.GyroX);

        broker.SetPhysicalMotionEnabled(false);

        Assert.IsNull(factory.SingleOutput.LastState.Motion);
        Assert.IsFalse(factory.SingleOutput.Disposed);
    }

    /// <summary>Physical disconnect drops that slot's output until the physical endpoint returns.</summary>
    [TestMethod]
    public void PhysicalControllerRemovalDisconnectsOutputUntilPhysicalReturns()
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

        Assert.AreEqual(1, factory.SingleOutput.LastState.Motion?.GyroX);
        FakeControllerOutput output = factory.SingleOutput;

        broker.RemovePhysicalController(ControllerId);

        Assert.IsTrue(output.Disposed);
        ControllerBrokerStatus status = broker.GetStatus();
        Assert.HasCount(1, status.Slots);
        Assert.IsFalse(status.Slots[0].HasPhysicalEndpoint);
        Assert.IsNull(status.Slots[0].PhysicalFeatures);
        Assert.IsTrue(status.Slots[0].HasActiveClientEndpoint);
        Assert.IsFalse(status.Slots[0].OutputConnected);

        broker.UpdatePhysicalController(
            ControllerId,
            new ControllerState(null, Motion(2), null),
            ControllerFeatures.Motion);

        Assert.HasCount(2, factory.Outputs);
        Assert.AreNotSame(output, factory.Outputs[1]);
        Assert.AreEqual(2, factory.Outputs[1].LastState.Motion?.GyroX);
    }

    /// <summary>Inactive client input is ignored before it can create an output.</summary>
    [TestMethod]
    public void InactiveClientInputDoesNotCreateOutput()
    {
        Guid clientId = Guid.NewGuid();
        FakeControllerOutputFactory factory = new();
        using ControllerBroker broker = new(factory);

        broker.RegisterClient(clientId, ControllerOutput.Xbox360);
        broker.UpdatePhysicalController(
            ControllerId,
            ControllerState.Empty,
            ControllerFeatures.None);
        broker.UpdateClientController(
            clientId,
            ControllerId,
            new ControllerState(Standard(ControllerButtons.South), null, null),
            ControllerFeatures.StandardControls);

        Assert.IsEmpty(factory.Outputs);
    }

    /// <summary>Controller registration connects output before the first input frame.</summary>
    [TestMethod]
    public void ControllerRegistrationConnectsOutputBeforeInput()
    {
        Guid clientId = Guid.NewGuid();
        FakeControllerOutputFactory factory = new();
        using ControllerBroker broker = new(factory);

        broker.RegisterClient(clientId, ControllerOutput.Xbox360);
        broker.UpdatePhysicalController(
            ControllerId,
            ControllerState.Empty,
            ControllerFeatures.None);
        broker.RegisterClientController(
            clientId,
            0,
            ControllerId,
            ControllerFeatures.StandardControls);

        Assert.HasCount(1, factory.Outputs);
        Assert.AreEqual(1, factory.SingleOutput.SendCount);
        Assert.IsNull(factory.SingleOutput.LastState.Standard);
        ControllerBrokerStatus status = broker.GetStatus();
        Assert.HasCount(1, status.Slots);
        Assert.AreEqual(1, status.Slots[0].ClientEndpointCount);
        Assert.IsTrue(status.Slots[0].OutputConnected);
    }

    /// <summary>Client route swaps keep existing virtual outputs connected.</summary>
    [TestMethod]
    public void BatchControllerRegistrationKeepsOutputsDuringIndexSwap()
    {
        Guid clientId = Guid.NewGuid();
        ControllerId first = new("physical-1", "First");
        ControllerId second = new("physical-2", "Second");
        FakeControllerOutputFactory factory = new();
        using ControllerBroker broker = new(factory);

        broker.RegisterClient(clientId, ControllerOutput.Xbox360);
        broker.UpdatePhysicalController(first, ControllerState.Empty, ControllerFeatures.None);
        broker.UpdatePhysicalController(second, ControllerState.Empty, ControllerFeatures.None);
        broker.SetClientControllers(
            clientId,
            [
                new ControllerClientRegistration(0, first, ControllerFeatures.StandardControls),
                new ControllerClientRegistration(1, second, ControllerFeatures.StandardControls),
            ]);

        FakeControllerOutput firstOutput = FindOutput(factory, first);
        FakeControllerOutput secondOutput = FindOutput(factory, second);

        broker.SetClientControllers(
            clientId,
            [
                new ControllerClientRegistration(0, second, ControllerFeatures.StandardControls),
                new ControllerClientRegistration(1, first, ControllerFeatures.StandardControls),
            ]);

        Assert.HasCount(2, factory.Outputs);
        Assert.AreSame(firstOutput, FindOutput(factory, first));
        Assert.AreSame(secondOutput, FindOutput(factory, second));
        Assert.IsFalse(firstOutput.Disposed);
        Assert.IsFalse(secondOutput.Disposed);
    }

    /// <summary>Physical slots wait for a resolved client endpoint before creating virtual output.</summary>
    [TestMethod]
    public void PhysicalSlotWaitsForResolvedClientEndpoint()
    {
        Guid clientId = Guid.NewGuid();
        FakeControllerOutputFactory factory = new();
        using ControllerBroker broker = new(factory);

        broker.UpdatePhysicalController(
            ControllerId,
            new ControllerState(null, Motion(1), null),
            ControllerFeatures.Motion);

        Assert.IsEmpty(factory.Outputs);
        ControllerBrokerStatus physicalStatus = broker.GetStatus();
        Assert.HasCount(1, physicalStatus.Slots);
        Assert.IsTrue(physicalStatus.Slots[0].HasPhysicalEndpoint);
        Assert.IsFalse(physicalStatus.Slots[0].OutputConnected);

        broker.RegisterClient(clientId, ControllerOutput.Xbox360);

        Assert.IsEmpty(factory.Outputs);
        ControllerBrokerStatus waitingStatus = broker.GetStatus();
        Assert.HasCount(1, waitingStatus.Slots);
        Assert.IsTrue(waitingStatus.Slots[0].HasPhysicalEndpoint);
        Assert.AreEqual(0, waitingStatus.Slots[0].ClientEndpointCount);
        Assert.IsFalse(waitingStatus.Slots[0].OutputConnected);

        broker.RegisterClientController(
            clientId,
            0,
            ControllerId,
            ControllerFeatures.StandardControls);

        Assert.HasCount(1, factory.Outputs);
        ControllerBrokerStatus routedStatus = broker.GetStatus();
        Assert.HasCount(1, routedStatus.Slots);
        Assert.IsTrue(routedStatus.Slots[0].HasPhysicalEndpoint);
        Assert.AreEqual(1, routedStatus.Slots[0].ClientEndpointCount);
        Assert.IsTrue(routedStatus.Slots[0].OutputConnected);
    }

    /// <summary>Physical-only slots do not create output before a matching client stream exists.</summary>
    [TestMethod]
    public void ActivePhysicalOnlySlotDoesNotCreateOutput()
    {
        Guid clientId = Guid.NewGuid();
        FakeControllerOutputFactory factory = new();
        using ControllerBroker broker = new(factory);

        broker.RegisterClient(clientId, ControllerOutput.Xbox360);
        broker.SetActiveClient(clientId);
        broker.UpdatePhysicalController(
            ControllerId,
            new ControllerState(Standard(ControllerButtons.South), Motion(3), null),
            ControllerFeatures.StandardControls | ControllerFeatures.Motion);

        Assert.IsEmpty(factory.Outputs);
        Assert.IsFalse(broker.GetStatus().Slots[0].OutputConnected);
    }

    /// <summary>Client-only routes do not connect output unless they are explicitly allowed.</summary>
    [TestMethod]
    public void ClientOnlyRouteRequiresExplicitOutputOwnership()
    {
        Guid clientId = Guid.NewGuid();
        ControllerId steamController = new("steam:0001fa99604010e6", "Steam Controller");
        FakeControllerOutputFactory factory = new();
        using ControllerBroker broker = new(factory);

        broker.RegisterClient(clientId, ControllerOutput.Xbox360);
        broker.SetClientControllers(
            clientId,
            [new ControllerClientRegistration(0, steamController, ControllerFeatures.StandardControls)]);

        Assert.IsEmpty(factory.Outputs);
        ControllerBrokerStatus status = broker.GetStatus();
        Assert.HasCount(1, status.Slots);
        Assert.IsFalse(status.Slots[0].HasPhysicalEndpoint);
        Assert.IsFalse(status.Slots[0].OutputConnected);
    }

    /// <summary>A real Steam Controller may own output without a host-visible physical slot.</summary>
    [TestMethod]
    public void ClientOnlySteamControllerRegistrationConnectsOutput()
    {
        Guid clientId = Guid.NewGuid();
        ControllerId steamController = new("steam:0001fa99604010e6", "Steam Controller");
        FakeControllerOutputFactory factory = new();
        using ControllerBroker broker = new(factory);

        broker.RegisterClient(clientId, ControllerOutput.Ds4);
        broker.SetClientControllers(
            clientId,
            [new ControllerClientRegistration(
                0,
                steamController,
                ControllerFeatures.StandardControls | ControllerFeatures.Touchpad,
                CanOwnOutputWithoutPhysical: true)]);

        Assert.HasCount(1, factory.Outputs);
        Assert.AreEqual(steamController, factory.SingleOutput.ControllerId);
        ControllerBrokerStatus status = broker.GetStatus();
        Assert.HasCount(1, status.Slots);
        Assert.IsFalse(status.Slots[0].HasPhysicalEndpoint);
        Assert.IsTrue(status.Slots[0].OutputConnected);
    }

    /// <summary>Active controller registration sends empty state until input arrives.</summary>
    [TestMethod]
    public void ActiveControllerRegistrationSendsEmptyState()
    {
        Guid clientId = Guid.NewGuid();
        FakeControllerOutputFactory factory = new();
        using ControllerBroker broker = new(factory);

        broker.RegisterClient(clientId, ControllerOutput.Xbox360);
        broker.SetActiveClient(clientId);
        broker.UpdatePhysicalController(
            ControllerId,
            ControllerState.Empty,
            ControllerFeatures.None);
        broker.RegisterClientController(
            clientId,
            0,
            ControllerId,
            ControllerFeatures.StandardControls);

        Assert.HasCount(1, factory.Outputs);
        Assert.AreEqual(2, factory.SingleOutput.SendCount);
        Assert.IsNull(factory.SingleOutput.LastState.Standard);
    }

    /// <summary>Only the active client may drive shared controller slots.</summary>
    [TestMethod]
    public void ActiveClientSwitchSendsCurrentStatesAndIgnoresInactiveInput()
    {
        Guid firstClient = Guid.NewGuid();
        Guid secondClient = Guid.NewGuid();
        ControllerId firstController = new("steam:first", "First");
        ControllerId secondController = new("steam:second", "Second");
        FakeControllerOutputFactory factory = new();
        using ControllerBroker broker = new(factory);

        broker.RegisterClient(firstClient, ControllerOutput.Xbox360);
        broker.RegisterClient(secondClient, ControllerOutput.Xbox360);
        broker.SetActiveClient(firstClient);
        broker.UpdatePhysicalController(firstController, ControllerState.Empty, ControllerFeatures.None);
        broker.UpdatePhysicalController(secondController, ControllerState.Empty, ControllerFeatures.None);
        broker.UpdateClientController(
            firstClient,
            0,
            firstController,
            new ControllerState(Standard(ControllerButtons.South), null, null),
            ControllerFeatures.StandardControls);
        broker.UpdateClientController(
            firstClient,
            1,
            secondController,
            new ControllerState(Standard(ControllerButtons.East), null, null),
            ControllerFeatures.StandardControls);

        FakeControllerOutput firstOutput = FindOutput(factory, firstController);
        FakeControllerOutput secondOutput = FindOutput(factory, secondController);
        Assert.AreEqual(ControllerButtons.South, firstOutput.LastState.Standard?.Buttons);
        Assert.AreEqual(ControllerButtons.East, secondOutput.LastState.Standard?.Buttons);

        broker.UpdateClientController(
            secondClient,
            0,
            firstController,
            new ControllerState(Standard(ControllerButtons.North), null, null),
            ControllerFeatures.StandardControls);
        broker.UpdateClientController(
            secondClient,
            1,
            secondController,
            new ControllerState(Standard(ControllerButtons.West), null, null),
            ControllerFeatures.StandardControls);

        Assert.AreEqual(ControllerButtons.South, firstOutput.LastState.Standard?.Buttons);
        Assert.AreEqual(ControllerButtons.East, secondOutput.LastState.Standard?.Buttons);

        broker.SetActiveClient(secondClient);

        Assert.IsNull(firstOutput.LastState.Standard);
        Assert.IsNull(secondOutput.LastState.Standard);

        broker.UpdateClientController(
            firstClient,
            0,
            firstController,
            new ControllerState(Standard(ControllerButtons.Back), null, null),
            ControllerFeatures.StandardControls);

        Assert.IsNull(firstOutput.LastState.Standard);

        broker.SetActiveClient(null);

        Assert.IsNull(firstOutput.LastState.Standard);
        Assert.IsNull(secondOutput.LastState.Standard);
    }

    /// <summary>Client endpoint removal disconnects output and keeps the physical slot idle.</summary>
    [TestMethod]
    public void ClientControllerRemovalDisconnectsPhysicalSlotOutput()
    {
        Guid clientId = Guid.NewGuid();
        FakeControllerOutputFactory factory = new();
        using ControllerBroker broker = new(factory);

        broker.RegisterClient(clientId, ControllerOutput.Xbox360);
        broker.SetActiveClient(clientId);
        broker.UpdatePhysicalController(
            ControllerId,
            ControllerState.Empty,
            ControllerFeatures.None);
        broker.UpdateClientController(
            clientId,
            ControllerId,
            new ControllerState(Standard(ControllerButtons.South), null, null),
            ControllerFeatures.StandardControls);

        FakeControllerOutput output = factory.SingleOutput;
        broker.RemoveClientControllers(clientId);

        Assert.IsTrue(output.Disposed);
        ControllerBrokerStatus status = broker.GetStatus();
        Assert.HasCount(1, status.Slots);
        Assert.IsTrue(status.Slots[0].HasPhysicalEndpoint);
        Assert.AreEqual(0, status.Slots[0].ClientEndpointCount);
        Assert.IsFalse(status.Slots[0].OutputConnected);
    }

    /// <summary>Removing one client controller stream disconnects only that slot's output.</summary>
    [TestMethod]
    public void SingleClientControllerRemovalKeepsOtherSlots()
    {
        Guid clientId = Guid.NewGuid();
        ControllerId firstController = new("steam:first", "First");
        ControllerId secondController = new("steam:second", "Second");
        FakeControllerOutputFactory factory = new();
        using ControllerBroker broker = new(factory);

        broker.RegisterClient(clientId, ControllerOutput.Xbox360);
        broker.SetActiveClient(clientId);
        broker.UpdatePhysicalController(firstController, ControllerState.Empty, ControllerFeatures.None);
        broker.UpdatePhysicalController(secondController, ControllerState.Empty, ControllerFeatures.None);
        broker.UpdateClientController(
            clientId,
            0,
            firstController,
            new ControllerState(Standard(ControllerButtons.South), null, null),
            ControllerFeatures.StandardControls);
        broker.UpdateClientController(
            clientId,
            1,
            secondController,
            new ControllerState(Standard(ControllerButtons.East), null, null),
            ControllerFeatures.StandardControls);

        FakeControllerOutput firstOutput = FindOutput(factory, firstController);
        FakeControllerOutput secondOutput = FindOutput(factory, secondController);

        broker.RemoveClientController(clientId, 0);

        Assert.IsTrue(firstOutput.Disposed);
        Assert.IsFalse(secondOutput.Disposed);
        Assert.AreSame(firstOutput, FindOutput(factory, firstController));
        Assert.AreSame(secondOutput, FindOutput(factory, secondController));
        ControllerBrokerStatus status = broker.GetStatus();
        Assert.HasCount(2, status.Slots);
        ControllerSlotStatus remainingOutput = FindSlot(status, secondController);
        ControllerSlotStatus removedOutput = FindSlot(status, firstController);
        Assert.IsTrue(remainingOutput.OutputConnected);
        Assert.IsFalse(removedOutput.OutputConnected);
    }

    /// <summary>Output feedback prefers the client endpoint and falls back to physical.</summary>
    [TestMethod]
    public void FeedbackUsesClientThenPhysicalFallback()
    {
        Guid clientId = Guid.NewGuid();
        FakeControllerOutputFactory factory = new();
        FakeFeedbackSink clientFeedback = new(accept: true);
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
            clientFeedback);

        factory.SingleOutput.EmitFeedback(new ControllerFeedback(new ControllerRumble(10, 20)));
        Assert.HasCount(1, clientFeedback.Feedback);
        Assert.IsEmpty(physicalFeedback.Feedback);

        clientFeedback.Accept = false;
        factory.SingleOutput.EmitFeedback(new ControllerFeedback(new ControllerRumble(30, 40)));
        Assert.HasCount(3, clientFeedback.Feedback);
        Assert.HasCount(1, physicalFeedback.Feedback);
        Assert.AreEqual((ushort)0, clientFeedback.Feedback[2].Rumble?.LowFrequency);
    }

    /// <summary>Combined DS4 feedback prefers an endpoint that can apply both rumble and light.</summary>
    [TestMethod]
    public void FeedbackPrefersEndpointWithAllRequestedFeatures()
    {
        Guid clientId = Guid.NewGuid();
        FakeControllerOutputFactory factory = new();
        FakeFeedbackSink clientFeedback = new(accept: true);
        FakeFeedbackSink physicalFeedback = new(accept: true);
        using ControllerBroker broker = new(factory);

        broker.RegisterClient(clientId, ControllerOutput.Ds4);
        broker.SetActiveClient(clientId);
        broker.UpdatePhysicalController(
            ControllerId,
            new ControllerState(null, Motion(1), null),
            ControllerFeatures.Motion | ControllerFeatures.Rumble | ControllerFeatures.Light,
            physicalFeedback);
        broker.UpdateClientController(
            clientId,
            ControllerId,
            new ControllerState(Standard(ControllerButtons.South), null, null),
            ControllerFeatures.StandardControls | ControllerFeatures.Rumble,
            clientFeedback);

        factory.SingleOutput.EmitFeedback(new ControllerFeedback(
            new ControllerRumble(10, 20),
            new ControllerLight(1, 2, 3, 4, 5)));

        Assert.IsEmpty(clientFeedback.Feedback);
        Assert.HasCount(1, physicalFeedback.Feedback);
        Assert.AreEqual((ushort)10, physicalFeedback.Feedback[0].Rumble?.LowFrequency);
        Assert.AreEqual((byte)3, physicalFeedback.Feedback[0].Light?.Blue);
        Assert.AreEqual((byte)5, physicalFeedback.Feedback[0].Light?.FlashOff);
    }

    /// <summary>Held feedback is replayed when the active endpoint reconnects.</summary>
    [TestMethod]
    public void FeedbackReplaysWhenEndpointReconnects()
    {
        Guid clientId = Guid.NewGuid();
        FakeControllerOutputFactory factory = new();
        FakeFeedbackSink firstFeedback = new(accept: true);
        FakeFeedbackSink secondFeedback = new(accept: true);
        using ControllerBroker broker = new(factory);

        broker.RegisterClient(clientId, ControllerOutput.Xbox360);
        broker.SetActiveClient(clientId);
        broker.UpdatePhysicalController(
            ControllerId,
            ControllerState.Empty,
            ControllerFeatures.None);
        broker.UpdateClientController(
            clientId,
            ControllerId,
            new ControllerState(Standard(ControllerButtons.South), null, null),
            ControllerFeatures.StandardControls | ControllerFeatures.Rumble,
            firstFeedback);

        factory.SingleOutput.EmitFeedback(new ControllerFeedback(new ControllerRumble(10, 20)));
        broker.UpdateClientController(
            clientId,
            ControllerId,
            new ControllerState(Standard(ControllerButtons.South), null, null),
            ControllerFeatures.StandardControls | ControllerFeatures.Rumble,
            secondFeedback);

        Assert.HasCount(1, firstFeedback.Feedback);
        Assert.HasCount(1, secondFeedback.Feedback);
        Assert.AreEqual((ushort)10, secondFeedback.Feedback[0].Rumble?.LowFrequency);
    }

    /// <summary>Held feedback is not replayed for every input report from the same endpoint.</summary>
    [TestMethod]
    public void FeedbackDoesNotReplayOnSteadyStateInputReports()
    {
        Guid clientId = Guid.NewGuid();
        FakeControllerOutputFactory factory = new();
        FakeFeedbackSink feedback = new(accept: true);
        using ControllerBroker broker = new(factory);

        broker.RegisterClient(clientId, ControllerOutput.Xbox360);
        broker.SetActiveClient(clientId);
        broker.UpdatePhysicalController(
            ControllerId,
            ControllerState.Empty,
            ControllerFeatures.None);
        broker.UpdateClientController(
            clientId,
            ControllerId,
            new ControllerState(Standard(ControllerButtons.South), null, null),
            ControllerFeatures.StandardControls | ControllerFeatures.Rumble,
            feedback);

        factory.SingleOutput.EmitFeedback(new ControllerFeedback(new ControllerRumble(10, 20)));
        broker.UpdateClientController(
            clientId,
            ControllerId,
            new ControllerState(Standard(ControllerButtons.East), null, null),
            ControllerFeatures.StandardControls | ControllerFeatures.Rumble,
            feedback);
        broker.UpdateClientController(
            clientId,
            ControllerId,
            new ControllerState(Standard(ControllerButtons.North), null, null),
            ControllerFeatures.StandardControls | ControllerFeatures.Rumble,
            feedback);

        Assert.HasCount(1, feedback.Feedback);
    }

    /// <summary>Held feedback is stopped when the active client is cleared.</summary>
    [TestMethod]
    public void FeedbackStopsWhenActiveClientClears()
    {
        Guid clientId = Guid.NewGuid();
        FakeControllerOutputFactory factory = new();
        FakeFeedbackSink feedback = new(accept: true);
        using ControllerBroker broker = new(factory);

        broker.RegisterClient(clientId, ControllerOutput.Xbox360);
        broker.SetActiveClient(clientId);
        broker.UpdatePhysicalController(
            ControllerId,
            ControllerState.Empty,
            ControllerFeatures.None);
        broker.UpdateClientController(
            clientId,
            ControllerId,
            new ControllerState(Standard(ControllerButtons.South), null, null),
            ControllerFeatures.StandardControls | ControllerFeatures.Rumble,
            feedback);

        factory.SingleOutput.EmitFeedback(new ControllerFeedback(new ControllerRumble(10, 20)));
        broker.SetActiveClient(null);

        Assert.HasCount(2, feedback.Feedback);
        Assert.AreEqual((ushort)0, feedback.Feedback[1].Rumble?.LowFrequency);
    }

    /// <summary>Held feedback is stopped when a client controller stream is removed.</summary>
    [TestMethod]
    public void FeedbackStopsWhenClientControllerIsRemoved()
    {
        Guid clientId = Guid.NewGuid();
        FakeControllerOutputFactory factory = new();
        FakeFeedbackSink feedback = new(accept: true);
        using ControllerBroker broker = new(factory);

        broker.RegisterClient(clientId, ControllerOutput.Xbox360);
        broker.SetActiveClient(clientId);
        broker.UpdatePhysicalController(
            ControllerId,
            ControllerState.Empty,
            ControllerFeatures.None);
        broker.UpdateClientController(
            clientId,
            ControllerId,
            new ControllerState(Standard(ControllerButtons.South), null, null),
            ControllerFeatures.StandardControls | ControllerFeatures.Rumble,
            feedback);

        factory.SingleOutput.EmitFeedback(new ControllerFeedback(new ControllerRumble(10, 20)));
        broker.RemoveClientControllers(clientId);

        Assert.HasCount(2, feedback.Feedback);
        Assert.AreEqual((ushort)0, feedback.Feedback[1].Rumble?.LowFrequency);
        Assert.IsTrue(factory.SingleOutput.Disposed);
    }

    /// <summary>Feedback is not sent to endpoints that do not claim the feature.</summary>
    [TestMethod]
    public void FeedbackRequiresMatchingCapability()
    {
        Guid clientId = Guid.NewGuid();
        FakeControllerOutputFactory factory = new();
        FakeFeedbackSink feedback = new(accept: true);
        using ControllerBroker broker = new(factory);

        broker.RegisterClient(clientId, ControllerOutput.Xbox360);
        broker.SetActiveClient(clientId);
        broker.UpdatePhysicalController(
            ControllerId,
            ControllerState.Empty,
            ControllerFeatures.None);
        broker.UpdateClientController(
            clientId,
            ControllerId,
            new ControllerState(Standard(ControllerButtons.South), null, null),
            ControllerFeatures.StandardControls,
            feedback);

        factory.SingleOutput.EmitFeedback(new ControllerFeedback(new ControllerRumble(10, 20)));

        Assert.IsEmpty(feedback.Feedback);
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
        broker.UpdatePhysicalController(
            new ControllerId("physical-1"),
            ControllerState.Empty,
            ControllerFeatures.None);
        broker.UpdatePhysicalController(
            new ControllerId("physical-2"),
            ControllerState.Empty,
            ControllerFeatures.None);
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
        broker.UpdatePhysicalController(
            ControllerId,
            ControllerState.Empty,
            ControllerFeatures.None);
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
        Assert.AreEqual(1, status.Slots[0].ClientEndpointCount);
        Assert.AreEqual(ControllerFeatures.StandardControls, status.Slots[0].ActiveClientFeatures);

        broker.SetActiveClient(null);
        Assert.IsFalse(output.Disposed);
        Assert.IsTrue(broker.GetStatus().Slots[0].OutputConnected);

        broker.SetActiveClient(clientId);
        Assert.AreSame(output, factory.SingleOutput);
        Assert.IsFalse(output.Disposed);

        broker.SetControllerOutputEnabled(false);
        Assert.IsFalse(output.Disposed);
        Assert.IsTrue(broker.GetStatus().Slots[0].OutputConnected);
        Assert.AreEqual(ControllerState.Empty, output.LastState);

        broker.SetControllerOutputEnabled(true);
        Assert.HasCount(1, factory.Outputs);
        Assert.AreSame(output, factory.SingleOutput);

        broker.RemoveClient(clientId);
        Assert.IsTrue(output.Disposed);
    }

    /// <summary>Output probes bypass foreground gates so routing can detect VIIPER SDL echoes.</summary>
    [TestMethod]
    public void OutputProbeSendsToConnectedOutputs()
    {
        Guid clientId = Guid.NewGuid();
        FakeControllerOutputFactory factory = new();
        using ControllerBroker broker = new(factory);

        broker.RegisterClient(clientId, ControllerOutput.Xbox360);
        broker.UpdatePhysicalController(
            ControllerId,
            ControllerState.Empty,
            ControllerFeatures.None);
        broker.RegisterClientController(
            clientId,
            0,
            ControllerId,
            ControllerFeatures.StandardControls);

        ControllerState probe = new(
            new ControllerStandardState(ControllerButtons.DPadUp, 0, 0, 0, 0, 0, 0),
            null,
            null);

        int count = broker.SendOutputProbe(probe);

        Assert.AreEqual(1, count);
        Assert.AreEqual(ControllerButtons.DPadUp, factory.SingleOutput.LastState.Standard?.Buttons);
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

    private static FakeControllerOutput FindOutput(FakeControllerOutputFactory factory, ControllerId controllerId)
    {
        return factory.Outputs.Find(output => output.ControllerId == controllerId) ??
            throw new InvalidOperationException($"Expected output for {controllerId}.");
    }

    private static ControllerSlotStatus FindSlot(ControllerBrokerStatus status, ControllerId controllerId)
    {
        foreach (ControllerSlotStatus slot in status.Slots)
        {
            if (slot.ControllerId == controllerId)
            {
                return slot;
            }
        }

        throw new InvalidOperationException($"Expected slot for {controllerId}.");
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
