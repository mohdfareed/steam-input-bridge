namespace SteamInputBridge.Microphone;

internal interface IMicrophoneControl
{
    MicrophoneStatus GetStatus();

    void SetEnabled(bool enabled);
}
