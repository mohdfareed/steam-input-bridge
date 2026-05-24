using System;
using System.Threading;
using SDL3;

namespace SteamInputBridge.Inputs.Sdl;

internal static class SdlGamepadRuntime
{
    private const string SteamHidApiHint = "SDL_JOYSTICK_HIDAPI_STEAM";

    private const SDL.InitFlags Features =
        SDL.InitFlags.Gamepad |
        SDL.InitFlags.Events |
        SDL.InitFlags.Sensor;

    private static readonly Lock Gate = new();
    private static bool _initialized;

    internal static Lease Acquire()
    {
        lock (Gate)
        {
            if (!_initialized)
            {
                // SDL defaults the Steam Controller HIDAPI driver off. Enable
                // it before SDL init so direct physical reads can expose
                // advanced Steam Controller features such as touchpads.
                _ = SDL.SetHintWithPriority(
                    SteamHidApiHint,
                    "1",
                    SDL.HintPriority.Override);

                if (!SDL.Init(Features))
                {
                    throw new InvalidOperationException($"Could not initialize SDL: {SDL.GetError()}");
                }
            }

            _initialized = true;
            return new Lease();
        }
    }

    internal sealed class Lease : IDisposable
    {
        public void Dispose() { }
    }
}
