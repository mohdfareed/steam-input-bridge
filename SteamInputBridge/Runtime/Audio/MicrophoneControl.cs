using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using SteamInputBridge.Hosting.Server.Orchestration;

namespace SteamInputBridge.Runtime.Audio;

/// <summary>Controls the default Windows capture endpoint.</summary>
internal interface IMicrophoneControl
{
    /// <summary>Reads current microphone indicator state.</summary>
    MicrophoneOverlayStatus GetStatus();

    /// <summary>Sets whether the microphone should be enabled.</summary>
    void SetEnabled(bool enabled);
}

/// <summary>No-op microphone control used by tests and non-audio hosts.</summary>
internal sealed class NoopMicrophoneControl : IMicrophoneControl
{
    public MicrophoneOverlayStatus GetStatus()
    {
        return MicrophoneOverlayStatus.Unavailable;
    }

    public void SetEnabled(bool enabled)
    {
        _ = enabled;
    }
}

/// <summary>CoreAudio-backed microphone control.</summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsMicrophoneControl : IMicrophoneControl
{
    private const uint ClsctxAll = 0x17;
    private static readonly Guid AudioEndpointVolumeId =
        new("5CDF2C82-841E-4546-9722-0CF74078229A");
    private static readonly Guid AudioSessionManager2Id =
        new("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F");

    public MicrophoneOverlayStatus GetStatus()
    {
        object? device = null;
        object? volumeObject = null;
        try
        {
            device = OpenDefaultCaptureDevice();
            volumeObject = Activate(device, AudioEndpointVolumeId);
            IAudioEndpointVolume volume = (IAudioEndpointVolume)volumeObject;
            ThrowIfFailed(volume.GetMute(out bool muted));

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
            ReleaseComObject(volumeObject);
            ReleaseComObject(device);
        }
    }

    public void SetEnabled(bool enabled)
    {
        object? device = null;
        object? volumeObject = null;
        try
        {
            device = OpenDefaultCaptureDevice();
            volumeObject = Activate(device, AudioEndpointVolumeId);
            IAudioEndpointVolume volume = (IAudioEndpointVolume)volumeObject;
            Guid eventContext = Guid.Empty;
            ThrowIfFailed(volume.SetMute(!enabled, ref eventContext));
        }
        finally
        {
            ReleaseComObject(volumeObject);
            ReleaseComObject(device);
        }
    }

    private static object OpenDefaultCaptureDevice()
    {
        object? enumeratorObject = null;
        try
        {
            enumeratorObject = new MMDeviceEnumerator();
            IMMDeviceEnumerator enumerator = (IMMDeviceEnumerator)enumeratorObject;
            int result = enumerator.GetDefaultAudioEndpoint(
                EDataFlow.Capture,
                ERole.Communications,
                out IMMDevice device);
            if (result != 0)
            {
                result = enumerator.GetDefaultAudioEndpoint(
                    EDataFlow.Capture,
                    ERole.Console,
                    out device);
            }

            ThrowIfFailed(result);
            return device;
        }
        finally
        {
            ReleaseComObject(enumeratorObject);
        }
    }

    private static object Activate(
        object deviceObject,
        Guid interfaceId)
    {
        IMMDevice device = (IMMDevice)deviceObject;
        ThrowIfFailed(device.Activate(
            ref interfaceId,
            ClsctxAll,
            IntPtr.Zero,
            out object instance));
        return instance;
    }

    private static bool HasActiveCaptureSession(object device)
    {
        object? managerObject = null;
        object? enumeratorObject = null;
        try
        {
            managerObject = Activate(device, AudioSessionManager2Id);
            IAudioSessionManager2 manager = (IAudioSessionManager2)managerObject;
            ThrowIfFailed(manager.GetSessionEnumerator(out IAudioSessionEnumerator enumerator));
            enumeratorObject = enumerator;
            ThrowIfFailed(enumerator.GetCount(out int count));
            for (int i = 0; i < count; i++)
            {
                object? sessionObject = null;
                try
                {
                    ThrowIfFailed(enumerator.GetSession(i, out IAudioSessionControl session));
                    sessionObject = session;
                    ThrowIfFailed(session.GetState(out AudioSessionState state));
                    if (state == AudioSessionState.Active)
                    {
                        return true;
                    }
                }
                finally
                {
                    ReleaseComObject(sessionObject);
                }
            }

            return false;
        }
        finally
        {
            ReleaseComObject(enumeratorObject);
            ReleaseComObject(managerObject);
        }
    }

    private static void ThrowIfFailed(int result)
    {
        if (result < 0)
        {
            Marshal.ThrowExceptionForHR(result);
        }
    }

    private static void ReleaseComObject(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
        {
            _ = Marshal.FinalReleaseComObject(value);
        }
    }

    private enum EDataFlow
    {
        Render,
        Capture,
    }

    private enum ERole
    {
        Console,
        Multimedia,
        Communications,
    }

    private enum AudioSessionState
    {
        Inactive,
        Active,
        Expired,
    }

    [ComImport]
    [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private sealed class MMDeviceEnumerator
    {
    }

    [ComImport]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig]
        int EnumAudioEndpoints();

        [PreserveSig]
        int GetDefaultAudioEndpoint(
            EDataFlow dataFlow,
            ERole role,
            out IMMDevice endpoint);
    }

    [ComImport]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig]
        int Activate(
            ref Guid interfaceId,
            uint classContext,
            IntPtr activationParams,
            [MarshalAs(UnmanagedType.IUnknown)] out object instance);
    }

    [ComImport]
    [Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        [PreserveSig]
        int RegisterControlChangeNotify(IntPtr notify);

        [PreserveSig]
        int UnregisterControlChangeNotify(IntPtr notify);

        [PreserveSig]
        int GetChannelCount(out uint channelCount);

        [PreserveSig]
        int SetMasterVolumeLevel(float level, ref Guid eventContext);

        [PreserveSig]
        int SetMasterVolumeLevelScalar(float level, ref Guid eventContext);

        [PreserveSig]
        int GetMasterVolumeLevel(out float level);

        [PreserveSig]
        int GetMasterVolumeLevelScalar(out float level);

        [PreserveSig]
        int SetChannelVolumeLevel(uint channel, float level, ref Guid eventContext);

        [PreserveSig]
        int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid eventContext);

        [PreserveSig]
        int GetChannelVolumeLevel(uint channel, out float level);

        [PreserveSig]
        int GetChannelVolumeLevelScalar(uint channel, out float level);

        [PreserveSig]
        int SetMute(
            [MarshalAs(UnmanagedType.Bool)] bool muted,
            ref Guid eventContext);

        [PreserveSig]
        int GetMute([MarshalAs(UnmanagedType.Bool)] out bool muted);
    }

    [ComImport]
    [Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionManager2
    {
        [PreserveSig]
        int GetAudioSessionControl(
            ref Guid audioSessionGuid,
            uint streamFlags,
            [MarshalAs(UnmanagedType.IUnknown)] out object sessionControl);

        [PreserveSig]
        int GetSimpleAudioVolume(
            ref Guid audioSessionGuid,
            uint streamFlags,
            [MarshalAs(UnmanagedType.IUnknown)] out object simpleAudioVolume);

        [PreserveSig]
        int GetSessionEnumerator(out IAudioSessionEnumerator sessionEnumerator);
    }

    [ComImport]
    [Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionEnumerator
    {
        [PreserveSig]
        int GetCount(out int sessionCount);

        [PreserveSig]
        int GetSession(int sessionIndex, out IAudioSessionControl session);
    }

    [ComImport]
    [Guid("F4B1A599-7266-4319-A8CA-E70ACB11E8CD")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionControl
    {
        [PreserveSig]
        int GetState(out AudioSessionState state);
    }
}
