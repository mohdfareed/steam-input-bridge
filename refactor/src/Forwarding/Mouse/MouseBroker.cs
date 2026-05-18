using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace VirtualMouse.Forwarding;

/// <summary>Routes mouse input through the active-client output gate.</summary>
public sealed class MouseBroker(IMouseOutputFactory outputFactory) : IDisposable, IAsyncDisposable
{
    private readonly Lock _gate = new();
    private readonly Dictionary<Guid, MouseOutput> _clients = [];
    private Guid? _activeClientId;
    private bool _mouseOutputEnabled = true;
    private IMouseOutput? _output;
    private MouseOutput _outputKind;
    private bool _disposed;

    // MARK: API
    // ========================================================================

    /// <summary>Registers a connected client and the mouse output its profile wants.</summary>
    public void RegisterClient(Guid clientId, MouseOutput mouseOutput)
    {
        ThrowIfDisposed();
        IMouseOutput? dispose;
        lock (_gate)
        {
            _clients[clientId] = mouseOutput;
            dispose = RefreshOutput();
        }

        DisposeOutput(dispose);
    }

    /// <summary>Removes a client and releases output it owned.</summary>
    public void RemoveClient(Guid clientId)
    {
        ThrowIfDisposed();
        IMouseOutput? dispose;
        lock (_gate)
        {
            _ = _clients.Remove(clientId);
            if (_activeClientId == clientId)
            {
                _activeClientId = null;
            }

            dispose = RefreshOutput();
        }

        DisposeOutput(dispose);
    }

    /// <summary>Sets the active client whose profile may drive mouse output.</summary>
    public void SetActiveClient(Guid? clientId)
    {
        ThrowIfDisposed();
        IMouseOutput? dispose;
        lock (_gate)
        {
            _activeClientId = clientId.HasValue && _clients.ContainsKey(clientId.Value)
                ? clientId
                : null;
            dispose = RefreshOutput();
        }

        DisposeOutput(dispose);
    }

    /// <summary>Enables or disables all mouse output without disconnecting clients.</summary>
    public void SetMouseOutputEnabled(bool enabled)
    {
        ThrowIfDisposed();
        IMouseOutput? dispose;
        lock (_gate)
        {
            _mouseOutputEnabled = enabled;
            dispose = RefreshOutput();
        }

        DisposeOutput(dispose);
    }

    /// <summary>Forwards one mouse report when the active profile has mouse output.</summary>
    public void Send(in MouseInput input)
    {
        ThrowIfDisposed();
        IMouseOutput? output;
        lock (_gate)
        {
            output = _output;
        }

        if (output is not null && !input.Report.IsEmpty)
        {
            output.SendAsync(input.Report).AsTask().GetAwaiter().GetResult();
        }
    }

    /// <summary>Gets mouse forwarding status.</summary>
    public MouseBrokerStatus GetStatus()
    {
        ThrowIfDisposed();
        lock (_gate)
        {
            return new MouseBrokerStatus(
                _activeClientId,
                _mouseOutputEnabled,
                _output is not null,
                _outputKind);
        }
    }

    /// <summary>Disconnects mouse output.</summary>
    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    /// <summary>Disconnects mouse output.</summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        IMouseOutput? dispose;
        lock (_gate)
        {
            dispose = _output;
            _output = null;
            _outputKind = MouseOutput.None;
            _clients.Clear();
        }

        if (dispose is not null)
        {
            await dispose.DisposeAsync().ConfigureAwait(false);
        }
    }

    // MARK: Helpers
    // ========================================================================

    private IMouseOutput? RefreshOutput()
    {
        MouseOutput outputKind = GetActiveOutputKind();
        bool shouldConnect = _mouseOutputEnabled && outputKind != MouseOutput.None;
        if (!shouldConnect)
        {
            return DisconnectOutput();
        }

        if (_output is null || _outputKind != outputKind)
        {
            IMouseOutput? dispose = DisconnectOutput();
            _output = outputFactory.Connect(outputKind);
            _outputKind = outputKind;
            return dispose;
        }

        return null;
    }

    private IMouseOutput? DisconnectOutput()
    {
        IMouseOutput? output = _output;
        _output = null;
        _outputKind = MouseOutput.None;
        return output;
    }

    private MouseOutput GetActiveOutputKind()
    {
        return _activeClientId.HasValue &&
            _clients.TryGetValue(_activeClientId.Value, out MouseOutput output)
            ? output
            : MouseOutput.None;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private static void DisposeOutput(IMouseOutput? output)
    {
        output?.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}

/// <summary>Mouse output shape.</summary>
public enum MouseOutput
{
    /// <summary>No mouse output.</summary>
    None,

    /// <summary>VIIPER virtual mouse output.</summary>
    Viiper,

    /// <summary>Teensy hardware mouse output.</summary>
    Teensy,
}

/// <summary>Creates game-facing mouse outputs.</summary>
public interface IMouseOutputFactory
{
    /// <summary>Connects a mouse output.</summary>
    IMouseOutput Connect(MouseOutput output);
}

/// <summary>Current mouse forwarding status.</summary>
public sealed record MouseBrokerStatus(
    Guid? ActiveClientId,
    bool MouseOutputEnabled,
    bool OutputConnected,
    MouseOutput Output);
