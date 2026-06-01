using System;
using System.Threading;
using System.Threading.Tasks;
using SteamInputBridge.Inputs.Mouse;

namespace SteamInputBridge.Outputs.Mouse;

/// <summary>Connected mouse output.</summary>
public interface IMouseOutput : IAsyncDisposable
{
    /// <summary>Gets whether the output is connected.</summary>
    bool IsConnected { get; }

    /// <summary>Sends one mouse input report.</summary>
    ValueTask SendAsync(in MouseInput input, CancellationToken cancellationToken = default);

    /// <summary>Clears any held output state.</summary>
    ValueTask ClearAsync(CancellationToken cancellationToken = default);
}

/// <summary>Creates game-facing mouse outputs.</summary>
public interface IMouseOutputFactory
{
    /// <summary>Connects a mouse output.</summary>
    ValueTask<IMouseOutput> ConnectAsync(MouseOutput output, CancellationToken cancellationToken = default);
}
