using System;
using System.Threading;
using SDL3;

namespace SteamInputBridge.Inputs.Sdl;

internal static class SdlGamepadRuntime
{
    private static readonly Lock Gate = new();
    private static int _leaseCount;

    internal static Lease Acquire()
    {
        lock (Gate)
        {
            if (_leaseCount == 0 && !SDL.Init(SDL.InitFlags.Gamepad | SDL.InitFlags.Events | SDL.InitFlags.Sensor))
            {
                throw new InvalidOperationException($"Could not initialize SDL: {SDL.GetError()}");
            }

            _leaseCount++;
            return new Lease();
        }
    }

    internal sealed class Lease : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            lock (Gate)
            {
                _leaseCount--;
                if (_leaseCount == 0)
                {
                    SDL.QuitSubSystem(SDL.InitFlags.Gamepad | SDL.InitFlags.Events | SDL.InitFlags.Sensor);
                }
            }
        }
    }
}
