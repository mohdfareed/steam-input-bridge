using System;
using System.Collections.Generic;
using SDL3;

namespace SteamInputBridge.Inputs.Sdl;

// MARK: Models
// ============================================================================

/// <summary>SDL controller source kind.</summary>
public enum SdlControllerSource
{
    /// <summary>Controller reported directly by SDL.</summary>
    Physical,

    /// <summary>Controller reported through Steam Input.</summary>
    Steam,
}

/// <summary>Stable SDL controller selector.</summary>
public readonly record struct SdlControllerId(string Value)
{
    /// <inheritdoc />
    public override string ToString()
    {
        return Value;
    }

    internal static SdlControllerId Create(SdlControllerInfo controller)
    {
        return controller.Source == SdlControllerSource.Steam && controller.SteamHandle != 0
            ? new SdlControllerId($"steam:{controller.SteamHandle:x16}")
            : !string.IsNullOrWhiteSpace(controller.Path)
            ? new SdlControllerId($"path:{controller.Path}")
            : throw new InvalidOperationException($"SDL controller \"{controller.Name}\" has no stable identity.");
    }
}

/// <summary>SDL controller discovered by the current process.</summary>
public sealed record SdlControllerInfo(
    SdlControllerId Id,
    uint InstanceId,
    string Name,
    SdlControllerSource Source,
    ulong SteamHandle,
    ushort VendorId,
    ushort ProductId,
    string? Path,
    bool HasGyro,
    bool HasAccelerometer)
{
    /// <summary>Gets whether SDL reports any motion sensor.</summary>
    public bool HasMotion => HasGyro || HasAccelerometer;
}

/// <summary>Lists and opens SDL controllers visible to the current process.</summary>
public static class SdlControllerCatalog
{
    // MARK: Publics
    // ========================================================================

    /// <summary>Lists SDL game controllers visible to this process.</summary>
    public static IReadOnlyList<SdlControllerInfo> GetControllers(
        Func<SdlControllerInfo, bool>? shouldInclude = null)
    {
        return WithSdlErrors(() =>
        {
            using SdlGamepadRuntime.Lease _ = SdlGamepadRuntime.Acquire();
            uint[] gamepadIds = SDL.GetGamepads(out int count) ?? [];
            return Filter(CreateControllerInfos(gamepadIds, count), shouldInclude);
        });
    }

    /// <summary>Lists and opens SDL controllers while keeping SDL initialized.</summary>
    public static IReadOnlyList<SdlGamepadSource> OpenControllers(
        Func<IReadOnlyList<SdlControllerInfo>, IReadOnlyList<SdlControllerInfo>> selectControllers)
    {
        ArgumentNullException.ThrowIfNull(selectControllers);

        return WithSdlErrors<IReadOnlyList<SdlGamepadSource>>(() =>
        {
            using SdlGamepadRuntime.Lease lease = SdlGamepadRuntime.Acquire();
            uint[] gamepadIds = SDL.GetGamepads(out int count) ?? [];
            IReadOnlyList<SdlControllerInfo> controllers = CreateControllerInfos(gamepadIds, count);
            List<SdlGamepadSource> sources = [];

            try
            {
                foreach (SdlControllerInfo controller in selectControllers(controllers))
                {
                    sources.Add(SdlGamepadSource.Connect(controller));
                }

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
        });
    }

    /// <summary>Opens all client-visible controllers except VIIPER loopback devices.</summary>
    public static IReadOnlyList<SdlGamepadSource> OpenClientControllers(
        Func<SdlControllerInfo, bool>? shouldInclude = null)
    {
        return OpenControllers(
            static controller => controller.Source is SdlControllerSource.Steam or SdlControllerSource.Physical,
            shouldInclude);
    }

    /// <summary>Opens physical controllers visible to the server process.</summary>
    public static IReadOnlyList<SdlGamepadSource> OpenPhysicalControllers(
        Func<SdlControllerInfo, bool>? shouldInclude = null)
    {
        return OpenControllers(
            static controller => controller.Source == SdlControllerSource.Physical,
            shouldInclude);
    }

    /// <summary>Gets the physical slot id used by forwarding.</summary>
    public static string GetPhysicalControllerId(SdlControllerInfo controller)
    {
        ArgumentNullException.ThrowIfNull(controller);
        return !string.IsNullOrWhiteSpace(controller.Path)
            ? $"path:{controller.Path}"
            : $"vidpid:{controller.VendorId:x4}:{controller.ProductId:x4}";
    }

    // MARK: Internals
    // ========================================================================

    internal static InvalidOperationException CreateSdlUnavailableException(Exception exception)
    {
        return new InvalidOperationException(
            "SDL3 runtime is not available. Restore SDL3-CS.Native or put SDL3.dll next to the app.",
            exception);
    }

    internal static nint OpenGamepad(SdlControllerInfo controller)
    {
        ArgumentNullException.ThrowIfNull(controller);
        return SDL.OpenGamepad(controller.InstanceId);
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

    // MARK: Privates
    // ========================================================================

    private static IReadOnlyList<SdlGamepadSource> OpenControllers(
        Func<SdlControllerInfo, bool> shouldOpen,
        Func<SdlControllerInfo, bool>? shouldInclude)
    {
        return WithSdlErrors<IReadOnlyList<SdlGamepadSource>>(() =>
        {
            using SdlGamepadRuntime.Lease lease = SdlGamepadRuntime.Acquire();
            uint[] gamepadIds = SDL.GetGamepads(out int count) ?? [];
            List<SdlGamepadSource> sources = [];

            try
            {
                foreach (SdlControllerInfo controller in CreateControllerInfos(gamepadIds, count))
                {
                    if (shouldOpen(controller) && (shouldInclude?.Invoke(controller) ?? true))
                    {
                        sources.Add(SdlGamepadSource.Connect(controller));
                    }
                }

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
        });
    }

    private static IReadOnlyList<SdlControllerInfo> Filter(
        IReadOnlyList<SdlControllerInfo> controllers,
        Func<SdlControllerInfo, bool>? shouldInclude)
    {
        if (shouldInclude is null)
        {
            return controllers;
        }

        List<SdlControllerInfo> filtered = [];
        foreach (SdlControllerInfo controller in controllers)
        {
            if (shouldInclude(controller))
            {
                filtered.Add(controller);
            }
        }

        return filtered;
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
