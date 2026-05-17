using System;
using System.Collections.Generic;
using Inputs.Sdl;

namespace Hosting;

internal static class SdlGamepadMotionSelector
{
    private const ushort ValveVendorId = 0x28de;
    private const ushort SteamControllerSteamInputProductId = 0x1302;
    private const ushort SteamControllerPhysicalProductId = 0x1304;

    public static SdlGamepadOptions ResolveOptions(
        IReadOnlyList<SdlGamepadInfo> gamepads,
        SdlGamepadOptions options)
    {
        ArgumentNullException.ThrowIfNull(gamepads);
        ArgumentNullException.ThrowIfNull(options);

        SdlGamepadInfo primary = GetGamepad(gamepads, options.DeviceIndex);
        if (options.MotionDeviceIndex.HasValue)
        {
            SdlGamepadInfo motion = GetGamepad(gamepads, options.MotionDeviceIndex.Value);
            EnsureMotionDevice(motion);
            return options;
        }

        return !primary.HasMotion &&
            primary.IsSteamInput &&
            TryFindSteamPhysicalMotionCounterpart(gamepads, primary, out SdlGamepadInfo motionDevice)
            ? options with
            {
                MotionDeviceIndex = motionDevice.Index,
            }
            : options;
    }

    public static bool TryFindSteamPhysicalMotionCounterpart(
        IReadOnlyList<SdlGamepadInfo> gamepads,
        SdlGamepadInfo steamGamepad,
        out SdlGamepadInfo motionDevice)
    {
        ArgumentNullException.ThrowIfNull(gamepads);
        ArgumentNullException.ThrowIfNull(steamGamepad);

        motionDevice = default!;
        int matchCount = 0;
        foreach (SdlGamepadInfo gamepad in gamepads)
        {
            if (gamepad.IsSteamInput ||
                !gamepad.HasMotion ||
                !IsStrictSteamPhysicalMatch(steamGamepad, gamepad))
            {
                continue;
            }

            motionDevice = gamepad;
            matchCount++;
        }

        return matchCount == 1;
    }

    public static SdlGamepadInfo GetGamepad(IReadOnlyList<SdlGamepadInfo> gamepads, int deviceIndex)
    {
        foreach (SdlGamepadInfo gamepad in gamepads)
        {
            if (gamepad.Index == deviceIndex)
            {
                return gamepad;
            }
        }

        throw new InvalidOperationException($"SDL gamepad index {deviceIndex} is not available.");
    }

    private static void EnsureMotionDevice(SdlGamepadInfo gamepad)
    {
        if (gamepad.IsSteamInput)
        {
            throw new InvalidOperationException(
                $"SDL motion gamepad index {gamepad.Index} is not a physical SDL gamepad.");
        }

        if (!gamepad.HasMotion)
        {
            throw new InvalidOperationException(
                $"SDL motion gamepad index {gamepad.Index} does not expose gyro or accelerometer sensors.");
        }
    }

    private static bool IsStrictSteamPhysicalMatch(SdlGamepadInfo steamGamepad, SdlGamepadInfo physicalGamepad)
    {
        return steamGamepad.VendorId == ValveVendorId &&
            steamGamepad.ProductId == SteamControllerSteamInputProductId

            ? physicalGamepad.VendorId == ValveVendorId &&
                physicalGamepad.ProductId == SteamControllerPhysicalProductId &&
                IsSameName(steamGamepad.Name, physicalGamepad.Name)
            : steamGamepad.VendorId != 0 &&

            steamGamepad.ProductId != 0 &&
            physicalGamepad.VendorId == steamGamepad.VendorId &&
            physicalGamepad.ProductId == steamGamepad.ProductId &&
            IsSameName(steamGamepad.Name, physicalGamepad.Name);
    }

    private static bool IsSameName(string left, string right)
    {
        return string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}
