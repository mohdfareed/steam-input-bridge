using System;
using System.Collections.Generic;
using System.Globalization;
using SteamInputBridge.Inputs.Sdl;

namespace SteamInputBridge.Hosting.Client;

internal static class ClientControllerRoutePlanner
{
    public static List<SdlControllerInfo> FilterForwardable(
        IReadOnlyList<SdlControllerInfo> controllers)
    {
        List<SdlControllerInfo> forwardable = [];
        foreach (SdlControllerInfo controller in controllers)
        {
            if (SdlControllerFilters.IsForwardable(controller))
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
                SdlControllerMatcher.FindPhysicalController(controller, physicalControllers) is { } physical)
            {
                _ = steamMatchedPhysicalIds.Add(SdlControllerCatalog.GetPhysicalControllerId(physical));
            }
        }

        List<SdlControllerInfo> selected = [];
        foreach (SdlControllerInfo controller in visibleControllers)
        {
            if (controller.Source == SdlControllerSource.Steam ||
                !steamMatchedPhysicalIds.Contains(SdlControllerCatalog.GetPhysicalControllerId(controller)))
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
        Dictionary<SdlGamepadSource, ControllerRouteIdentity> identities =
            CreateRouteIdentities(sources, physicalControllers);
        ClientControllerInfo[] controllers = new ClientControllerInfo[sources.Count];
        for (int i = 0; i < sources.Count; i++)
        {
            ClientControllerRouteSource source = sources[i];
            SdlGamepadSource gamepad = source.Source;
            ControllerRouteIdentity identity = identities[gamepad];
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
        List<SdlControllerInfo> physical = [];
        foreach (SdlControllerInfo controller in controllers)
        {
            if (controller.Source == SdlControllerSource.Physical)
            {
                physical.Add(controller);
            }
        }

        return physical;
    }

    internal static string GetRouteId(
        ushort controllerIndex,
        SdlControllerInfo controller,
        SdlControllerInfo? physical)
    {
        return physical is not null
            ? SdlControllerCatalog.GetPhysicalControllerId(physical)
            : controller.Source == SdlControllerSource.Steam && controller.SteamHandle != 0
            ? controller.Id.Value
            : !string.IsNullOrWhiteSpace(controller.Path)
            ? SdlControllerCatalog.GetPhysicalControllerId(controller)
            : string.Create(
                CultureInfo.InvariantCulture,
                $"client:{controllerIndex}:{controller.Source}:{controller.InstanceId}");
    }

    internal static string? GetPhysicalDeviceId(SdlControllerInfo controller, SdlControllerInfo? physical)
    {
        return physical is not null
            ? GetPathControllerId(physical)
            : controller.Source == SdlControllerSource.Physical
            ? GetPathControllerId(controller)
            : null;
    }

    public static string FormatScanSignature(IReadOnlyList<SdlControllerInfo> controllers)
    {
        if (controllers.Count == 0)
        {
            return "none";
        }

        List<string> values = [];
        foreach (SdlControllerInfo controller in controllers)
        {
            values.Add(FormatController(controller));
        }

        return string.Join("; ", values);
    }

    public static string FormatRouteDecisions(
        IReadOnlyList<ClientControllerRouteSource> sources,
        IReadOnlyDictionary<SdlGamepadSource, ControllerRouteIdentity> identities)
    {
        if (sources.Count == 0)
        {
            return "none";
        }

        List<string> values = [];
        foreach (ClientControllerRouteSource source in sources)
        {
            SdlControllerInfo controller = source.Source.Controller;
            ControllerRouteIdentity identity = identities[source.Source];
            values.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"idx={source.ControllerIndex} route=\"{Clean(identity.RouteId)}\" physical=\"{Clean(identity.PhysicalDeviceId)}\" label=\"{Clean(identity.Label)}\" sdl=\"{Clean(controller.Id.Value)}\" source={controller.Source} instance={controller.InstanceId} steam={controller.SteamHandle:x16} vid={controller.VendorId:x4} pid={controller.ProductId:x4} path=\"{Clean(controller.Path)}\" motion={controller.HasMotion}"));
        }

        return string.Join("; ", values);
    }

    private static Dictionary<SdlGamepadSource, ControllerRouteIdentity> CreateRouteIdentities(
        IReadOnlyList<ClientControllerRouteSource> sources,
        IReadOnlyList<SdlControllerInfo> physicalControllers)
    {
        Dictionary<SdlGamepadSource, ControllerRouteIdentity> identities = [];
        foreach (ClientControllerRouteSource source in sources)
        {
            SdlControllerInfo controller = source.Source.Controller;
            SdlControllerInfo? physical = SdlControllerMatcher.FindPhysicalController(
                controller,
                physicalControllers);
            identities[source.Source] = new ControllerRouteIdentity(
                GetRouteId(source.ControllerIndex, controller, physical),
                physical?.Name ?? controller.Name,
                GetPhysicalDeviceId(controller, physical));
        }

        return identities;
    }

    private static string? GetPathControllerId(SdlControllerInfo controller)
    {
        return string.IsNullOrWhiteSpace(controller.Path)
            ? null
            : SdlControllerCatalog.GetPhysicalControllerId(controller);
    }

    private static string FormatController(SdlControllerInfo controller)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"name=\"{Clean(controller.Name)}\" id=\"{Clean(controller.Id.Value)}\" source={controller.Source} instance={controller.InstanceId} steam={controller.SteamHandle:x16} vid={controller.VendorId:x4} pid={controller.ProductId:x4} path=\"{Clean(controller.Path)}\" motion={controller.HasMotion}");
    }

    private static string Clean(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? "none"
            : value.Replace("\"", "'", StringComparison.Ordinal);
    }
}

internal sealed record ClientControllerRoutePlan(
    IReadOnlyList<ClientControllerInfo> Controllers,
    IReadOnlyDictionary<SdlGamepadSource, ControllerRouteIdentity> Identities);

internal readonly record struct ClientControllerRouteSource(
    ushort ControllerIndex,
    SdlGamepadSource Source);

internal sealed record ControllerRouteIdentity(
    string RouteId,
    string Label,
    string? PhysicalDeviceId);
