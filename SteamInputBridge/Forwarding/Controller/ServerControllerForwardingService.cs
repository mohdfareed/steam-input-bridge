using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SteamInputBridge.Profiles;

namespace SteamInputBridge.Forwarding.Controller;

/// <summary>Controls which connected client is allowed to forward controller input.</summary>
public sealed class ServerControllerForwardingService(
    ActiveProfileService profiles,
    ProfileClientsService clients,
    ILogger<ServerControllerForwardingService> logger) : IHostedService
{
    // MARK: Lifecycle
    // ========================================================================

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        profiles.ActiveProfileChanged += OnActiveProfileChanged;
        clients.ClientsChanged += OnClientsChanged;
        return PublishActiveStateAsync(CancellationToken.None);
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        profiles.ActiveProfileChanged -= OnActiveProfileChanged;
        clients.ClientsChanged -= OnClientsChanged;
        return PublishInactiveStateAsync();
    }

    // MARK: Events
    // ========================================================================

    private void OnActiveProfileChanged(object? sender, ActiveProfileChangedEventArgs args)
    {
        _ = sender;
        _ = args;
        _ = PublishActiveStateAsync(CancellationToken.None);
    }

    private void OnClientsChanged(object? sender, EventArgs args)
    {
        _ = sender;
        _ = args;
        _ = PublishActiveStateAsync(CancellationToken.None);
    }

    // MARK: Implementation
    // ========================================================================

    private async Task PublishActiveStateAsync(CancellationToken cancellationToken)
    {
        string? activeProfileId = profiles.ActiveProfile?.Id;
        IReadOnlyList<ProfileClientsService.BridgeClientConnection> connections = clients.Connections;
        foreach (ProfileClientsService.BridgeClientConnection connection in connections)
        {
            cancellationToken.ThrowIfCancellationRequested();
            bool active = string.Equals(connection.ProfileId, activeProfileId, StringComparison.OrdinalIgnoreCase);
            await SetActiveAsync(connection, active).ConfigureAwait(false);
        }
    }

    private async Task PublishInactiveStateAsync()
    {
        IReadOnlyList<ProfileClientsService.BridgeClientConnection> connections = clients.Connections;
        foreach (ProfileClientsService.BridgeClientConnection connection in connections)
        {
            await SetActiveAsync(connection, active: false).ConfigureAwait(false);
        }
    }

    private async Task SetActiveAsync(ProfileClientsService.BridgeClientConnection connection, bool active)
    {
        try
        {
            await connection.Control.SetActiveAsync(active).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is InvalidOperationException or ObjectDisposedException)
        {
            LogActiveStateUpdateFailed(logger, connection.ProfileId, connection.ConnectionId, exception.Message, null);
        }
    }

    // MARK: Logging
    // ========================================================================

    private static readonly Action<ILogger, string, Guid, string, Exception?> LogActiveStateUpdateFailed =
        LoggerMessage.Define<string, Guid, string>(
            LogLevel.Debug,
            new EventId(1, nameof(LogActiveStateUpdateFailed)),
            "Client active state update failed for profile {ProfileId} connection {ConnectionId}: {Message}");
}
