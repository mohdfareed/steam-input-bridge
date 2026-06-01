using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using static Vanara.PInvoke.CoreAudio;
using static Vanara.PInvoke.Ole32;

namespace SteamInputBridge.Microphone;

/// <summary>Owns the CoreAudio objects for the current microphone endpoint.</summary>
[SupportedOSPlatform("windows")]
internal sealed class MicrophoneDevice : IDisposable
{
    private readonly Lock _gate = new();
    private IMMDevice? _device;
    private IAudioEndpointVolume? _volume;
    private IAudioSessionManager2? _sessionManager;

    // MARK: Lifecycle
    // ========================================================================

    private MicrophoneDevice(IMMDevice device, IAudioEndpointVolume volume, IAudioSessionManager2 sessionManager)
    {
        _device = device;
        _volume = volume;
        _sessionManager = sessionManager;
    }

    // MARK: Publics
    // ========================================================================

    public static MicrophoneDevice Open()
    {
        IMMDevice? device = null;
        IAudioEndpointVolume? volume = null;
        IAudioSessionManager2? sessionManager = null;

        try
        {
            device = OpenDefaultCaptureDevice();
            volume = Activate<IAudioEndpointVolume>(device);
            sessionManager = Activate<IAudioSessionManager2>(device);
            return new MicrophoneDevice(device, volume, sessionManager);
        }
        catch
        {
            ReleaseComObject(sessionManager);
            ReleaseComObject(volume);
            ReleaseComObject(device);
            throw;
        }
    }

    public MicrophoneStatus ReadStatus()
    {
        lock (_gate)
        {
            IAudioEndpointVolume volume = _volume ?? throw new ObjectDisposedException(nameof(MicrophoneDevice));
            bool muted = volume.GetMute();
            bool inputActive = HasActiveCaptureSession(_sessionManager);
            return new MicrophoneStatus(Available: true, Muted: muted, IsActive: !muted && inputActive);
        }
    }

    public void SetEnabled(bool enabled)
    {
        lock (_gate)
        {
            IAudioEndpointVolume volume = _volume ?? throw new ObjectDisposedException(nameof(MicrophoneDevice));
            Guid eventContext = Guid.Empty;
            volume.SetMute(!enabled, in eventContext);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            ReleaseComObject(_sessionManager);
            ReleaseComObject(_volume);
            ReleaseComObject(_device);

            _sessionManager = null;
            _volume = null;
            _device = null;
        }
    }

    // MARK: CoreAudio
    // ========================================================================

    private static IMMDevice OpenDefaultCaptureDevice()
    {
        IMMDeviceEnumerator? enumerator = null;
        try
        {
            enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            try
            {
                return enumerator.GetDefaultAudioEndpoint(EDataFlow.eCapture, ERole.eCommunications) ??
                    throw new InvalidOperationException("Default communication capture endpoint is unavailable.");
            }
            catch (COMException)
            {
                return enumerator.GetDefaultAudioEndpoint(EDataFlow.eCapture, ERole.eConsole) ??
                    throw new InvalidOperationException("Default capture endpoint is unavailable.");
            }
        }
        finally
        {
            ReleaseComObject(enumerator);
        }
    }

    private static T Activate<T>(IMMDevice device) where T : class
    {
        Guid interfaceId = typeof(T).GUID;
        _ = device.Activate(in interfaceId, CLSCTX.CLSCTX_ALL, default, out object? instance);
        return instance as T ?? throw new InvalidOperationException($"Could not activate CoreAudio interface {typeof(T).Name}.");
    }

    private static bool HasActiveCaptureSession(IAudioSessionManager2? manager)
    {
        if (manager is null)
        {
            return false;
        }

        IAudioSessionEnumerator? enumerator = null;
        try
        {
            enumerator = manager.GetSessionEnumerator();
            for (int i = 0; i < enumerator.GetCount(); i++)
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
