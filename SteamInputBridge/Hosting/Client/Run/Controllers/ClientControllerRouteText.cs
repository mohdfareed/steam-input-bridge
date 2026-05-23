using System;
using System.Collections.Generic;
using System.Globalization;
using SteamInputBridge.Inputs.Sdl;

namespace SteamInputBridge.Hosting.Client.Run.Controllers;

internal static class ClientControllerRouteText
{
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
        IReadOnlyDictionary<SdlGamepadSource, SdlControllerRouteIdentity> identities)
    {
        if (sources.Count == 0)
        {
            return "none";
        }

        List<string> values = [];
        foreach (ClientControllerRouteSource source in sources)
        {
            SdlControllerInfo controller = source.Source.Controller;
            SdlControllerRouteIdentity identity = identities[source.Source];
            values.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"idx={source.ControllerIndex} route=\"{Clean(identity.RouteId)}\" physical=\"{Clean(identity.PhysicalDeviceId)}\" label=\"{Clean(identity.Label)}\" sdl=\"{Clean(controller.Id.Value)}\" source={controller.Source} instance={controller.InstanceId} steam={controller.SteamHandle:x16} vid={controller.VendorId:x4} pid={controller.ProductId:x4} path=\"{Clean(controller.Path)}\" motion={controller.HasMotion} touchpad={controller.HasTouchpad}"));
        }

        return string.Join("; ", values);
    }

    private static string FormatController(SdlControllerInfo controller)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"name=\"{Clean(controller.Name)}\" id=\"{Clean(controller.Id.Value)}\" source={controller.Source} instance={controller.InstanceId} steam={controller.SteamHandle:x16} vid={controller.VendorId:x4} pid={controller.ProductId:x4} path=\"{Clean(controller.Path)}\" motion={controller.HasMotion} touchpad={controller.HasTouchpad}");
    }

    private static string Clean(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? "none"
            : value.Replace("\"", "'", StringComparison.Ordinal);
    }
}
