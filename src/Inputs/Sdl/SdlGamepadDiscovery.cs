using System;
using System.Collections.Generic;
using SDL3;

namespace Inputs.Sdl;

internal static class SdlGamepadDiscovery
{
    public static IReadOnlyList<SdlGamepadInfo> GetGamepads()
    {
        try
        {
            InitializeGamepads();
            uint[] gamepadIds = SDL.GetGamepads(out int count) ?? [];
            try
            {
                return gamepadIds.Length == 0 && count > 0
                    ? throw new InvalidOperationException($"Could not list SDL gamepads: {SDL.GetError()}")
                    : GetGamepadInfos(gamepadIds, count);
            }
            finally
            {
                SDL.QuitSubSystem(SDL.InitFlags.Gamepad);
            }
        }
        catch (DllNotFoundException exception)
        {
            throw CreateSdlUnavailableException(exception);
        }
        catch (EntryPointNotFoundException exception)
        {
            throw CreateSdlUnavailableException(exception);
        }
    }

    public static List<SdlGamepadInfo> GetGamepadInfos(uint[] gamepadIds, int count)
    {
        List<SdlGamepadInfo> gamepads = new(count);
        for (int i = 0; i < count; i++)
        {
            uint instanceId = gamepadIds[i];
            nint gamepad = SDL.OpenGamepad(instanceId);

            try
            {
                gamepads.Add(gamepad == 0
                    ? CreateGamepadInfoForId(gamepads.Count, instanceId)
                    : CreateGamepadInfo(gamepads.Count, instanceId, gamepad));
            }
            finally
            {
                if (gamepad != 0)
                {
                    SDL.CloseGamepad(gamepad);
                }
            }
        }

        return gamepads;
    }

    public static nint OpenGamepad(uint[] gamepadIds, int count, int deviceIndex)
    {
        return deviceIndex >= count
            ? throw new ArgumentOutOfRangeException(
                nameof(deviceIndex),
                $"SDL gamepad index {deviceIndex} was not found.")
            : SDL.OpenGamepad(gamepadIds[deviceIndex]);
    }

    public static void InitializeGamepads()
    {
        _ = SDL.SetHint(SDL.Hints.JoystickAllowBackgroundEvents, "1");
        if (!SDL.Init(SDL.InitFlags.Gamepad))
        {
            throw new InvalidOperationException($"Could not initialize SDL gamepad input: {SDL.GetError()}");
        }
    }

    public static SdlGamepadInfo CreateGamepadInfo(int index, uint instanceId, nint gamepad)
    {
        string name =
            SDL.GetGamepadName(gamepad) ??
            SDL.GetGamepadNameForID(instanceId) ??
            $"SDL gamepad {instanceId}";

        return new SdlGamepadInfo(
            index,
            instanceId,
            name,
            SDL.GetGamepadSteamHandle(gamepad),
            SDL.GetGamepadVendor(gamepad),
            SDL.GetGamepadProduct(gamepad),
            SDL.GetGamepadPath(gamepad) ?? SDL.GetGamepadPathForID(instanceId))
        {
            HasGyro = SDL.GamepadHasSensor(gamepad, SDL.SensorType.Gyro),
            HasAccelerometer = SDL.GamepadHasSensor(gamepad, SDL.SensorType.Accel),
        };
    }

    public static InvalidOperationException CreateSdlUnavailableException(Exception exception)
    {
        return new InvalidOperationException(
            "SDL3 runtime is not available. Restore SDL3-CS.Native or put SDL3.dll next to the app.",
            exception);
    }

    private static SdlGamepadInfo CreateGamepadInfoForId(int index, uint instanceId)
    {
        string name =
            SDL.GetGamepadNameForID(instanceId) ??
            $"SDL gamepad {instanceId}";

        return new SdlGamepadInfo(
            index,
            instanceId,
            name,
            0,
            SDL.GetGamepadVendorForID(instanceId),
            SDL.GetGamepadProductForID(instanceId),
            SDL.GetGamepadPathForID(instanceId));
    }
}
