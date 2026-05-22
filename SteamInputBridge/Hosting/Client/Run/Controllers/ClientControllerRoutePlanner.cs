using System;
using System.Collections.Generic;
using SteamInputBridge.Inputs.Sdl;

namespace SteamInputBridge.Hosting.Client.Run.Controllers;

internal static class ClientControllerRoutePlanner
{
    public static List<SdlControllerInfo> FilterForwardable(
        IReadOnlyList<SdlControllerInfo> controllers)
    {
        List<SdlControllerInfo> forwardable = [];
        foreach (SdlControllerInfo controller in controllers)
        {
            if (SdlControllerRoutePolicy.IsForwardable(controller))
            {
                forwardable.Add(controller);
            }
        }

        return forwardable;
    }

    public static IReadOnlyList<SdlControllerInfo> SelectClientControllers(
        IReadOnlyList<SdlControllerInfo> visibleControllers)
    {
        List<SdlControllerInfo> physicalControllers = GetPhysicalControllers(visibleControllers);
        HashSet<string> steamMatchedPhysicalIds = new(StringComparer.OrdinalIgnoreCase);
        foreach (SdlControllerInfo controller in visibleControllers)
        {
            if (controller.Source == SdlControllerSource.Steam &&
                SdlControllerRoutePolicy.FindPhysicalController(controller, physicalControllers) is { } physical)
            {
                _ = steamMatchedPhysicalIds.Add(SdlControllerRoutePolicy.GetPhysicalControllerId(physical));
            }
        }

        List<SdlControllerInfo> selected = [];
        foreach (SdlControllerInfo controller in visibleControllers)
        {
            if (controller.Source == SdlControllerSource.Steam ||
                !steamMatchedPhysicalIds.Contains(SdlControllerRoutePolicy.GetPhysicalControllerId(controller)))
            {
                selected.Add(controller);
            }
        }

        return selected;
    }

    public static ClientControllerRoutePlan CreatePlan(
        IReadOnlyList<ClientControllerRouteSource> sources,
        IReadOnlyList<SdlControllerInfo> physicalControllers)
    {
        Dictionary<SdlGamepadSource, SdlControllerRouteIdentity> identities =
            CreateRouteIdentities(sources, physicalControllers);
        ClientControllerInfo[] controllers = new ClientControllerInfo[sources.Count];
        for (int i = 0; i < sources.Count; i++)
        {
            ClientControllerRouteSource source = sources[i];
            SdlGamepadSource gamepad = source.Source;
            SdlControllerRouteIdentity identity = identities[gamepad];
            controllers[i] = new ClientControllerInfo(
                source.ControllerIndex,
                identity.RouteId,
                identity.Label,
                gamepad.Features,
                identity.PhysicalDeviceId);
        }

        return new ClientControllerRoutePlan(controllers, identities);
    }

    public static List<SdlControllerInfo> GetPhysicalControllers(
        IReadOnlyList<SdlControllerInfo> controllers)
    {
        return SdlControllerRoutePolicy.GetPhysicalControllers(controllers);
    }

    private static Dictionary<SdlGamepadSource, SdlControllerRouteIdentity> CreateRouteIdentities(
        IReadOnlyList<ClientControllerRouteSource> sources,
        IReadOnlyList<SdlControllerInfo> physicalControllers)
    {
        Dictionary<SdlGamepadSource, SdlControllerRouteIdentity> identities = [];
        foreach (ClientControllerRouteSource source in sources)
        {
            identities[source.Source] = SdlControllerRoutePolicy.CreateIdentity(
                source.ControllerIndex,
                source.Source.Controller,
                physicalControllers);
        }

        return identities;
    }

}
