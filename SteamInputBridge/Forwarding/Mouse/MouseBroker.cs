using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SteamInputBridge.Forwarding.Mouse;

// MARK: Models
// ============================================================================

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
    bool PointerOutputEnabled,
    bool OutputConnected,
    MouseOutput Output);

/// <summary>Routes mouse input through the active-client output gate.</summary>
public sealed class MouseBroker(IMouseOutputFactory outputFactory) : IDisposable, IAsyncDisposable
{
    private readonly Lock _gate = new();
    private readonly Dictionary<Guid, MouseOutput> _clients = [];
    private Guid? _activeClientId;
    private bool _mouseOutputEnabled = true;
    private bool _pointerOutputEnabled = true;
    private IMouseOutput? _output;
    private MouseOutput _outputKind;
    private bool _disposed;

    // MARK: Publics
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

    /// <summary>Enables or disables pointer reports without disconnecting the output device.</summary>
    public void SetPointerOutputEnabled(bool enabled)
    {
        ThrowIfDisposed();
        IMouseOutput? output = null;

        lock (_gate)
        {
            if (_pointerOutputEnabled == enabled)
            {
                return;
            }

            _pointerOutputEnabled = enabled;
            if (!enabled)
            {
                output = _output;
            }
        }

        if (output is not null)
        {
            ValueTask release = output.SendAsync(MouseReport.Empty);
            if (!release.IsCompletedSuccessfully)
            {
                _ = ObserveSendAsync(release);
            }
        }
    }

    /// <summary>Forwards one mouse report when the active profile has mouse output.</summary>
    public void Send(in MouseInput input)
    {
        ThrowIfDisposed();
        IMouseOutput? output;

        lock (_gate)
        {
            output = _pointerOutputEnabled && HasActiveOutput() ? _output : null;
        }

        if (output is not null && !input.Report.IsEmpty && !output.FilterInput(in input))
        {
            ValueTask send = output.SendAsync(input.Report);
            if (!send.IsCompletedSuccessfully)
            {
                _ = ObserveSendAsync(send);
            }
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
                _pointerOutputEnabled,
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

    // MARK: Privates
    // ========================================================================

    private IMouseOutput? RefreshOutput()
    {
        MouseOutput outputKind = GetOutputKind(keepExistingWhenInactive: true);
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

    private MouseOutput GetOutputKind(bool keepExistingWhenInactive)
    {
        if (_activeClientId.HasValue &&
            _clients.TryGetValue(_activeClientId.Value, out MouseOutput activeOutput) &&
            activeOutput != MouseOutput.None)
        {
            return activeOutput;
        }

        if (keepExistingWhenInactive && _outputKind != MouseOutput.None && HasOutputClient())
        {
            return _outputKind;
        }

        foreach (MouseOutput output in _clients.Values)
        {
            if (output != MouseOutput.None)
            {
                return output;
            }
        }

        return MouseOutput.None;
    }

    private bool HasOutputClient()
    {
        foreach (MouseOutput output in _clients.Values)
        {
            if (output != MouseOutput.None)
            {
                return true;
            }
        }

        return false;
    }

    private bool HasActiveOutput()
    {
        return _activeClientId.HasValue &&
            _clients.TryGetValue(_activeClientId.Value, out MouseOutput activeOutput) &&
            activeOutput != MouseOutput.None;
    }

    private static async Task ObserveSendAsync(ValueTask send)
    {
        try
        {
            await send.ConfigureAwait(false);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or ObjectDisposedException)
        {
        }
    }

    // MARK: Disposal
    // ========================================================================

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private static void DisposeOutput(IMouseOutput? output)
    {
        output?.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
