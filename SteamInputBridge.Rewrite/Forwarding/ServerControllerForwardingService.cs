using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SteamInputBridge.Hosting;
using SteamInputBridge.Profiles;

namespace SteamInputBridge.Forwarding;

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
            await SetActiveAsync(connection.Control, active).ConfigureAwait(false);
        }
    }

    private async Task PublishInactiveStateAsync()
    {
        IReadOnlyList<ProfileClientsService.BridgeClientConnection> connections = clients.Connections;
        foreach (ProfileClientsService.BridgeClientConnection connection in connections)
        {
            await SetActiveAsync(connection.Control, active: false).ConfigureAwait(false);
        }
    }

    private async Task SetActiveAsync(IBridgeClientApi control, bool active)
    {
        try
        {
            await control.SetActiveAsync(active).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is InvalidOperationException or ObjectDisposedException)
        {
            LogClientActiveUpdateFailed(logger, exception.Message, null);
        }
    }

    // MARK: Logging
    // ========================================================================

    private static readonly Action<ILogger, string, Exception?> LogClientActiveUpdateFailed =
        LoggerMessage.Define<string>(
            LogLevel.Debug,
            new EventId(1, nameof(LogClientActiveUpdateFailed)),
            "Client active state update failed: {Message}");
}
