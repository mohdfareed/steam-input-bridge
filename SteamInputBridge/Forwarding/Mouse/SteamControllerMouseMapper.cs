using System;
using SteamInputBridge.Inputs.Controller;
using SteamInputBridge.Inputs.Mouse;

namespace SteamInputBridge.Forwarding.Mouse;

/// <summary>Maps Steam Input virtual controller state to mouse reports.</summary>
internal sealed class SteamControllerMouseMapper
{
    private double _sensitivity;
    private MouseButtons _buttons;
    private ControllerButtons _controllerButtons;
    private short _rightX;
    private short _rightY;
    private double _remainingX;
    private double _remainingY;

    public SteamControllerMouseMapper(double sensitivity)
    {
        SetSensitivity(sensitivity);
    }

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

        // The elapsed interval belongs to the state held before this update arrived.
        _remainingX += NormalizeAxis(_rightX) * _sensitivity * elapsed.TotalSeconds;
        _remainingY += NormalizeAxis(_rightY) * _sensitivity * elapsed.TotalSeconds;
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
        _rightX = state.RightX;
        _rightY = state.RightY;
        report = new(buttons, deltaX, deltaY, wheelDelta);
        return changed;
    }

    public void Reset()
    {
        _buttons = MouseButtons.None;
        _controllerButtons = ControllerButtons.None;
        _rightX = 0;
        _rightY = 0;
        _remainingX = 0;
        _remainingY = 0;
    }

    public void SetSensitivity(double sensitivity)
    {
        if (!double.IsFinite(sensitivity) || sensitivity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sensitivity));
        }

        _sensitivity = sensitivity;
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
