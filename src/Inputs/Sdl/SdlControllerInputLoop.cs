using System;
using System.Collections.Generic;
using System.Threading;
using SDL3;

namespace Inputs.Sdl;

/// <summary>Runs one SDL event loop for one or more connected controllers.</summary>
public static class SdlControllerInputLoop
{
    private const int EventWaitTimeoutMilliseconds = 50;

    /// <summary>Runs until cancelled.</summary>
    public static void Run(
        IReadOnlyList<SdlGamepadSource> sources,
        Action<SdlGamepadSource, GamepadInput> handler,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(handler);

        GamepadState[] previousStates = new GamepadState[sources.Count];
        bool[] hasPreviousStates = new bool[sources.Count];

        for (int i = 0; i < sources.Count; i++)
        {
            EmitCurrentState(sources[i], handler, ref hasPreviousStates[i], ref previousStates[i]);
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            if (!WaitForSdlEvent(out SDL.Event sdlEvent, cancellationToken))
            {
                continue;
            }

            bool emit = ProcessEvent(sdlEvent);
            while (SDL.PollEvent(out sdlEvent))
            {
                emit = ProcessEvent(sdlEvent) || emit;
            }

            if (emit)
            {
                EmitCurrentStates();
            }
        }

        bool ProcessEvent(SDL.Event sdlEvent)
        {
            bool emit = IsGamepadUpdateEvent(sdlEvent);
            for (int i = 0; i < sources.Count; i++)
            {
                SdlGamepadSource source = sources[i];
                _ = source.ProcessEvent(sdlEvent);
            }

            return emit;
        }

        void EmitCurrentStates()
        {
            for (int i = 0; i < sources.Count; i++)
            {
                EmitCurrentState(sources[i], handler, ref hasPreviousStates[i], ref previousStates[i]);
            }
        }
    }

    private static bool IsGamepadUpdateEvent(SDL.Event sdlEvent)
    {
        SDL.EventType eventType = (SDL.EventType)sdlEvent.Type;
        return eventType is SDL.EventType.GamepadUpdateComplete or SDL.EventType.GamepadSensorUpdate;
    }

    private static void EmitCurrentState(
        SdlGamepadSource source,
        Action<SdlGamepadSource, GamepadInput> handler,
        ref bool hasPreviousState,
        ref GamepadState previousState)
    {
        GamepadState state = source.ReadCurrentState();
        if (hasPreviousState && state == previousState)
        {
            return;
        }

        handler(source, new GamepadInput(state, source.Controller.Name));
        previousState = state;
        hasPreviousState = true;
    }

    private static bool WaitForSdlEvent(out SDL.Event sdlEvent, CancellationToken cancellationToken)
    {
        sdlEvent = default;
        cancellationToken.ThrowIfCancellationRequested();
        return SDL.WaitEventTimeout(out sdlEvent, EventWaitTimeoutMilliseconds);
    }
}
