using System.Collections.Generic;
using SDL3;

namespace SteamInputBridge.Inputs.Sdl;

/// <summary>Steam Controller stream reported by SDL through Steam Input.</summary>
public sealed record SdlSteamControllerInfo(
    uint InstanceId,
    string Name,
    ulong SteamHandle,
    ushort VendorId,
    ushort ProductId);

/// <summary>Lists Steam Controller streams visible to the current process.</summary>
public static class SdlSteamControllerCatalog
{
    private const ushort SteamVendorId = 0x28DE;
    private const ushort SteamControllerProductId = 0x1302;

    /// <summary>Lists Steam Controller streams reported through Steam Input.</summary>
    public static IReadOnlyList<SdlSteamControllerInfo> GetControllers()
    {
        SdlGamepadRuntime.EnsureInitialized();
        uint[] gamepadIds = SDL.GetGamepads(out int count) ?? [];
        List<SdlSteamControllerInfo> controllers = new(count);

        for (int i = 0; i < count; i++)
        {
            uint id = gamepadIds[i];
            nint gamepad = SDL.OpenGamepad(id);
            if (gamepad == 0)
            {
                continue;
            }

            try
            {
                ulong steamHandle = SDL.GetGamepadSteamHandle(gamepad);
                ushort vendorId = SDL.GetGamepadVendor(gamepad);
                ushort productId = SDL.GetGamepadProduct(gamepad);
                if (steamHandle == 0 ||
                    vendorId != SteamVendorId ||
                    productId != SteamControllerProductId)
                {
                    continue;
                }

                controllers.Add(new(
                    id,
                    SDL.GetGamepadName(gamepad) ?? SDL.GetGamepadNameForID(id) ?? $"SDL gamepad {id}",
                    steamHandle,
                    vendorId,
                    productId));
            }
            finally
            {
                SDL.CloseGamepad(gamepad);
            }
        }

        return controllers;
    }
}
