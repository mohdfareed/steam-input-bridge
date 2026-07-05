using System.Collections.Generic;
using SDL3;

namespace SteamInputBridge.Inputs.Sdl;

/// <summary>Supported controller stream reported by SDL through Steam Input.</summary>
public sealed record SdlSteamControllerInfo(
    uint InstanceId,
    string Name,
    ulong SteamHandle,
    ushort VendorId,
    ushort ProductId);

/// <summary>Lists supported Steam Input controller streams visible to the current process.</summary>
public static class SdlSteamControllerCatalog
{
    private const ushort SteamVendorId = 0x28DE;
    private const ushort SteamControllerProductId = 0x1302;
    private const ushort EightBitDoVendorId = 0x2DC8;
    private const ushort EightBitDoUltimate2WirelessProductId = 0x6012;

    /// <summary>Lists supported controller streams reported through Steam Input.</summary>
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
                if (!IsSupportedSteamInputController(steamHandle, vendorId, productId))
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

    internal static bool IsSupportedSteamInputController(ulong steamHandle, ushort vendorId, ushort productId)
    {
        return steamHandle != 0 &&
            ((vendorId == SteamVendorId && productId == SteamControllerProductId) ||
             (vendorId == EightBitDoVendorId && productId == EightBitDoUltimate2WirelessProductId));
    }
}
