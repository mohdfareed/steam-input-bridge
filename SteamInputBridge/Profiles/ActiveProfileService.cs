using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Vanara.PInvoke;
using static Vanara.PInvoke.User32;

namespace SteamInputBridge.Profiles;

/// <summary>Tracks active profile from foreground receiver processes.</summary>
public sealed class ActiveProfileService : IHostedService, IDisposable
{
    private static readonly TimeSpan ForegroundPollInterval = TimeSpan.FromMilliseconds(250);

    private readonly ProfileCatalogService _catalog;
    private readonly ProfileClientsService _clients;
    private readonly Func<int?> _foregroundProcessId;
    private readonly TimeSpan _foregroundPollInterval;
    private readonly Lock _gate = new();
    private readonly CancellationTokenSource _stop = new();
    private ProfileStatus? _activeProfile;
    private Task? _monitor;
    private bool _disposed;

    /// <summary>Creates active profile tracking from the foreground window.</summary>
    public ActiveProfileService(ProfileCatalogService catalog, ProfileClientsService clients)
        : this(catalog, clients, ForegroundProcessId, ForegroundPollInterval)
    {
    }

    internal ActiveProfileService(
        ProfileCatalogService catalog,
        ProfileClientsService clients,
        Func<int?> foregroundProcessId,
        TimeSpan foregroundPollInterval)
    {
        _catalog = catalog;
        _clients = clients;
        _foregroundProcessId = foregroundProcessId;
        _foregroundPollInterval = foregroundPollInterval;
    }

    // MARK: Publics
    // ========================================================================

    /// <summary>Raised when resolved profile availability changes.</summary>
    public event EventHandler<ProfilesChangedEventArgs>? ProfilesChanged;

    /// <summary>Raised when the active profile changes.</summary>
    public event EventHandler<ActiveProfileChangedEventArgs>? ActiveProfileChanged;

    /// <summary>All resolved profile statuses.</summary>
    public IReadOnlyList<ProfileStatus> Profiles => SnapshotProfiles();

    /// <summary>Profiles with connected clients.</summary>
    public IReadOnlyList<ProfileStatus> MonitoredProfiles => SnapshotProfiles(static profile => profile.ClientProcessId.HasValue);

    /// <summary>Current active profile, or null when no monitored profile is active.</summary>
    public ProfileStatus? ActiveProfile
    {
        get
        {
            lock (_gate)
            {
                return _activeProfile;
            }
        }
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _catalog.ProfilesChanged += OnProfilesChanged;
        _clients.ClientsChanged += OnClientsChanged;
        _monitor = Task.Run(() => MonitorForegroundAsync(_stop.Token), cancellationToken);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _catalog.ProfilesChanged -= OnProfilesChanged;
        _clients.ClientsChanged -= OnClientsChanged;
        await _stop.CancelAsync().ConfigureAwait(false);

        if (_monitor is not null)
        {
            try
            {
                await _monitor.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested)
            {
            }
        }
    }

    /// <summary>Stops active profile monitoring.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _catalog.ProfilesChanged -= OnProfilesChanged;
        _clients.ClientsChanged -= OnClientsChanged;

        _stop.Cancel();
        _stop.Dispose();
    }

    // MARK: Events
    // ========================================================================

    private void OnProfilesChanged(object? sender, EventArgs args)
    {
        _ = sender;
        _ = args;
        PublishProfilesChanged();
    }

    private void OnClientsChanged(object? sender, EventArgs args)
    {
        _ = sender;
        _ = args;
        PublishProfilesChanged();
    }

    // MARK: Foreground
    // ========================================================================

    private async Task MonitorForegroundAsync(CancellationToken cancellationToken)
    {
        using PeriodicTimer timer = new(_foregroundPollInterval);
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            ProfileStatus? activeProfile = FindActiveProfile();
            ProfileStatus? previous;
            lock (_gate)
            {
                previous = _activeProfile;
                _activeProfile = activeProfile;
            }

            if (previous?.Id != activeProfile?.Id)
            {
                ActiveProfileChanged?.Invoke(this, new(activeProfile));
                PublishProfilesChanged();
            }
        }
    }

    private ProfileStatus? FindActiveProfile()
    {
        int? foregroundProcessId = _foregroundProcessId();
        if (!foregroundProcessId.HasValue)
        {
            return null;
        }

        foreach (ProfileStatus profile in MonitoredProfiles)
        {
            ProfileClientStatus? client = ConnectedClient(profile.Id, _clients.Clients);
            if (client is null)
            {
                continue;
            }

            foreach (int receiverProcessId in client.ReceiverProcessIds)
            {
                if (receiverProcessId == foregroundProcessId.Value)
                {
                    return profile with { Active = true };
                }
            }
        }

        return null;
    }

    // MARK: Snapshots
    // ========================================================================

    private void PublishProfilesChanged()
    {
        ProfilesChanged?.Invoke(this, new(Profiles));
    }

    private List<ProfileStatus> SnapshotProfiles(Func<ProfileStatus, bool>? filter = null)
    {
        IReadOnlyList<ResolvedProfile> resolvedProfiles = _catalog.Profiles;
        IReadOnlyList<ProfileClientStatus> connectedClients = _clients.Clients;
        string? activeProfileId = ActiveProfile?.Id;

        List<ProfileStatus> profiles = new(resolvedProfiles.Count);
        foreach (ResolvedProfile profile in resolvedProfiles)
        {
            ProfileStatus status = ToStatus(profile, connectedClients, activeProfileId);
            if (filter is null || filter(status))
            {
                profiles.Add(status);
            }
        }

        return profiles;
    }

    private static ProfileStatus ToStatus(
        ResolvedProfile profile,
        IReadOnlyList<ProfileClientStatus> clients,
        string? activeProfileId)
    {
        ProfileClientStatus? client = ConnectedClient(profile.Id, clients);
        return new(
            profile.Id,
            profile.Title,
            profile.SteamAppId,
            client?.SteamAppId ?? profile.SteamAppId,
            profile.MouseOutput,
            profile.ControllerOutput,
            profile.ReceiverProcesses,
            client?.ReceiverProcessIds ?? [],
            Active: string.Equals(activeProfileId, profile.Id, StringComparison.OrdinalIgnoreCase),
            ClientProcessId: client?.ProcessId,
            ClientConnectionId: client?.ConnectionId);
    }

    private static ProfileClientStatus? ConnectedClient(string profileId, IReadOnlyList<ProfileClientStatus> clients)
    {
        foreach (ProfileClientStatus client in clients)
        {
            if (string.Equals(client.ProfileId, profileId, StringComparison.OrdinalIgnoreCase))
            {
                return client;
            }
        }

        return null;
    }

    // MARK: Windows
    // ========================================================================

    private static int? ForegroundProcessId()
    {
        HWND foregroundWindow = GetForegroundWindow();
        if (foregroundWindow.IsNull)
        {
            return null;
        }

        _ = GetWindowThreadProcessId(foregroundWindow, out uint processId);
        return processId == 0 ? null : (int)processId;
    }
}
