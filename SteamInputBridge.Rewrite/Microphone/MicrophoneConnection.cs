using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;

namespace SteamInputBridge.Microphone;

/// <summary>Tracks status for one connected microphone device.</summary>
[SupportedOSPlatform("windows")]
internal sealed class MicrophoneConnection : IDisposable
{
    private readonly Lock _gate = new();
    private readonly MicrophoneDevice _device;
    private MicrophoneStatus _status = MicrophoneStatus.Unavailable;

    // MARK: Lifecycle
    // ========================================================================

    private MicrophoneConnection(MicrophoneDevice device)
    {
        _device = device;
    }

    public event Action<MicrophoneStatus>? StatusChanged;

    // MARK: Publics
    // ========================================================================

    public MicrophoneStatus Status
    {
        get
        {
            lock (_gate)
            {
                return _status;
            }
        }
    }

    public static MicrophoneConnection Open()
    {
        return new MicrophoneConnection(MicrophoneDevice.Open());
    }

    public bool TryReadStatus(out MicrophoneStatus status)
    {
        try
        {
            status = _device.ReadStatus();
        }
        catch (Exception exception) when (IsConnectionFailure(exception))
        {
            status = MicrophoneStatus.Unavailable;
            Publish(status);
            return false;
        }

        Publish(status);
        return true;
    }

    public void SetEnabled(bool enabled)
    {
        _device.SetEnabled(enabled);
    }

    public void Dispose()
    {
        _device.Dispose();
    }

    // MARK: Implementation
    // ========================================================================

    private void Publish(MicrophoneStatus status)
    {
        bool changed;
        lock (_gate)
        {
            changed = status != _status;
            _status = status;
        }

        if (changed)
        {
            StatusChanged?.Invoke(status);
        }
    }

    internal static bool IsConnectionFailure(Exception exception)
    {
        return exception is COMException or InvalidCastException or InvalidOperationException or ObjectDisposedException;
    }
}
