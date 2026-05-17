using System;
using System.Collections.Generic;
using SDL3;

namespace Inputs.Sdl;

public sealed partial class SdlGamepadSource
{
    private static SdlGamepadSource Connect(SdlGamepadOptions options)
    {
        bool initialized = false;
        nint gamepad = 0;
        nint motionGamepad = 0;

        try
        {
            SdlGamepadDiscovery.InitializeGamepads();
            initialized = true;

            uint[] gamepadIds = SDL.GetGamepads(out int count) ?? [];
            if (count <= 0)
            {
                throw new InvalidOperationException("No SDL gamepads were found.");
            }

            IReadOnlyList<SdlGamepadInfo> gamepads = SdlGamepadDiscovery.GetGamepadInfos(gamepadIds, count);
            SdlGamepadInfo primary = GetGamepadInfo(gamepads, options.DeviceIndex);
            gamepad = SdlGamepadDiscovery.OpenGamepad(gamepadIds, count, options.DeviceIndex);
            if (gamepad == 0)
            {
                throw new InvalidOperationException($"Could not open SDL gamepad: {SDL.GetError()}");
            }

            SdlMotionDeviceSelection motion = ResolveMotionDevice(gamepads, primary, options);

            if (motion.UsesSecondaryDevice && motion.DeviceIndex.HasValue)
            {
                motionGamepad = SdlGamepadDiscovery.OpenGamepad(gamepadIds, count, motion.DeviceIndex.Value);
                if (motionGamepad == 0)
                {
                    throw new InvalidOperationException($"Could not open SDL motion gamepad: {SDL.GetError()}");
                }
            }

            nint motionHandle = motion.UsesSecondaryDevice ? motionGamepad : gamepad;
            bool hasGyro = motion.Enabled && EnableSensor(motionHandle, SDL.SensorType.Gyro);
            bool hasAccelerometer = motion.Enabled && EnableSensor(motionHandle, SDL.SensorType.Accel);

            nint connectedGamepad = gamepad;
            nint connectedMotionGamepad = motionGamepad;
            gamepad = 0;
            motionGamepad = 0;
            initialized = false;

            return new SdlGamepadSource(
                connectedGamepad,
                primary,
                connectedMotionGamepad,
                motion.UsesSecondaryDevice ? motion.Device : null,
                hasGyro,
                hasAccelerometer);
        }
        finally
        {
            if (motionGamepad != 0)
            {
                SDL.CloseGamepad(motionGamepad);
            }

            if (gamepad != 0)
            {
                SDL.CloseGamepad(gamepad);
            }

            if (initialized)
            {
                SDL.QuitSubSystem(SDL.InitFlags.Gamepad);
            }
        }
    }

    internal static SdlMotionDeviceSelection ResolveMotionDevice(
        IReadOnlyList<SdlGamepadInfo> gamepads,
        SdlGamepadInfo primary,
        SdlGamepadOptions options)
    {
        ArgumentNullException.ThrowIfNull(gamepads);
        ArgumentNullException.ThrowIfNull(primary);
        ArgumentNullException.ThrowIfNull(options);

        return options.MotionDeviceIndex.HasValue
            ? ResolveMotionOverride(gamepads, primary, options.MotionDeviceIndex.Value)
            : primary.HasMotion
                ? new SdlMotionDeviceSelection(true, primary.Index, primary, UsesSecondaryDevice: false)
                : default;
    }

    internal static int ResolveMotionDeviceIndex(
        IReadOnlyList<SdlGamepadInfo> gamepads,
        SdlGamepadInfo primary,
        SdlGamepadOptions options)
    {
        SdlMotionDeviceSelection selection = ResolveMotionDevice(gamepads, primary, options);
        return selection.DeviceIndex ??
            throw new InvalidOperationException("No matching SDL motion gamepad was found.");
    }

    private static SdlMotionDeviceSelection ResolveMotionOverride(
        IReadOnlyList<SdlGamepadInfo> gamepads,
        SdlGamepadInfo primary,
        int deviceIndex)
    {
        SdlGamepadInfo device = GetGamepadInfo(gamepads, deviceIndex);
        return device.HasMotion
            ? new SdlMotionDeviceSelection(
            true,
            device.Index,
            device,
            UsesSecondaryDevice: device.Index != primary.Index)
            : throw new InvalidOperationException(
                $"SDL motion gamepad index {deviceIndex} does not expose gyro or accelerometer sensors.");
    }

    private static SdlGamepadInfo GetGamepadInfo(IReadOnlyList<SdlGamepadInfo> gamepads, int deviceIndex)
    {
        foreach (SdlGamepadInfo gamepad in gamepads)
        {
            if (gamepad.Index == deviceIndex)
            {
                return gamepad;
            }
        }

        throw new ArgumentOutOfRangeException(
            nameof(deviceIndex),
            $"SDL gamepad index {deviceIndex} was not found.");
    }

    private static void ValidateOptions(SdlGamepadOptions options)
    {
        if (options.DeviceIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "SDL gamepad index must be non-negative.");
        }

        if (options.MotionDeviceIndex is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "SDL motion gamepad index must be non-negative.");
        }
    }

    private static bool EnableSensor(nint gamepad, SDL.SensorType sensor)
    {
        return gamepad != 0 &&
            SDL.GamepadHasSensor(gamepad, sensor) &&
            (SDL.GamepadSensorEnabled(gamepad, sensor) ||
            SDL.SetGamepadSensorEnabled(gamepad, sensor, enabled: true));
    }
}
