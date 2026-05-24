using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SteamInputBridge.Forwarding.Mouse;

/// <summary>Routes mouse input through the active-client output gate.</summary>
public sealed class MouseBroker(IMouseOutputFactory outputFactory) : IDisposable, IAsyncDisposable
{
    private readonly Lock _gate = new();
    private readonly Dictionary<Guid, MouseOutput> _clients = [];
    private Guid? _activeClientId;
    private bool _mouseOutputEnabled = true;
    private bool _pointerOutputEnabled = true;
    private IMouseOutput? _output;
    private IMouseOutput? _sendOutput;
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
            UpdateSendOutput();
        }

        DisposeOutput(dispose);
    }

    /// <summary>Removes a client and releases output it owned.</summary>
    public void RemoveClient(Guid clientId)
    {
        ThrowIfDisposed();
        IMouseOutput? dispose;
        IMouseOutput? clear;

        lock (_gate)
        {
            _ = _clients.Remove(clientId);
            if (_activeClientId == clientId)
            {
                _activeClientId = null;
            }

            dispose = RefreshOutput();
            clear = _output is not null && !HasActiveOutput() ? _output : null;
            UpdateSendOutput();
        }

        SendEmpty(clear);
        DisposeOutput(dispose);
    }

    /// <summary>Sets the active client whose profile may drive mouse output.</summary>
    public void SetActiveClient(Guid? clientId)
    {
        ThrowIfDisposed();
        IMouseOutput? dispose;
        IMouseOutput? clear;

        lock (_gate)
        {
            _activeClientId = clientId.HasValue && _clients.ContainsKey(clientId.Value)
                ? clientId
                : null;
            dispose = RefreshOutput();
            clear = _output is not null && !HasActiveOutput() ? _output : null;
            UpdateSendOutput();
        }

        SendEmpty(clear);
        DisposeOutput(dispose);
    }

    /// <summary>Enables or disables all mouse output without disconnecting clients.</summary>
    public void SetMouseOutputEnabled(bool enabled)
    {
        ThrowIfDisposed();
        IMouseOutput? clear;

        lock (_gate)
        {
            _mouseOutputEnabled = enabled;
            clear = !enabled ? _output : null;
            UpdateSendOutput();
        }

        SendEmpty(clear);
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
            UpdateSendOutput();
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
        IMouseOutput? output = Volatile.Read(ref _sendOutput);

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
                _outputKind,
                GetClientStatuses());
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
            Volatile.Write(ref _sendOutput, null);
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
        // Output enable/disable is a report gate, not a device lifecycle
        // gate. Keep the virtual mouse connected for the profile run so
        // Windows and Steam do not see device churn.
        bool shouldConnect = outputKind != MouseOutput.None;

        if (!shouldConnect)
        {
            return DisconnectOutput();
        }

        if (_output is null || _outputKind != outputKind)
        {
            IMouseOutput? dispose = DisconnectOutput();
            _output = outputFactory.Connect(outputKind);
            _outputKind = outputKind;
            ValueTask send = _output.SendAsync(MouseReport.Empty);
            if (!send.IsCompletedSuccessfully)
            {
                _ = ObserveSendAsync(send);
            }

            return dispose;
        }

        return null;
    }

    private IMouseOutput? DisconnectOutput()
    {
        IMouseOutput? output = _output;
        _output = null;
        Volatile.Write(ref _sendOutput, null);
        _outputKind = MouseOutput.None;
        return output;
    }

    private void UpdateSendOutput()
    {
        // Raw Input can run at 1000 Hz. Lifecycle changes keep this cached so
        // the report path does not take the broker lock for every packet.
        Volatile.Write(
            ref _sendOutput,
            _mouseOutputEnabled && _pointerOutputEnabled && HasActiveOutput() ? _output : null);
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

    private List<MouseClientStatus> GetClientStatuses()
    {
        List<MouseClientStatus> clients = [];
        foreach (KeyValuePair<Guid, MouseOutput> client in _clients)
        {
            if (client.Value != MouseOutput.None)
            {
                clients.Add(new MouseClientStatus(client.Key, client.Value));
            }
        }

        return clients;
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

    private static void SendEmpty(IMouseOutput? output)
    {
        if (output is null)
        {
            return;
        }

        ValueTask send = output.SendAsync(MouseReport.Empty);
        if (!send.IsCompletedSuccessfully)
        {
            _ = ObserveSendAsync(send);
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
