using System;
using Microsoft.Extensions.Logging;

namespace SteamInputBridge.Hosting;

internal static partial class HostingLog
{
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

    [LoggerMessage(EventId = 34, Level = LogLevel.Warning, Message = "HidHide update failed for client {ClientId}: {Message}")]
    public static partial void HidHideUpdateFailed(ILogger logger, Guid? clientId, string message);

    [LoggerMessage(EventId = 39, Level = LogLevel.Information, Message = "Registered this process for HidHide device access.")]
    public static partial void HidHideApplicationAccessRegistered(ILogger logger);

    [LoggerMessage(EventId = 40, Level = LogLevel.Warning, Message = "HidHide application access registration failed: {Message}")]
    public static partial void HidHideApplicationAccessFailed(ILogger logger, string message);
}
