using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using SteamInputBridge.Hosting.Server.Orchestration;
using static Vanara.PInvoke.CoreAudio;
using static Vanara.PInvoke.Ole32;

namespace SteamInputBridge;

/// <summary>Controls the default Windows capture endpoint.</summary>
internal interface IMicrophoneControl
{
    /// <summary>Raised when the observed microphone status changes.</summary>
    event Action? StatusChanged;

    /// <summary>Reads current microphone indicator state.</summary>
    MicrophoneOverlayStatus GetStatus();

    /// <summary>Sets whether the microphone should be enabled.</summary>
    void SetEnabled(bool enabled);

    /// <summary>Starts monitoring microphone status changes.</summary>
    void StartMonitoring(CancellationToken cancellationToken);
}

/// <summary>No-op microphone control used by tests and non-audio hosts.</summary>
internal sealed class NoopMicrophoneControl : IMicrophoneControl
{
    public event Action? StatusChanged;

    public MicrophoneOverlayStatus GetStatus()
    {
        return MicrophoneOverlayStatus.Unavailable;
    }

    public void SetEnabled(bool enabled)
    {
        _ = enabled;
    }

    public void StartMonitoring(CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        _ = StatusChanged;
    }
}

/// <summary>CoreAudio-backed microphone control.</summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsMicrophoneControl : IMicrophoneControl, IDisposable
{
    private static readonly TimeSpan StatusPollInterval = TimeSpan.FromMilliseconds(100);
    private readonly Lock _gate = new();
    private Timer? _monitor;
    private CancellationTokenRegistration _monitorStop;
    private MicrophoneOverlayStatus? _lastStatus;
    private int _polling;
    private bool _disposed;

    public event Action? StatusChanged;

    public MicrophoneOverlayStatus GetStatus()
    {
        IMMDevice? device = null;
        IAudioEndpointVolume? volume = null;
        try
        {
            device = OpenDefaultCaptureDevice();
            volume = Activate<IAudioEndpointVolume>(device);
            bool muted = volume.GetMute();

            try
            {
                bool inputActive = HasActiveCaptureSession(device);
                return new MicrophoneOverlayStatus(
                    Available: true,
                    Muted: muted,
                    ActivityReliable: true,
                    InputActive: !muted && inputActive);
            }
            catch (COMException)
            {
                return new MicrophoneOverlayStatus(
                    Available: true,
                    Muted: muted,
                    ActivityReliable: false,
                    InputActive: false);
            }
        }
        catch (Exception exception) when (
            exception is COMException or InvalidCastException or InvalidOperationException)
        {
            return MicrophoneOverlayStatus.Unavailable;
        }
        finally
        {
            ReleaseComObject(volume);
            ReleaseComObject(device);
        }
    }

    public void SetEnabled(bool enabled)
    {
        IMMDevice? device = null;
        IAudioEndpointVolume? volume = null;
        try
        {
            device = OpenDefaultCaptureDevice();
            volume = Activate<IAudioEndpointVolume>(device);
            Guid eventContext = Guid.Empty;
            volume.SetMute(!enabled, in eventContext);
        }
        finally
        {
            ReleaseComObject(volume);
            ReleaseComObject(device);
        }
    }

    public void StartMonitoring(CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        CancellationTokenRegistration previousMonitorStop = default;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_monitor is not null)
            {
                return;
            }

            previousMonitorStop = _monitorStop;
            _monitorStop = default;
            _lastStatus = GetStatus();
            _monitor = new Timer(PollStatus, null, StatusPollInterval, StatusPollInterval);
        }

        previousMonitorStop.Dispose();

        if (cancellationToken.CanBeCanceled)
        {
            CancellationTokenRegistration monitorStop = cancellationToken.Register(
                static state => ((WindowsMicrophoneControl)state!).StopMonitoringFromCancellation(),
                this);
            bool disposeRegistration = false;
            lock (_gate)
            {
                if (_disposed || _monitor is null)
                {
                    disposeRegistration = true;
                }
                else
                {
                    _monitorStop = monitorStop;
                }
            }

            if (disposeRegistration)
            {
                monitorStop.Dispose();
            }
        }
    }

    public void Dispose()
    {
        Timer? monitor;
        CancellationTokenRegistration monitorStop;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            monitorStop = _monitorStop;
            _monitorStop = default;
            monitor = _monitor;
            _monitor = null;
        }

        monitorStop.Dispose();
        monitor?.Dispose();
    }

    private void StopMonitoringFromCancellation()
    {
        Timer? monitor;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            monitor = _monitor;
            _monitor = null;
        }

        monitor?.Dispose();
    }

    private void PollStatus(object? state)
    {
        _ = state;
        if (Interlocked.Exchange(ref _polling, 1) != 0)
        {
            return;
        }

        try
        {
            MicrophoneOverlayStatus status = GetStatus();
            bool changed;
            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }

                changed = status != _lastStatus;
                _lastStatus = status;
            }

            if (changed)
            {
                StatusChanged?.Invoke();
            }
        }
        finally
        {
            _ = Interlocked.Exchange(ref _polling, 0);
        }
    }

    private static IMMDevice OpenDefaultCaptureDevice()
    {
        IMMDeviceEnumerator? enumerator = null;
        try
        {
            enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            try
            {
                return enumerator.GetDefaultAudioEndpoint(
                        EDataFlow.eCapture,
                        ERole.eCommunications) ??
                    throw new InvalidOperationException("Default communication capture endpoint is unavailable.");
            }
            catch (COMException)
            {
                return enumerator.GetDefaultAudioEndpoint(
                        EDataFlow.eCapture,
                        ERole.eConsole) ??
                    throw new InvalidOperationException("Default capture endpoint is unavailable.");
            }
        }
        finally
        {
            ReleaseComObject(enumerator);
        }
    }

    private static T Activate<T>(IMMDevice device)
        where T : class
    {
        Guid interfaceId = typeof(T).GUID;
        _ = device.Activate(
            in interfaceId,
            CLSCTX.CLSCTX_ALL,
            default,
            out object? instance);
        return instance as T ??
            throw new InvalidOperationException($"Could not activate CoreAudio interface {typeof(T).Name}.");
    }

    private static bool HasActiveCaptureSession(IMMDevice device)
    {
        IAudioSessionManager2? manager = null;
        IAudioSessionEnumerator? enumerator = null;
        try
        {
            manager = Activate<IAudioSessionManager2>(device);
            enumerator = manager.GetSessionEnumerator();
            int count = enumerator.GetCount();
            for (int i = 0; i < count; i++)
            {
                IAudioSessionControl? session = null;
                try
                {
                    session = enumerator.GetSession(i);
                    if (session.GetState() == AudioSessionState.AudioSessionStateActive)
                    {
                        return true;
                    }
                }
                finally
                {
                    ReleaseComObject(session);
                }
            }

            return false;
        }
        finally
        {
            ReleaseComObject(enumerator);
            ReleaseComObject(manager);
        }
    }

    private static void ReleaseComObject(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
        {
            _ = Marshal.FinalReleaseComObject(value);
        }
    }
}
