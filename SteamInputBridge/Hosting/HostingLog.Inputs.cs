using System;
using Microsoft.Extensions.Logging;

namespace SteamInputBridge.Hosting;

internal static partial class HostingLog
{
    [LoggerMessage(EventId = 7, Level = LogLevel.Information, Message = "SDL controller streaming restarting: {Message}")]
    public static partial void SdlControllerStreamingRestarting(ILogger logger, string message);

    [LoggerMessage(EventId = 8, Level = LogLevel.Warning, Message = "Raw Input mouse pump disabled: Windows is required.")]
    public static partial void RawInputMousePumpDisabled(ILogger logger);

    [LoggerMessage(EventId = 9, Level = LogLevel.Information, Message = "Raw Input mouse pump started.")]
    public static partial void RawInputMousePumpStarted(ILogger logger);

    [LoggerMessage(EventId = 10, Level = LogLevel.Warning, Message = "Raw Input mouse pump stopped: {Message}")]
    public static partial void RawInputMousePumpStopped(ILogger logger, string message);

    [LoggerMessage(EventId = 11, Level = LogLevel.Information, Message = "Physical SDL controller pump started.")]
    public static partial void PhysicalControllerPumpStarted(ILogger logger);

    [LoggerMessage(EventId = 12, Level = LogLevel.Information, Message = "Physical SDL controller pump restarting: {Message}")]
    public static partial void PhysicalControllerPumpRestarting(ILogger logger, string message);

    [LoggerMessage(EventId = 42, Level = LogLevel.Information, Message = "Client SDL controller scan: visible={VisibleCount} selected={SelectedCount} opened={OpenedCount} controllers={Controllers}")]
    public static partial void ClientControllerScan(
        ILogger logger,
        int visibleCount,
        int selectedCount,
        int openedCount,
        string controllers);

    [LoggerMessage(
        EventId = 43,
        Level = LogLevel.Information,
        Message = "Registered client controllers: client={ClientId} count={Count} routes={Routes}",
        SkipEnabledCheck = true)]
    public static partial void ClientControllersRegistered(
        ILogger logger,
        Guid clientId,
        int count,
        string routes);

    [LoggerMessage(EventId = 44, Level = LogLevel.Information, Message = "Client controller routes: client={ClientId} profile={ProfileId} routes={Routes}")]
    public static partial void ClientControllerRoutes(
        ILogger logger,
        Guid? clientId,
        string profileId,
        string routes);

    [LoggerMessage(EventId = 48, Level = LogLevel.Information, Message = "Passive controller matched: client={ClientId} index={ControllerIndex} physical=\"{PhysicalId}\"")]
    public static partial void PassiveControllerMatched(
        ILogger logger,
        Guid clientId,
        ushort controllerIndex,
        string physicalId);

}
