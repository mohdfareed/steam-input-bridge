using System;
using System.Threading;
using System.Threading.Tasks;

namespace SteamInputBridge.Inputs.Mouse;

/// <summary>Connected mouse input source.</summary>
public interface IMouseInputSource : IAsyncDisposable
{
    /// <summary>Gets whether the source is connected.</summary>
    bool IsConnected { get; }

    /// <summary>Runs the source until cancelled.</summary>
    void Run(MouseInputHandler handler, CancellationToken cancellationToken = default);
}

/// <summary>Creates mouse input sources.</summary>
public interface IMouseInputSourceFactory
{
    /// <summary>Connects a mouse input source.</summary>
    ValueTask<IMouseInputSource> ConnectAsync(CancellationToken cancellationToken = default);
}
