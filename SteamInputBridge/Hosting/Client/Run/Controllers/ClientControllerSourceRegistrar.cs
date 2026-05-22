using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SteamInputBridge.Hosting.Client.Connection;
using SteamInputBridge.Inputs.Sdl;

namespace SteamInputBridge.Hosting.Client.Run.Controllers;

internal sealed class ClientControllerSourceRegistrar(
    ClientControllerSourceRegistry sources,
    ILogger logger)
{
    private string? _lastScanSignature;
    private string? _lastRouteSignature;

    public async Task<IReadOnlyList<SdlGamepadSource>> RefreshSourcesAsync(
        ClientService client,
        string profileId,
        CancellationToken cancellationToken)
    {
        await sources.ClearAsync().ConfigureAwait(false);
        await client.RegisterClientControllersAsync([], cancellationToken).ConfigureAwait(false);
        return await AddMissingSourcesAsync(client, profileId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<SdlGamepadSource>> AddMissingSourcesAsync(
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
            physicalControllers = ClientControllerRoutePlanner.GetPhysicalControllers(visibleControllers);
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

            await client.RegisterClientControllersAsync(plan.Controllers, cancellationToken).ConfigureAwait(false);
            return sources.GetGamepadSourcesSnapshot();
        }
        catch
        {
            foreach (SdlGamepadSource source in openedSources)
            {
                await source.DisposeAsync().ConfigureAwait(false);
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
        _ = AddMissingSourcesAsync(client, profileId, cancellationToken).GetAwaiter().GetResult();
    }

    private void RefreshControllerRegistration(
        ClientService client,
        string profileId,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<ClientControllerRouteSource> routeSources = sources.GetSourcesSnapshot();
        ClientControllerRoutePlan plan = ClientControllerRoutePlanner.CreatePlan(
            routeSources,
            ClientControllerRoutePlanner.GetPhysicalControllers(
                ClientControllerRoutePlanner.FilterForwardable(SdlControllerCatalog.GetControllers())));
        LogRoutesIfChanged(client.ClientId, profileId, routeSources, plan);
        client.RegisterClientControllersAsync(plan.Controllers, cancellationToken).GetAwaiter().GetResult();
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
