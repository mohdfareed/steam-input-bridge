using System;
using System.Collections.Generic;
using SDL3;

namespace Inputs.Sdl;

/// <summary>Lists SDL controllers visible to the current process.</summary>
public static class SdlControllerCatalog
{
    /// <summary>Lists SDL game controllers visible to this process.</summary>
    public static IReadOnlyList<SdlControllerInfo> GetControllers()
    {
        return WithSdlErrors(() =>
        {
            using SdlGamepadRuntime.Lease _ = SdlGamepadRuntime.Acquire();
            uint[] gamepadIds = SDL.GetGamepads(out int count) ?? [];
            return CreateControllerInfos(gamepadIds, count);
        });
    }

    /// <summary>Opens all Steam Input controllers visible to a client process.</summary>
    public static IReadOnlyList<SdlGamepadSource> OpenSteamControllers()
    {
        return WithSdlErrors<IReadOnlyList<SdlGamepadSource>>(() =>
        {
            SdlGamepadRuntime.Lease? lease = null;
            List<SdlGamepadSource> sources = [];
            try
            {
                lease = SdlGamepadRuntime.Acquire();
                uint[] gamepadIds = SDL.GetGamepads(out int count) ?? [];
                for (int i = 0; i < count; i++)
                {
                    nint gamepad = SDL.OpenGamepad(gamepadIds[i]);
                    SdlGamepadRuntime.Lease? sourceLease = null;
                    try
                    {
                        if (gamepad == 0)
                        {
                            continue;
                        }

                        SdlControllerInfo controller = CreateOpenControllerInfo(gamepadIds[i], gamepad);
                        if (!ShouldOpenSteamController(controller))
                        {
                            continue;
                        }

                        sourceLease = SdlGamepadRuntime.Acquire();
                        sources.Add(SdlGamepadSource.AdoptOpenGamepad(gamepad, controller, sourceLease));
                        sourceLease = null;
                        gamepad = 0;
                    }
                    finally
                    {
                        if (gamepad != 0)
                        {
                            SDL.CloseGamepad(gamepad);
                        }

                        sourceLease?.Dispose();
                    }
                }

                lease = null;
                return [.. sources];
            }
            catch
            {
                foreach (SdlGamepadSource source in sources)
                {
                    source.Dispose();
                }

                throw;
            }
            finally
            {
                lease?.Dispose();
            }
        });
    }

    internal static IReadOnlyList<SdlControllerInfo> CreateControllerInfos(uint[] gamepadIds, int count)
    {
        List<SdlControllerInfo> controllers = new(count);
        for (int i = 0; i < count; i++)
        {
            uint instanceId = gamepadIds[i];
            nint gamepad = SDL.OpenGamepad(instanceId);
            try
            {
                controllers.Add(gamepad == 0
                    ? CreateClosedControllerInfo(instanceId)
                    : CreateOpenControllerInfo(instanceId, gamepad));
            }
            finally
            {
                if (gamepad != 0)
                {
                    SDL.CloseGamepad(gamepad);
                }
            }
        }

        return controllers;
    }

    internal static nint OpenGamepad(SdlControllerInfo controller)
    {
        ArgumentNullException.ThrowIfNull(controller);
        return SDL.OpenGamepad(controller.InstanceId);
    }

    internal static bool ShouldOpenSteamController(SdlControllerInfo controller)
    {
        ArgumentNullException.ThrowIfNull(controller);
        return controller.Source == SdlControllerSource.Steam && controller.SteamHandle != 0;
    }

    internal static InvalidOperationException CreateSdlUnavailableException(Exception exception)
    {
        return new InvalidOperationException(
            "SDL3 runtime is not available. Restore SDL3-CS.Native or put SDL3.dll next to the app.",
            exception);
    }

    internal static SdlControllerInfo ResolveController(
        IReadOnlyList<SdlControllerInfo> controllers,
        SdlControllerId id)
    {
        SdlControllerInfo? match = null;
        foreach (SdlControllerInfo controller in controllers)
        {
            if (!Matches(controller, id))
            {
                continue;
            }

            if (match is not null)
            {
                throw new InvalidOperationException($"SDL controller {id} matches multiple devices.");
            }

            match = controller;
        }

        return match ?? throw new InvalidOperationException($"SDL controller {id} was not found.");
    }

    private static SdlControllerInfo CreateOpenControllerInfo(uint instanceId, nint gamepad)
    {
        string name =
            SDL.GetGamepadNameForID(instanceId) ??
            SDL.GetGamepadName(gamepad) ??
            $"SDL gamepad {instanceId}";
        ulong steamHandle = SDL.GetGamepadSteamHandle(gamepad);
        SdlControllerInfo controller = new(
            default,
            instanceId,
            name,
            steamHandle == 0 ? SdlControllerSource.Physical : SdlControllerSource.Steam,
            steamHandle,
            SDL.GetGamepadVendor(gamepad),
            SDL.GetGamepadProduct(gamepad),
            SDL.GetGamepadPath(gamepad) ?? SDL.GetGamepadPathForID(instanceId),
            SDL.GamepadHasSensor(gamepad, SDL.SensorType.Gyro),
            SDL.GamepadHasSensor(gamepad, SDL.SensorType.Accel));
        return controller with { Id = SdlControllerId.Create(controller) };
    }

    private static SdlControllerInfo CreateClosedControllerInfo(uint instanceId)
    {
        SdlControllerInfo controller = new(
            default,
            instanceId,
            SDL.GetGamepadNameForID(instanceId) ?? $"SDL gamepad {instanceId}",
            SdlControllerSource.Physical,
            0,
            SDL.GetGamepadVendorForID(instanceId),
            SDL.GetGamepadProductForID(instanceId),
            SDL.GetGamepadPathForID(instanceId),
            HasGyro: false,
            HasAccelerometer: false);
        return controller with { Id = SdlControllerId.Create(controller) };
    }

    private static bool Matches(SdlControllerInfo controller, SdlControllerId id)
    {
        return string.Equals(controller.Id.Value, id.Value, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(controller.Name, id.Value, StringComparison.OrdinalIgnoreCase) ||
            (controller.Source == SdlControllerSource.Steam &&
                string.Equals($"{controller.Name} (steam)", id.Value, StringComparison.OrdinalIgnoreCase));
    }

    private static T WithSdlErrors<T>(Func<T> action)
    {
        try
        {
            return action();
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
}
