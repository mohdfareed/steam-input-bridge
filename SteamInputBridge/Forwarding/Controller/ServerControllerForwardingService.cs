using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SteamInputBridge.Forwarding.Mouse;
using SteamInputBridge.Outputs.Mouse;
using SteamInputBridge.Profiles;
using SteamInputBridge.Settings;
using StreamJsonRpc;

namespace SteamInputBridge.Forwarding.Controller;

/// <summary>Controls which connected client is allowed to forward controller input.</summary>
public sealed class ServerControllerForwardingService(
    ActiveProfileService profiles,
    ProfileClientsService clients,
    ServerMouseForwardingService mouse,
    ILogger<ServerControllerForwardingService> logger) : IHostedService, IDisposable
{
    private readonly SemaphoreSlim _publish = new(1, 1);

    // MARK: Lifecycle
    // ========================================================================

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        profiles.ActiveProfileChanged += OnActiveProfileChanged;
        clients.ClientsChanged += OnClientsChanged;
        mouse.PointerEnabledChanged += OnPointerEnabledChanged;
        return PublishActiveStateAsync(CancellationToken.None);
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        profiles.ActiveProfileChanged -= OnActiveProfileChanged;
        clients.ClientsChanged -= OnClientsChanged;
        mouse.PointerEnabledChanged -= OnPointerEnabledChanged;
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

    private void OnPointerEnabledChanged(object? sender, EventArgs args)
    {
        _ = sender;
        _ = args;
        _ = PublishPointerStateAsync(CancellationToken.None);
    }

    // MARK: Implementation
    // ========================================================================

    private async Task PublishActiveStateAsync(CancellationToken cancellationToken)
    {
        await _publish.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ProfileStatus? activeProfile = profiles.ActiveProfile;
            string? activeProfileId = activeProfile?.Id;
            IReadOnlyList<ProfileClientsService.BridgeClientConnection> connections = clients.Connections;

            foreach (ProfileClientsService.BridgeClientConnection connection in connections)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!string.Equals(connection.ProfileId, activeProfileId, StringComparison.OrdinalIgnoreCase))
                {
                    await SetActiveAsync(connection, active: false).ConfigureAwait(false);
                }
            }

            bool clientOwnsMouse = activeProfile?.Definition.MouseInput == MouseInputMode.Steam &&
                activeProfile.Definition.MouseOutput.HasValue;
            bool clientUsesTeensy = clientOwnsMouse &&
                activeProfile!.Definition.MouseOutput == MouseOutput.Teensy;
            await mouse.SetClientOwnsOutputAsync(clientOwnsMouse, clientUsesTeensy, cancellationToken)
                .ConfigureAwait(false);

            foreach (ProfileClientsService.BridgeClientConnection connection in connections)
            {
                if (string.Equals(connection.ProfileId, activeProfileId, StringComparison.OrdinalIgnoreCase))
                {
                    await SetActiveAsync(connection, active: true).ConfigureAwait(false);
                    await SetPointerEnabledAsync(connection, mouse.Status.PointerEnabled).ConfigureAwait(false);
                    break;
                }
            }
        }
        finally
        {
            _ = _publish.Release();
        }
    }

    private async Task PublishInactiveStateAsync()
    {
        await _publish.WaitAsync().ConfigureAwait(false);
        try
        {
            IReadOnlyList<ProfileClientsService.BridgeClientConnection> connections = clients.Connections;
            foreach (ProfileClientsService.BridgeClientConnection connection in connections)
            {
                await SetActiveAsync(connection, active: false).ConfigureAwait(false);
            }

            await mouse.SetClientOwnsOutputAsync(
                    clientOwnsOutput: false,
                    clientUsesTeensy: false,
                    cancellationToken: CancellationToken.None)
                .ConfigureAwait(false);
        }
        finally
        {
            _ = _publish.Release();
        }
    }

    private async Task SetActiveAsync(ProfileClientsService.BridgeClientConnection connection, bool active)
    {
        try
        {
            await connection.Control.SetActiveAsync(active).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is InvalidOperationException or ObjectDisposedException or ConnectionLostException)
        {
            LogActiveStateUpdateFailed(logger, connection.ProfileId, connection.ConnectionId, exception.Message, null);
        }
    }

    private async Task PublishPointerStateAsync(CancellationToken cancellationToken)
    {
        await _publish.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            string? activeProfileId = profiles.ActiveProfile?.Id;
            foreach (ProfileClientsService.BridgeClientConnection connection in clients.Connections)
            {
                if (string.Equals(connection.ProfileId, activeProfileId, StringComparison.OrdinalIgnoreCase))
                {
                    await SetPointerEnabledAsync(connection, mouse.Status.PointerEnabled).ConfigureAwait(false);
                    break;
                }
            }
        }
        finally
        {
            _ = _publish.Release();
        }
    }

    private async Task SetPointerEnabledAsync(
        ProfileClientsService.BridgeClientConnection connection,
        bool enabled)
    {
        try
        {
            await connection.Control.SetMousePointerEnabledAsync(enabled).ConfigureAwait(false);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or ObjectDisposedException or ConnectionLostException)
        {
            LogActiveStateUpdateFailed(logger, connection.ProfileId, connection.ConnectionId, exception.Message, null);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _publish.Dispose();
    }

    // MARK: Logging
    // ========================================================================

    private static readonly Action<ILogger, string, Guid, string, Exception?> LogActiveStateUpdateFailed =
        LoggerMessage.Define<string, Guid, string>(
            LogLevel.Debug,
            new EventId(1, nameof(LogActiveStateUpdateFailed)),
            "Client active state update failed for profile {ProfileId} connection {ConnectionId}: {Message}");
}
