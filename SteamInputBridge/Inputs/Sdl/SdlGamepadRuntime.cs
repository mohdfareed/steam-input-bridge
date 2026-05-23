using System;
using System.Threading;
using SDL3;

namespace SteamInputBridge.Inputs.Sdl;

internal static class SdlGamepadRuntime
{
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
            if (!_initialized && !SDL.Init(Features))
            {
                throw new InvalidOperationException($"Could not initialize SDL: {SDL.GetError()}");
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
