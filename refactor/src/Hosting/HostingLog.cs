using System;
using Microsoft.Extensions.Logging;

namespace VirtualMouse.Hosting;

internal static partial class HostingLog
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "Server connection lost: {Message}")]
    public static partial void ServerConnectionLost(ILogger logger, string message);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "Connecting to server pipe {PipeName}")]
    public static partial void ConnectingToServerPipe(ILogger logger, string pipeName);

    [LoggerMessage(EventId = 3, Level = LogLevel.Information, Message = "Connected to server as {ClientId}")]
    public static partial void ConnectedToServer(ILogger logger, Guid? clientId);

    [LoggerMessage(EventId = 4, Level = LogLevel.Warning, Message = "Reconnect failed: {Message}")]
    public static partial void ReconnectFailed(ILogger logger, string message);

    [LoggerMessage(EventId = 5, Level = LogLevel.Information, Message = "Listening on server pipe {PipeName}")]
    public static partial void ListeningOnServerPipe(ILogger logger, string pipeName);

    [LoggerMessage(EventId = 6, Level = LogLevel.Information, Message = "Using settings file at: {SettingsPath}")]
    public static partial void UsingSettingsFile(ILogger logger, string settingsPath);

    [LoggerMessage(EventId = 7, Level = LogLevel.Information, Message = "SDL controller streaming restarting: {Message}")]
    public static partial void SdlControllerStreamingRestarting(ILogger logger, string message);

    [LoggerMessage(EventId = 8, Level = LogLevel.Warning, Message = "Raw Input mouse pump disabled: Windows is required.")]
    public static partial void RawInputMousePumpDisabled(ILogger logger);

    [LoggerMessage(EventId = 9, Level = LogLevel.Information, Message = "Raw Input mouse pump started.")]
    public static partial void RawInputMousePumpStarted(ILogger logger);

    [LoggerMessage(EventId = 10, Level = LogLevel.Warning, Message = "Raw Input mouse pump stopped: {Message}")]
    public static partial void RawInputMousePumpStopped(ILogger logger, string message);

    [LoggerMessage(EventId = 11, Level = LogLevel.Information, Message = "Physical SDL controller pump started: controllers={Count}")]
    public static partial void PhysicalControllerPumpStarted(ILogger logger, int count);

    [LoggerMessage(EventId = 12, Level = LogLevel.Information, Message = "Physical SDL controller pump restarting: {Message}")]
    public static partial void PhysicalControllerPumpRestarting(ILogger logger, string message);

    [LoggerMessage(EventId = 13, Level = LogLevel.Information, Message = "Active client changed: previous={PreviousClientId} current={CurrentClientId}")]
    public static partial void ActiveClientChanged(ILogger logger, Guid? previousClientId, Guid? currentClientId);

    [LoggerMessage(EventId = 14, Level = LogLevel.Information, Message = "Clearing forced Steam Input app id.")]
    public static partial void ClearingForcedSteamInputAppId(ILogger logger);

    [LoggerMessage(EventId = 15, Level = LogLevel.Information, Message = "Forcing Steam Input app id {AppId} for client {ClientId}.")]
    public static partial void ForcingSteamInputAppId(ILogger logger, uint appId, Guid? clientId);

    [LoggerMessage(EventId = 16, Level = LogLevel.Debug, Message = "No Steam Input app id to force.")]
    public static partial void NoSteamInputAppIdToForce(ILogger logger);

    [LoggerMessage(EventId = 17, Level = LogLevel.Warning, Message = "Steam Input forcing failed for client {ClientId}: {Message}")]
    public static partial void SteamInputForcingFailed(ILogger logger, Guid? clientId, string message);

    [LoggerMessage(EventId = 18, Level = LogLevel.Information, Message = "Client connected: {ClientId} process={ProcessId} (clients={ClientCount})")]
    public static partial void ClientConnected(ILogger logger, Guid clientId, int processId, int clientCount);

    [LoggerMessage(EventId = 19, Level = LogLevel.Information, Message = "Client disconnected: {ClientId} (clients={ClientCount})")]
    public static partial void ClientDisconnected(ILogger logger, Guid clientId, int clientCount);

    [LoggerMessage(EventId = 20, Level = LogLevel.Information, Message = "Client pipe closed: {Message}")]
    public static partial void ClientPipeClosed(ILogger logger, string message);

    [LoggerMessage(EventId = 21, Level = LogLevel.Information, Message = "Controller pipe for client {ClientId} closed: {Message}")]
    public static partial void ControllerPipeClosed(ILogger logger, Guid clientId, string message);

    [LoggerMessage(EventId = 22, Level = LogLevel.Information, Message = "Connection changed: {State} client={ClientId}")]
    public static partial void ConnectionChanged(ILogger logger, ClientConnectionState state, Guid? clientId);

    [LoggerMessage(EventId = 23, Level = LogLevel.Information, Message = "Started {ProfileId} rootPid={ProcessId}")]
    public static partial void Started(ILogger logger, string profileId, int processId);

    [LoggerMessage(EventId = 24, Level = LogLevel.Information, Message = "Watching receiver processes for {ProfileId}: {Receivers}")]
    public static partial void WatchingReceiverProcesses(ILogger logger, string profileId, string receivers);

    [LoggerMessage(EventId = 25, Level = LogLevel.Information, Message = "Receiver processes for {ProfileId}: count={Count} {Processes}")]
    public static partial void ReceiverProcesses(ILogger logger, string profileId, int count, string processes);

    [LoggerMessage(EventId = 26, Level = LogLevel.Information, Message = "Root process exited before receiver detection for {ProfileId}; waiting {Seconds}s for receivers.")]
    public static partial void RootProcessExitedBeforeReceiver(ILogger logger, string profileId, double seconds);

    [LoggerMessage(EventId = 27, Level = LogLevel.Information, Message = "No receiver processes appeared for {ProfileId}; ending client run.")]
    public static partial void NoReceiverProcessesAppeared(ILogger logger, string profileId);

    [LoggerMessage(EventId = 28, Level = LogLevel.Information, Message = "{Reason} stopped game processes: {Count}")]
    public static partial void StoppedGameProcesses(ILogger logger, string reason, int count);

    [LoggerMessage(EventId = 29, Level = LogLevel.Warning, Message = "Could not attach launched process to cleanup job: {Message}")]
    public static partial void CouldNotAttachProcessJob(ILogger logger, string message);

    [LoggerMessage(EventId = 30, Level = LogLevel.Information, Message = "Restored server registration for {ProfileId} client={ClientId}")]
    public static partial void RestoredServerRegistration(ILogger logger, string profileId, Guid? clientId);
}
