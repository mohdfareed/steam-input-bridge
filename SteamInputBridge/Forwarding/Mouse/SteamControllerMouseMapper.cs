using System;
using SteamInputBridge.Inputs.Controller;
using SteamInputBridge.Inputs.Mouse;

namespace SteamInputBridge.Forwarding.Mouse;

/// <summary>Maps Steam Input virtual controller state to mouse reports.</summary>
internal sealed class SteamControllerMouseMapper
{
    // Steam controls the stick amplitude; this is only the linear stick-to-pixel conversion.
    private const double MouseSensitivity = 4_000.0; // Pixels per second at full stick.

    private MouseButtons _buttons;
    private ControllerButtons _controllerButtons;
    private double _remainingX;
    private double _remainingY;

    public bool TryMap(in ControllerState state, TimeSpan elapsed, out MouseReport report)
    {
        MouseButtons buttons = MouseButtons.None;
        if (state.RightTrigger > 0)
        {
            buttons |= MouseButtons.Left;
        }

        if (state.LeftTrigger > 0)
        {
            buttons |= MouseButtons.Right;
        }

        if ((state.Buttons & ControllerButtons.RightStick) != 0)
        {
            buttons |= MouseButtons.Middle;
        }

        _remainingX += NormalizeAxis(state.RightX) * MouseSensitivity * elapsed.TotalSeconds;
        _remainingY += NormalizeAxis(state.RightY) * MouseSensitivity * elapsed.TotalSeconds;
        int deltaX = (int)_remainingX;
        int deltaY = (int)_remainingY;
        _remainingX -= deltaX;
        _remainingY -= deltaY;

        int wheelDelta = 0;
        if (Pressed(state.Buttons, ControllerButtons.RightShoulder) &&
            !Pressed(_controllerButtons, ControllerButtons.RightShoulder))
        {
            wheelDelta--;
        }

        if (Pressed(state.Buttons, ControllerButtons.LeftShoulder) &&
            !Pressed(_controllerButtons, ControllerButtons.LeftShoulder))
        {
            wheelDelta++;
        }

        bool changed = buttons != _buttons || deltaX != 0 || deltaY != 0 || wheelDelta != 0;
        _buttons = buttons;
        _controllerButtons = state.Buttons;
        report = new(buttons, deltaX, deltaY, wheelDelta);
        return changed;
    }

    public void Reset()
    {
        _buttons = MouseButtons.None;
        _controllerButtons = ControllerButtons.None;
        _remainingX = 0;
        _remainingY = 0;
    }

    private static double NormalizeAxis(short value)
    {
        return value < 0 ? value / 32768.0 : value / 32767.0;
    }

    private static bool Pressed(ControllerButtons buttons, ControllerButtons button)
    {
        return (buttons & button) != 0;
    }
}
