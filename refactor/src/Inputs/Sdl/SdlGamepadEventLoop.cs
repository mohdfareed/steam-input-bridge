using System;
using System.Collections.Generic;
using System.Threading;
using SDL3;
using VirtualMouse.Forwarding;

namespace VirtualMouse.Inputs.Sdl;

/// <summary>Runs one SDL event loop for a group of controller sources.</summary>
public static class SdlGamepadEventLoop
{
    private const int EventWaitTimeoutMilliseconds = 50;

    /// <summary>Runs the sources until cancellation.</summary>
    public static void Run(
        IReadOnlyList<SdlGamepadSource> sources,
        Action<SdlGamepadSource, ControllerState> handler,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(handler);

        while (!cancellationToken.IsCancellationRequested)
        {
            if (!WaitForSdlEvent(out SDL.Event sdlEvent, cancellationToken))
            {
                continue;
            }

            ProcessEvent(sdlEvent);
            while (SDL.PollEvent(out SDL.Event queuedEvent))
            {
                ProcessEvent(queuedEvent);
            }
        }

        void ProcessEvent(SDL.Event sdlEvent)
        {
            foreach (SdlGamepadSource source in sources)
            {
                if (source.ProcessEvent(sdlEvent))
                {
                    handler(source, source.ReadCurrentState());
                    break;
                }
            }
        }
    }

    private static bool WaitForSdlEvent(out SDL.Event sdlEvent, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return SDL.WaitEventTimeout(out sdlEvent, EventWaitTimeoutMilliseconds);
    }
}

/// <summary>Thrown when an SDL controller disconnects while streaming.</summary>
public sealed class SdlGamepadDisconnectedException : InvalidOperationException
{
    /// <summary>Creates a disconnect exception.</summary>
    public SdlGamepadDisconnectedException()
    {
    }

    /// <summary>Creates a disconnect exception.</summary>
    public SdlGamepadDisconnectedException(string message)
        : base(message)
    {
    }

    /// <summary>Creates a disconnect exception.</summary>
    public SdlGamepadDisconnectedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

internal static class SdlGamepadRuntime
{
    private static readonly Lock Gate = new();
    private static int _leaseCount;

    public static Lease Acquire()
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

    public sealed class Lease : IDisposable
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
