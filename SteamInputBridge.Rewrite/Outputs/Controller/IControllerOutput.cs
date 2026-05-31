using System;
using SteamInputBridge.Inputs.Controller;

namespace SteamInputBridge.Outputs.Controller;

/// <summary>Game-facing controller output.</summary>
public interface IControllerOutput : IAsyncDisposable
{
    /// <summary>Raised when the game sends rumble feedback.</summary>
    event EventHandler<ControllerRumbleEventArgs>? RumbleReceived;

    /// <summary>Gets whether the output is connected.</summary>
    bool IsConnected { get; }

    /// <summary>Sends one controller state.</summary>
    void Send(in ControllerState state);

    /// <summary>Clears held output state.</summary>
    void Clear()
    {
        ControllerState state = ControllerState.Empty;
        Send(in state);
    }
}

/// <summary>Controller rumble event data.</summary>
public sealed class ControllerRumbleEventArgs(ControllerRumble rumble) : EventArgs
{
    /// <summary>Rumble feedback.</summary>
    public ControllerRumble Rumble { get; } = rumble;
}
