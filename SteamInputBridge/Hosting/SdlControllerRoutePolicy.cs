using System;
using System.Collections.Generic;
using System.Globalization;
using SteamInputBridge.Inputs.Sdl;
using SteamInputBridge.Outputs.Viiper;

namespace SteamInputBridge.Hosting;

internal static class SdlControllerRoutePolicy
{
    private const ushort ValveVendorId = 0x28de;
    private const ushort SonyVendorId = 0x054c;
    private const ushort SteamControllerProductId = 0x1302;
    private const ushort SteamVirtualXInputProductId = 0x11ff;
    private const string DualSenseName = "DualSense";
    private const string SteamControllerName = "Steam Controller";

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
        return !ViiperDevices.IsController(controller.VendorId, controller.ProductId, controller.Name, controller.Path) &&
            !IsSteamVirtualXInputFallback(controller);
    }

    public static bool IsValveVirtualXInput(SdlControllerInfo controller)
    {
        return controller.VendorId == ValveVendorId &&
            controller.ProductId == SteamVirtualXInputProductId &&
            !string.IsNullOrWhiteSpace(controller.Path) &&
            controller.Path.StartsWith("XInput#", StringComparison.OrdinalIgnoreCase);
    }

    public static bool CanOwnOutputWithoutPhysical(ClientControllerInfo controller)
    {
        ArgumentNullException.ThrowIfNull(controller);

        // A real Steam Controller can be visible only as Steam's client-local
        // SDL stream. Allow that exact identity to create an output slot, while
        // keeping unresolved generic Steam/DS4 echoes from creating loopbacks.
        return IsSteamRouteId(controller.PhysicalControllerId) &&
            string.IsNullOrWhiteSpace(controller.PhysicalDeviceId) &&
            controller.VendorId == ValveVendorId &&
            controller.ProductId == SteamControllerProductId &&
            string.Equals(controller.Label, SteamControllerName, StringComparison.OrdinalIgnoreCase);
    }

    public static string GetPhysicalControllerId(SdlControllerInfo controller)
    {
        ArgumentNullException.ThrowIfNull(controller);
        return !string.IsNullOrWhiteSpace(controller.Path)
            ? $"path:{controller.Path}"
            : $"vidpid:{controller.VendorId:x4}:{controller.ProductId:x4}";
    }

    public static bool IsSameConnectedController(
        SdlControllerInfo source,
        SdlControllerInfo current)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(current);

        // Steam can reuse SDL ids while swapping the controller identity behind
        // them during virtual-device rebuilds. Treat that as stale and reopen.
        return current.InstanceId == source.InstanceId &&
            current.Source == source.Source &&
            current.SteamHandle == source.SteamHandle &&
            current.VendorId == source.VendorId &&
            current.ProductId == source.ProductId &&
            string.Equals(current.Name, source.Name, StringComparison.Ordinal) &&
            string.Equals(current.Path, source.Path, StringComparison.OrdinalIgnoreCase);
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
        return exactPath ?? FindPhysicalControllerByDeviceIdentity(
            steamController.VendorId,
            steamController.ProductId,
            steamController.Name,
            physicalControllers);
    }

    public static SdlControllerInfo? FindPhysicalControllerByDeviceIdentity(
        ushort vendorId,
        ushort productId,
        string? name,
        IReadOnlyList<SdlControllerInfo> physicalControllers)
    {
        ArgumentNullException.ThrowIfNull(physicalControllers);

        return vendorId == 0
            ? null
            : FindPhysicalControllerByDeviceIdentityCore(vendorId, productId, name, physicalControllers);
    }

    public static bool CanBePhysicalCounterpart(
        ushort vendorId,
        ushort productId,
        string? name,
        SdlControllerInfo physicalController)
    {
        ArgumentNullException.ThrowIfNull(physicalController);

        if (physicalController.Source != SdlControllerSource.Physical || vendorId == 0)
        {
            return false;
        }

        bool exactMatch = physicalController.VendorId == vendorId &&
            productId != 0 &&
            physicalController.ProductId == productId;

        return exactMatch || (vendorId == ValveVendorId
            ? physicalController.VendorId == ValveVendorId &&
                string.Equals(physicalController.Name, name, StringComparison.OrdinalIgnoreCase)
            : vendorId == SonyVendorId &&
            physicalController.VendorId == SonyVendorId &&
            IsDualSenseName(name) &&
            IsDualSenseName(physicalController.Name));
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

    private static SdlControllerInfo? FindPhysicalControllerByDeviceIdentityCore(
        ushort vendorId,
        ushort productId,
        string? name,
        IReadOnlyList<SdlControllerInfo> physicalControllers)
    {
        SdlControllerInfo? exact = FindUnique(
            physicalControllers,
            controller => controller.Source == SdlControllerSource.Physical &&
                controller.VendorId == vendorId &&
                controller.ProductId == productId);

        if (exact is not null)
        {
            return exact;
        }

        // Steam Controllers expose different Steam/physical product ids. SDL
        // does not expose enough identity to pair multiple Valve controllers.
        if (vendorId == ValveVendorId)
        {
            return FindUnique(
                physicalControllers,
                controller => controller.Source == SdlControllerSource.Physical &&
                    controller.VendorId == ValveVendorId &&
                    string.Equals(controller.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        // Steam Input can report a normal DualSense id while host SDL sees a
        // DualSense Edge physical id. Pair only one host-visible DualSense so
        // we keep Steam's remapped buttons and fill missing physical features
        // without guessing between multiple Sony controllers.
        return vendorId == SonyVendorId && IsDualSenseName(name)
            ? FindUnique(
                physicalControllers,
                controller => controller.Source == SdlControllerSource.Physical &&
                    controller.VendorId == SonyVendorId &&
                    IsDualSenseName(controller.Name))
            : null;
    }

    private static string? GetPathControllerId(SdlControllerInfo controller)
    {
        return string.IsNullOrWhiteSpace(controller.Path)
            ? null
            : GetPhysicalControllerId(controller);
    }

    private static bool IsSteamVirtualXInputFallback(SdlControllerInfo controller)
    {
        // Steam can temporarily expose its virtual XInput fallback as an SDL
        // Physical device with 28de:11ff and an XInput#N path. XInput#N changes
        // order/count during Steam Input rebuilds, so it is not a trusted
        // physical controller route id and must not create VIIPER slots.
        return IsValveVirtualXInput(controller) && controller.Source == SdlControllerSource.Physical;
    }

    private static bool IsSteamRouteId(string routeId)
    {
        return routeId.StartsWith("steam:", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDualSenseName(string? name)
    {
        return name?.Contains(DualSenseName, StringComparison.OrdinalIgnoreCase) == true;
    }
}

internal sealed record SdlControllerRouteIdentity(
    string RouteId,
    string Label,
    string? PhysicalDeviceId,
    SdlControllerInfo? PhysicalController);
