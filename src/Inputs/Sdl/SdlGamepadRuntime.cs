using System;
using System.Threading;
using SDL3;

namespace Inputs.Sdl;

internal static class SdlGamepadRuntime
{
    private static int references;

    public static Lease Acquire()
    {
        if (Interlocked.Increment(ref references) == 1)
        {
            _ = SDL.SetHint(SDL.Hints.JoystickAllowBackgroundEvents, "1");
            if (!SDL.Init(SDL.InitFlags.Gamepad))
            {
                _ = Interlocked.Decrement(ref references);
                throw new InvalidOperationException($"Could not initialize SDL gamepad input: {SDL.GetError()}");
            }
        }

        return new Lease();
    }

    public sealed class Lease : IDisposable
    {
        private int disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0)
            {
                return;
            }

            if (Interlocked.Decrement(ref references) == 0)
            {
                SDL.QuitSubSystem(SDL.InitFlags.Gamepad);
            }
        }
    }
}

