using System;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using SteamInputBridge.Inputs.Mouse;

namespace SteamInputBridge.Inputs.RawInput;

/// <summary>Windows Raw Input mouse source.</summary>
[SupportedOSPlatform("windows")]
public sealed partial class RawInputMouseSource : IMouseInputSource, IDisposable
{
    private static readonly RawInputNative.WindowProc WindowProcDelegate = HandleWindowMessage;
    private static RunState? CurrentState;
    private int _isConnected = 1;

    // MARK: Construction
    // ========================================================================

    private RawInputMouseSource()
    {
    }

    /// <summary>Creates a Raw Input mouse source.</summary>
    public static ValueTask<RawInputMouseSource> ConnectAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
#pragma warning disable CA2000 // Ownership transfers to the caller.
        return ValueTask.FromResult(new RawInputMouseSource());
#pragma warning restore CA2000
    }

    // MARK: Publics
    // ========================================================================

    /// <inheritdoc />
    public bool IsConnected => Volatile.Read(ref _isConnected) != 0;

    /// <inheritdoc />
    public void Run(MouseInputHandler handler, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(handler);
        if (!IsConnected)
        {
            throw new InvalidOperationException("Mouse input is not connected.");
        }

        RunState state = new(handler, cancellationToken);
        if (Interlocked.CompareExchange(ref CurrentState, state, null) is not null)
        {
            throw new InvalidOperationException("Another Raw Input mouse source is already running.");
        }

        nint windowHandle = CreateWindowHandle();
        using CancellationTokenRegistration registration = cancellationToken.Register(static target =>
        {
            _ = RawInputNative.PostMessage((nint)target!, RawInputNative.WmClose, nint.Zero, nint.Zero);
        }, windowHandle);

        try
        {
            RegisterRawInput(windowHandle);
            RunMessageLoop();
        }
        finally
        {
            _ = Interlocked.CompareExchange(ref CurrentState, null, state);
            if (windowHandle != nint.Zero)
            {
                _ = RawInputNative.DestroyWindow(windowHandle);
            }

            state.Dispose();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        _ = Interlocked.Exchange(ref _isConnected, 0);
        return ValueTask.CompletedTask;
    }
}

/// <summary>Creates Raw Input mouse sources.</summary>
[SupportedOSPlatform("windows")]
public sealed class RawInputMouseSourceFactory : IMouseInputSourceFactory
{
    /// <inheritdoc />
    public async ValueTask<IMouseInputSource> ConnectAsync(CancellationToken cancellationToken = default)
    {
        return await RawInputMouseSource.ConnectAsync(cancellationToken).ConfigureAwait(false);
    }
}
