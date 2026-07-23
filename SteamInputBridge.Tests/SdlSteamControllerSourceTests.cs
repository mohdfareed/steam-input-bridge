using SDL3;
using SteamInputBridge.Inputs.Controller;
using SteamInputBridge.Inputs.Sdl;

namespace SteamInputBridge.Tests;

[TestClass]
public sealed class SdlSteamControllerSourceTests
{
    private const uint InstanceId = 42;
    private static readonly SDL.GamepadButton[] MappedButtons =
    [
        SDL.GamepadButton.South,
        SDL.GamepadButton.East,
        SDL.GamepadButton.West,
        SDL.GamepadButton.North,
        SDL.GamepadButton.Back,
        SDL.GamepadButton.Guide,
        SDL.GamepadButton.Start,
        SDL.GamepadButton.LeftStick,
        SDL.GamepadButton.RightStick,
        SDL.GamepadButton.LeftShoulder,
        SDL.GamepadButton.RightShoulder,
        SDL.GamepadButton.DPadUp,
        SDL.GamepadButton.DPadDown,
        SDL.GamepadButton.DPadLeft,
        SDL.GamepadButton.DPadRight,
    ];

    [TestMethod]
    public void CompletedUpdatesPreserveDistinctQueuedStatesAndTimestamps()
    {
        SdlSteamControllerSource.EventState events = new(InstanceId, ControllerState.Empty);

        Assert.IsFalse(events.TryRead(Axis(SDL.GamepadAxis.LeftX, -100), out _, out _));
        Assert.IsFalse(events.TryRead(Axis(SDL.GamepadAxis.LeftY, 200), out _, out _));
        Assert.IsFalse(events.TryRead(Axis(SDL.GamepadAxis.RightX, 10_000), out _, out _));
        Assert.IsFalse(events.TryRead(Axis(SDL.GamepadAxis.RightY, -400), out _, out _));
        Assert.IsFalse(events.TryRead(Axis(SDL.GamepadAxis.LeftTrigger, 500), out _, out _));
        Assert.IsFalse(events.TryRead(Axis(SDL.GamepadAxis.RightTrigger, 600), out _, out _));
        foreach (SDL.GamepadButton button in MappedButtons)
        {
            Assert.IsFalse(events.TryRead(Button(button, pressed: true), out _, out _));
        }

        Assert.IsTrue(events.TryRead(Completed(timestamp: 1_000), out ControllerState first, out ulong firstAt));

        Assert.IsFalse(events.TryRead(Axis(SDL.GamepadAxis.RightX, -20_000), out _, out _));
        Assert.IsFalse(events.TryRead(Button(SDL.GamepadButton.South, pressed: false), out _, out _));
        Assert.IsTrue(events.TryRead(Completed(timestamp: 2_000), out ControllerState second, out ulong secondAt));

        ControllerButtons allButtons =
            ControllerButtons.South |
            ControllerButtons.East |
            ControllerButtons.West |
            ControllerButtons.North |
            ControllerButtons.Back |
            ControllerButtons.Guide |
            ControllerButtons.Start |
            ControllerButtons.LeftStick |
            ControllerButtons.RightStick |
            ControllerButtons.LeftShoulder |
            ControllerButtons.RightShoulder |
            ControllerButtons.DPadUp |
            ControllerButtons.DPadDown |
            ControllerButtons.DPadLeft |
            ControllerButtons.DPadRight;
        Assert.AreEqual(new ControllerState(allButtons, -100, 200, 10_000, -400, 500, 600), first);
        Assert.AreEqual((ulong)1_000, firstAt);
        Assert.AreEqual(-20_000, second.RightX);
        Assert.AreEqual(allButtons & ~ControllerButtons.South, second.Buttons);
        Assert.AreEqual(-100, second.LeftX);
        Assert.AreEqual(200, second.LeftY);
        Assert.AreEqual(-400, second.RightY);
        Assert.AreEqual(500, second.LeftTrigger);
        Assert.AreEqual(600, second.RightTrigger);
        Assert.AreEqual((ulong)2_000, secondAt);
    }

    [TestMethod]
    public void EventsForAnotherControllerDoNotChangeTheSnapshot()
    {
        ControllerState initial = ControllerState.Empty with { RightY = 1234 };
        SdlSteamControllerSource.EventState events = new(InstanceId, initial);

        Assert.IsFalse(events.TryRead(Axis(SDL.GamepadAxis.RightY, -5678, InstanceId + 1), out _, out _));
        Assert.IsFalse(events.TryRead(Completed(timestamp: 10, InstanceId + 1), out _, out _));
        Assert.IsTrue(events.TryRead(Completed(timestamp: 20), out ControllerState state, out ulong timestamp));

        Assert.AreEqual(initial, state);
        Assert.AreEqual((ulong)20, timestamp);
    }

    [TestMethod]
    public void TriggerEventsClampNegativeValuesWithoutChangingOtherState()
    {
        ControllerState initial = ControllerState.Empty with
        {
            Buttons = ControllerButtons.North,
            LeftTrigger = 10,
        };
        SdlSteamControllerSource.EventState events = new(InstanceId, initial);

        Assert.IsFalse(events.TryRead(Axis(SDL.GamepadAxis.LeftTrigger, short.MinValue), out _, out _));
        Assert.IsTrue(events.TryRead(Completed(timestamp: 30), out ControllerState state, out _));

        Assert.AreEqual(0, state.LeftTrigger);
        Assert.AreEqual(ControllerButtons.North, state.Buttons);
    }

    private static SDL.Event Axis(SDL.GamepadAxis axis, short value, uint instanceId = InstanceId)
    {
        return new SDL.Event
        {
            GAxis = new SDL.GamepadAxisEvent
            {
                Type = SDL.EventType.GamepadAxisMotion,
                Which = instanceId,
                Axis = (byte)axis,
                Value = value,
            },
        };
    }

    private static SDL.Event Button(SDL.GamepadButton button, bool pressed, uint instanceId = InstanceId)
    {
        return new SDL.Event
        {
            GButton = new SDL.GamepadButtonEvent
            {
                Type = pressed ? SDL.EventType.GamepadButtonDown : SDL.EventType.GamepadButtonUp,
                Which = instanceId,
                Button = (byte)button,
            },
        };
    }

    private static SDL.Event Completed(ulong timestamp, uint instanceId = InstanceId)
    {
        return new SDL.Event
        {
            GDevice = new SDL.GamepadDeviceEvent
            {
                Type = SDL.EventType.GamepadUpdateComplete,
                Timestamp = timestamp,
                Which = instanceId,
            },
        };
    }
}
