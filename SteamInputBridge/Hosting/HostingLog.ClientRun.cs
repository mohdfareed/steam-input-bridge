using System;
using Microsoft.Extensions.Logging;

namespace SteamInputBridge.Hosting;

internal static partial class HostingLog
{
    [LoggerMessage(EventId = 6, Level = LogLevel.Information, Message = "Using settings file at: {SettingsPath}")]
    public static partial void UsingSettingsFile(ILogger logger, string settingsPath);

    [LoggerMessage(EventId = 23, Level = LogLevel.Information, Message = "Started {ProfileId} rootPid={ProcessId}")]
    public static partial void Started(ILogger logger, string profileId, int processId);

    [LoggerMessage(EventId = 31, Level = LogLevel.Information, Message = "Attached {ProfileId} without launching a process.")]
    public static partial void Attached(ILogger logger, string profileId);

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

    [LoggerMessage(EventId = 46, Level = LogLevel.Warning, Message = "Server registration restore failed; retrying: {Message}")]
    public static partial void ServerRegistrationRestoreRetrying(ILogger logger, string message);
}
