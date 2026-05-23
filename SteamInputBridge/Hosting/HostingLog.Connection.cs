using System;
using Microsoft.Extensions.Logging;
using SteamInputBridge.Hosting.Client.Connection;

namespace SteamInputBridge.Hosting;

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

    [LoggerMessage(EventId = 45, Level = LogLevel.Warning, Message = "Startup cleanup did not finish: {Message}")]
    public static partial void StartupCleanupDidNotFinish(ILogger logger, string message);
}
