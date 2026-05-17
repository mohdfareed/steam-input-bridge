using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using SDL3;

namespace Inputs.Sdl;

public sealed partial class SdlGamepadSource
{
    private const int EventWaitTimeoutMilliseconds = 50;

    internal static void RunAll(
        IReadOnlyList<SdlGamepadSource> inputs,
        Action<SdlGamepadSource, GamepadInput> handler,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(handler);

        GamepadState[] previousStates = new GamepadState[inputs.Count];
        bool[] hasPreviousStates = new bool[inputs.Count];

        for (int i = 0; i < inputs.Count; i++)
        {
            EmitCurrentState(inputs[i], handler, ref hasPreviousStates[i], ref previousStates[i]);
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            if (!WaitForSdlEvent(out SDL.Event sdlEvent, cancellationToken))
            {
                continue;
            }

            ProcessEvent(sdlEvent);
            while (SDL.PollEvent(out sdlEvent))
            {
                ProcessEvent(sdlEvent);
            }
        }

        void ProcessEvent(SDL.Event sdlEvent)
        {
            for (int i = 0; i < inputs.Count; i++)
            {
                SdlGamepadSource input = inputs[i];
                if (input.ProcessEvent(sdlEvent))
                {
                    EmitCurrentState(input, handler, ref hasPreviousStates[i], ref previousStates[i]);
                }
            }
        }
    }

    internal void Run(
        GamepadInputHandler handler,
        Action<long, long>? timingHandler,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(handler);
        if (!IsConnected || _gamepad == 0)
        {
            throw new InvalidOperationException("SDL gamepad source is not connected.");
        }

        bool hasPreviousState = false;
        GamepadState previousState = default;

        EmitCurrentState(Stopwatch.GetTimestamp());
        while (!cancellationToken.IsCancellationRequested)
        {
            if (!WaitForSdlEvent(out SDL.Event sdlEvent, cancellationToken))
            {
                continue;
            }

            ProcessEventAndEmit(sdlEvent);
            while (SDL.PollEvent(out sdlEvent))
            {
                ProcessEventAndEmit(sdlEvent);
            }
        }

        void ProcessEventAndEmit(SDL.Event sdlEvent)
        {
            if (ProcessEvent(sdlEvent))
            {
                EmitCurrentState(Stopwatch.GetTimestamp());
            }
        }

        void EmitCurrentState(long startedTimestamp)
        {
            GamepadState state = ReadCurrentState();

            if (hasPreviousState && state == previousState)
            {
                return;
            }

            long emittedTimestamp = Stopwatch.GetTimestamp();
            timingHandler?.Invoke(startedTimestamp, emittedTimestamp);
            GamepadInput input = new(state, DeviceName);
            handler(in input);
            previousState = state;
            hasPreviousState = true;
        }
    }

    private bool ProcessEvent(SDL.Event sdlEvent)
    {
        SDL.EventType eventType = (SDL.EventType)sdlEvent.Type;
        if (eventType == SDL.EventType.GamepadUpdateComplete &&
            sdlEvent.GDevice.Which == InstanceId)
        {
            return true;
        }

        if (eventType == SDL.EventType.GamepadSensorUpdate &&
            IsMotionEvent(sdlEvent.GSensor.Which, InstanceId, MotionInstanceId))
        {
            UpdateMotion(sdlEvent.GSensor);
            return true;
        }

        if (eventType == SDL.EventType.GamepadRemoved &&
            sdlEvent.GDevice.Which == InstanceId)
        {
            _ = Interlocked.Exchange(ref _isConnected, 0);
            throw new InvalidOperationException($"SDL gamepad \"{DeviceName}\" was disconnected.");
        }

        if (eventType == SDL.EventType.GamepadRemoved &&
            MotionInstanceId.HasValue &&
            sdlEvent.GDevice.Which == MotionInstanceId.Value)
        {
            _ = Interlocked.Exchange(ref _isConnected, 0);
            throw new InvalidOperationException($"SDL motion gamepad \"{MotionDeviceName}\" was disconnected.");
        }

        return false;
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

        handler(source, new GamepadInput(state, source.DeviceName));
        previousState = state;
        hasPreviousState = true;
    }

    private GamepadState ReadCurrentState()
    {
        return ReadState(
            _gamepad,
            MotionEnabled && HasGyro,
            MotionEnabled && HasAccelerometer,
            _gyroData,
            _accelerometerData);
    }

    internal static bool IsMotionEvent(uint instanceId, uint primaryInstanceId, uint? motionInstanceId)
    {
        return motionInstanceId.HasValue
            ? instanceId == motionInstanceId.Value
            : instanceId == primaryInstanceId;
    }

    private unsafe void UpdateMotion(SDL.GamepadSensorEvent sensorEvent)
    {
        ReadOnlySpan<float> data = new(sensorEvent.Data, SensorValueCount);

        SDL.SensorType sensor = (SDL.SensorType)sensorEvent.Sensor;
        if (sensor == SDL.SensorType.Gyro)
        {
            data.CopyTo(_gyroData);
        }
        else if (sensor == SDL.SensorType.Accel)
        {
            data.CopyTo(_accelerometerData);
        }
    }

    private static bool WaitForSdlEvent(out SDL.Event sdlEvent, CancellationToken cancellationToken)
    {
        sdlEvent = default;
        cancellationToken.ThrowIfCancellationRequested();
        return SDL.WaitEventTimeout(out sdlEvent, EventWaitTimeoutMilliseconds);
    }
}
