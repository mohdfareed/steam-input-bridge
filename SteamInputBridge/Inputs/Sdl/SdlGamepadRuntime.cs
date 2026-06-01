using System;
using System.Threading;
using SDL3;

namespace SteamInputBridge.Inputs.Sdl;

internal static class SdlGamepadRuntime
{
    private const string SteamHidApiHint = "SDL_JOYSTICK_HIDAPI_STEAM";

    private const SDL.InitFlags Features =
        SDL.InitFlags.Gamepad |
        SDL.InitFlags.Joystick |
        SDL.InitFlags.Events;

    private static readonly Lock Gate = new();
    private static bool _initialized;

    public static void EnsureInitialized()
    {
        lock (Gate)
        {
            if (_initialized)
            {
                return;
            }

            _ = SDL.SetHintWithPriority(SteamHidApiHint, "1", SDL.HintPriority.Override);
            if (!SDL.Init(Features))
            {
                throw new InvalidOperationException($"Could not initialize SDL: {SDL.GetError()}");
            }

            _initialized = true;
        }
    }
}
