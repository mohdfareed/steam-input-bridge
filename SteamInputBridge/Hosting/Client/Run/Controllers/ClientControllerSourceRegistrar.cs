using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SteamInputBridge.Hosting.Client.Connection;
using SteamInputBridge.Inputs.Sdl;

namespace SteamInputBridge.Hosting.Client.Run.Controllers;

internal sealed class ClientControllerSourceRegistrar(
    ClientControllerSourceRegistry sources,
    ILogger logger) : IDisposable
{
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private string? _lastScanSignature;
    private string? _lastRouteSignature;
    private Guid? _lastRegisteredClientId;
    private IReadOnlyList<ClientControllerInfo> _lastRegisteredControllers = [];

    public async Task<IReadOnlyList<SdlGamepadSource>> RefreshSourcesAsync(
        ClientService client,
        string profileId,
        CancellationToken cancellationToken)
    {
        // Empty scans are treated as transient. Non-empty scans are allowed to
        // replace stale opened sources because Steam can reshuffle/reuse handles
        // while rebuilding virtual controllers.
        await _refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await AddMissingSourcesAsync(client, profileId, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _ = _refreshGate.Release();
        }
    }

    private async Task<IReadOnlyList<SdlGamepadSource>> AddMissingSourcesAsync(
        ClientService client,
        string profileId,
        CancellationToken cancellationToken)
    {
        HashSet<SdlControllerId> openIds = sources.GetOpenSourceIds();
        IReadOnlyList<SdlControllerInfo> visibleControllers = [];
        IReadOnlyList<SdlControllerInfo> selectedControllers = [];
        List<SdlControllerInfo> physicalControllers = [];
        IReadOnlyList<SdlGamepadSource> openedSources = SdlControllerCatalog.OpenControllers(controllers =>
        {
            visibleControllers = ClientControllerRoutePlanner.FilterForwardable(controllers);
            physicalControllers = SdlControllerRoutePolicy.GetPhysicalControllers(visibleControllers);
            selectedControllers = ClientControllerRoutePlanner.SelectClientControllers(visibleControllers);
            DisposeStaleSources(sources.RemoveStale(selectedControllers));
            List<SdlControllerInfo> missingControllers = [];
            openIds = sources.GetOpenSourceIds();
            foreach (SdlControllerInfo controller in selectedControllers)
            {
                if (!openIds.Contains(controller.Id))
                {
                    missingControllers.Add(controller);
                }
            }

            return missingControllers;
        });

        LogScanIfChanged(visibleControllers, selectedControllers, openedSources);
        try
        {
            IReadOnlyList<ClientControllerRouteSource> routeSources = sources.Add(openedSources);
            ClientControllerRoutePlan plan = ClientControllerRoutePlanner.CreatePlan(
                routeSources,
                physicalControllers);
            LogRoutesIfChanged(client.ClientId, profileId, routeSources, plan);

            await RegisterControllersIfChangedAsync(client, plan.Controllers, cancellationToken).ConfigureAwait(false);
            return sources.GetGamepadSourcesSnapshot();
        }
        catch
        {
            foreach (SdlGamepadSource source in openedSources)
            {
                if (sources.Remove(source, out ClientControllerRouteSource removed))
                {
                    await removed.Source.DisposeAsync().ConfigureAwait(false);
                }
                else
                {
                    await source.DisposeAsync().ConfigureAwait(false);
                }
            }

            throw;
        }
    }

    private static void DisposeStaleSources(IReadOnlyList<ClientControllerRouteSource> staleSources)
    {
        foreach (ClientControllerRouteSource source in staleSources)
        {
            source.Source.Dispose();
        }
    }

    public void RemoveSource(
        ClientService client,
        string profileId,
        SdlGamepadSource source,
        CancellationToken cancellationToken)
    {
        if (!sources.Remove(source, out ClientControllerRouteSource removed))
        {
            return;
        }

        removed.Source.Dispose();
        RefreshControllerRegistration(client, profileId, cancellationToken);
    }

    public void RefreshSources(
        ClientService client,
        string profileId,
        CancellationToken cancellationToken)
    {
        _refreshGate.Wait(cancellationToken);
        try
        {
            _ = AddMissingSourcesAsync(client, profileId, cancellationToken).GetAwaiter().GetResult();
        }
        finally
        {
            _ = _refreshGate.Release();
        }
    }

    public void Dispose()
    {
        _refreshGate.Dispose();
    }

    private void RefreshControllerRegistration(
        ClientService client,
        string profileId,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<ClientControllerRouteSource> routeSources = sources.GetSourcesSnapshot();
        ClientControllerRoutePlan plan = ClientControllerRoutePlanner.CreatePlan(
            routeSources,
            SdlControllerRoutePolicy.GetPhysicalControllers(
                ClientControllerRoutePlanner.FilterForwardable(SdlControllerCatalog.GetControllers())));
        LogRoutesIfChanged(client.ClientId, profileId, routeSources, plan);
        RegisterControllersIfChangedAsync(client, plan.Controllers, cancellationToken).GetAwaiter().GetResult();
    }

    private async Task RegisterControllersIfChangedAsync(
        ClientService client,
        IReadOnlyList<ClientControllerInfo> controllers,
        CancellationToken cancellationToken)
    {
        if (_lastRegisteredClientId == client.ClientId &&
            controllers.SequenceEqual(_lastRegisteredControllers))
        {
            return;
        }

        await client.RegisterClientControllersAsync(controllers, cancellationToken).ConfigureAwait(false);
        _lastRegisteredClientId = client.ClientId;
        _lastRegisteredControllers = [.. controllers];
    }

    private void LogScanIfChanged(
        IReadOnlyList<SdlControllerInfo> visibleControllers,
        IReadOnlyList<SdlControllerInfo> selectedControllers,
        IReadOnlyList<SdlGamepadSource> openedSources)
    {
        string signature = ClientControllerRouteText.FormatScanSignature(visibleControllers);
        if (signature == _lastScanSignature)
        {
            return;
        }

        _lastScanSignature = signature;
        HostingLog.ClientControllerScan(
            logger,
            visibleControllers.Count,
            selectedControllers.Count,
            openedSources.Count,
            signature);
    }

    private void LogRoutesIfChanged(
        Guid? clientId,
        string profileId,
        IReadOnlyList<ClientControllerRouteSource> routeSources,
        ClientControllerRoutePlan plan)
    {
        string routes = ClientControllerRouteText.FormatRouteDecisions(routeSources, plan.Identities);
        string signature = $"{clientId}:{profileId}:{routes}";
        if (signature == _lastRouteSignature)
        {
            return;
        }

        _lastRouteSignature = signature;
        HostingLog.ClientControllerRoutes(logger, clientId, profileId, routes);
    }
}
