using System;
using System.Collections.Generic;
using System.Globalization;
using SteamInputBridge.Inputs.Sdl;
using SteamInputBridge.Outputs.Viiper;

namespace SteamInputBridge.Hosting;

internal static class SdlControllerRoutePolicy
{
    private const ushort ValveVendorId = 0x28de;
    private const ushort SteamVirtualXInputProductId = 0x11ff;

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

    public static bool IsGenericSteamDs4(SdlControllerInfo controller)
    {
        // Steam echoes VIIPER DS4 outputs back to launched clients as generic
        // Steam-routed PS4 controllers. Real original DS4 controllers can look
        // similar, so callers should only treat this shape as loopback when
        // they have more context, such as the initial controller baseline.
        return controller.Source == SdlControllerSource.Steam &&
            controller.VendorId == ViiperDs4Output.OwnedVendorId &&
            controller.ProductId == ViiperDs4Output.OwnedProductId &&
            string.Equals(controller.Name, "PS4 Controller", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(controller.Path) &&
            controller.Path.StartsWith("XInput#", StringComparison.OrdinalIgnoreCase);
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

        // Valve Steam Controllers expose different Steam/physical product ids.
        // SDL does not expose enough identity to pair multiple Valve controllers.
        return exact is not null || vendorId != ValveVendorId
            ? exact
            : FindUnique(
                physicalControllers,
                controller => controller.Source == SdlControllerSource.Physical &&
                    controller.VendorId == ValveVendorId &&
                    string.Equals(controller.Name, name, StringComparison.OrdinalIgnoreCase));
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
}

internal sealed record SdlControllerRouteIdentity(
    string RouteId,
    string Label,
    string? PhysicalDeviceId,
    SdlControllerInfo? PhysicalController);
