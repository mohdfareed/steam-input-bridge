using System;
using System.Collections.Generic;
using System.Globalization;
using SteamInputBridge.Inputs.Sdl;
using SteamInputBridge.Outputs.Viiper;

namespace SteamInputBridge.Hosting;

internal static class SdlControllerRoutePolicy
{
    private const ushort ValveVendorId = 0x28de;

    public static SdlControllerRouteIdentity CreateIdentity(
        ushort controllerIndex,
        SdlControllerInfo controller,
        IReadOnlyList<SdlControllerInfo> physicalControllers)
    {
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(physicalControllers);

        SdlControllerInfo? physical = FindPhysicalController(controller, physicalControllers);
        return new SdlControllerRouteIdentity(
            GetRouteId(controllerIndex, controller, physical),
            physical?.Name ?? controller.Name,
            GetPhysicalDeviceId(controller, physical),
            physical);
    }

    public static bool IsForwardable(SdlControllerInfo controller)
    {
        return !ViiperDevices.IsController(controller.VendorId, controller.ProductId);
    }

    public static string GetPhysicalControllerId(SdlControllerInfo controller)
    {
        ArgumentNullException.ThrowIfNull(controller);
        return !string.IsNullOrWhiteSpace(controller.Path)
            ? $"path:{controller.Path}"
            : $"vidpid:{controller.VendorId:x4}:{controller.ProductId:x4}";
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

    public static SdlControllerInfo? FindPhysicalController(
        SdlControllerInfo steamController,
        IReadOnlyList<SdlControllerInfo> physicalControllers)
    {
        ArgumentNullException.ThrowIfNull(steamController);
        ArgumentNullException.ThrowIfNull(physicalControllers);

        if (steamController.Source != SdlControllerSource.Steam)
        {
            return null;
        }

        SdlControllerInfo? exactPath = string.IsNullOrWhiteSpace(steamController.Path)
            ? null
            : FindUnique(
                physicalControllers,
                controller => controller.Source == SdlControllerSource.Physical &&
                    string.Equals(controller.Path, steamController.Path, StringComparison.OrdinalIgnoreCase));
        if (exactPath is not null)
        {
            return exactPath;
        }

        SdlControllerInfo? exact = FindUnique(
            physicalControllers,
            controller => controller.Source == SdlControllerSource.Physical &&
                controller.VendorId == steamController.VendorId &&
                controller.ProductId == steamController.ProductId);

        // Valve Steam Controllers expose different Steam/physical product ids.
        // SDL does not expose enough identity to pair multiple Valve controllers.
        return exact is not null || steamController.VendorId != ValveVendorId
            ? exact
            : FindUnique(
                physicalControllers,
                controller => controller.Source == SdlControllerSource.Physical &&
                    controller.VendorId == ValveVendorId &&
                    string.Equals(controller.Name, steamController.Name, StringComparison.OrdinalIgnoreCase));
    }

    private static string GetRouteId(
        ushort controllerIndex,
        SdlControllerInfo controller,
        SdlControllerInfo? physical)
    {
        return physical is not null
            ? GetPhysicalControllerId(physical)
            : controller.Source == SdlControllerSource.Steam && controller.SteamHandle != 0
            ? controller.Id.Value
            : !string.IsNullOrWhiteSpace(controller.Path)
            ? GetPhysicalControllerId(controller)
            : string.Create(
                CultureInfo.InvariantCulture,
                $"client:{controllerIndex}:{controller.Source}:{controller.InstanceId}");
    }

    private static string? GetPhysicalDeviceId(SdlControllerInfo controller, SdlControllerInfo? physical)
    {
        return physical is not null
            ? GetPathControllerId(physical)
            : controller.Source == SdlControllerSource.Physical
            ? GetPathControllerId(controller)
            : null;
    }

    private static SdlControllerInfo? FindUnique(
        IReadOnlyList<SdlControllerInfo> controllers,
        Func<SdlControllerInfo, bool> predicate)
    {
        SdlControllerInfo? match = null;
        int count = 0;

        foreach (SdlControllerInfo controller in controllers)
        {
            if (!predicate(controller))
            {
                continue;
            }

            match = controller;
            count++;
        }

        return count == 1 ? match : null;
    }

    private static string? GetPathControllerId(SdlControllerInfo controller)
    {
        return string.IsNullOrWhiteSpace(controller.Path)
            ? null
            : GetPhysicalControllerId(controller);
    }
}

internal sealed record SdlControllerRouteIdentity(
    string RouteId,
    string Label,
    string? PhysicalDeviceId,
    SdlControllerInfo? PhysicalController);
