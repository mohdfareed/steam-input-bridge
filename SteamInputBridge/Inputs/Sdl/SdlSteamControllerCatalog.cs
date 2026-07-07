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
    private const ushort MirroredXbox360VendorId = 0x045E;
    private const ushort MirroredXbox360ProductId = 0x028E;

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
            (vendorId != MirroredXbox360VendorId || productId != MirroredXbox360ProductId);
    }
}
