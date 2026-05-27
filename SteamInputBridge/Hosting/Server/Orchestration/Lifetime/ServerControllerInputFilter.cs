using System;
using System.Collections.Generic;
using System.Text.Json;
using SteamInputBridge.HidHide;
using SteamInputBridge.Inputs.Sdl;
using SteamInputBridge.Outputs.Viiper;

namespace SteamInputBridge.Hosting.Server.Orchestration.Lifetime;

/// <summary>Filters physical controller candidates before they can become route slots.</summary>
internal sealed class ServerControllerInputFilter(
    HidHideDeviceCatalog? hidHideDevices,
    HidHideService? hidHide,
    OwnedVirtualControllerRegistry? ownedVirtualControllers = null)
{
    private static readonly HiddenHidHideDevices EmptyHiddenDevices = new(
        new HashSet<DeviceIdentity>(),
        new HashSet<DeviceIdentity>());

    public bool Allows(SdlControllerInfo controller)
    {
        return CreateSnapshot().Allows(controller);
    }

    public bool Allows(ClientControllerInfo controller)
    {
        return CreateSnapshot().Allows(controller);
    }

    public void Observe(IReadOnlyList<SdlControllerInfo> controllers)
    {
        ownedVirtualControllers?.ObserveControllers(controllers);
    }

    public ServerControllerInputFilterSnapshot CreateSnapshot()
    {
        IReadOnlyList<HidHideDevice> devices = ListHidHideDevices();
        return new ServerControllerInputFilterSnapshot(
            hidHide,
            ownedVirtualControllers,
            devices,
            GetHiddenDevices(devices));
    }

    private IReadOnlyList<HidHideDevice> ListHidHideDevices()
    {
        if (hidHideDevices is null)
        {
            return [];
        }

        try
        {
            return hidHideDevices.ListDevices();
        }
        catch (Exception exception) when (exception is InvalidOperationException or JsonException)
        {
            return [];
        }
    }

    private HiddenHidHideDevices GetHiddenDevices(IReadOnlyList<HidHideDevice> devices)
    {
        if (hidHide is null)
        {
            return EmptyHiddenDevices;
        }

        try
        {
            HashSet<DeviceIdentity> hidden = [];
            HashSet<DeviceIdentity> hiddenContainers = [];
            foreach (string path in hidHide.GetStatus().HiddenDevices)
            {
                if (DeviceIdentity.FromDeviceInstancePath(path) is { } identity)
                {
                    _ = hidden.Add(identity);
                }

                if (ServerControllerInputFilterPaths.FindByInstancePath(devices, path) is { } device &&
                    DeviceIdentity.FromDeviceInstancePath(device.BaseContainerDeviceInstancePath) is { } container)
                {
                    _ = hiddenContainers.Add(container);
                }
            }

            return new HiddenHidHideDevices(hidden, hiddenContainers);
        }
        catch (Exception exception) when (exception is InvalidOperationException or JsonException)
        {
            return EmptyHiddenDevices;
        }
    }
}

internal sealed class ServerControllerInputFilterSnapshot(
    HidHideService? hidHide,
    OwnedVirtualControllerRegistry? ownedVirtualControllers,
    IReadOnlyList<HidHideDevice> devices,
    HiddenHidHideDevices hiddenDevices)
{
    public bool Allows(SdlControllerInfo controller)
    {
        ArgumentNullException.ThrowIfNull(controller);

        if (controller.Source != SdlControllerSource.Physical)
        {
            return true;
        }

        HidHideDevice? device = FindDeviceBySymbolicLink(controller.Path);
        return ownedVirtualControllers?.IsOwned(controller) != true &&
            !IsOwnedVirtualController(controller, device) &&
            !IsForeignHiddenDevice(device);
    }

    public bool Allows(ClientControllerInfo controller)
    {
        ArgumentNullException.ThrowIfNull(controller);

        HidHideDevice? device = FindDevice(controller.PhysicalDeviceId) ??
            FindDevice(controller.PhysicalControllerId);
        return ownedVirtualControllers?.IsOwned(controller) != true &&
            !IsOwnedVirtualController(controller, device) &&
            !IsForeignHiddenDevice(device);
    }

    public bool IsCurrentScopeDevice(SdlControllerInfo controller)
    {
        ArgumentNullException.ThrowIfNull(controller);

        HidHideDevice? device = FindDeviceBySymbolicLink(controller.Path);
        return device is not null &&
            hidHide is not null &&
            hidHide.IsScopeDevice(device.DeviceInstancePath);
    }

    private HidHideDevice? FindDevice(string? controllerId)
    {
        string? path = GetControllerPath(controllerId);
        return path is null ? null : FindDeviceBySymbolicLink(path);
    }

    private HidHideDevice? FindDeviceBySymbolicLink(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        string normalized = ServerControllerInputFilterPaths.Normalize(path);
        foreach (HidHideDevice device in devices)
        {
            if (ServerControllerInputFilterPaths.Normalize(device.SymbolicLink) == normalized &&
                !string.IsNullOrWhiteSpace(device.DeviceInstancePath))
            {
                return device;
            }
        }

        return null;
    }

    private bool IsForeignHiddenDevice(HidHideDevice? device)
    {
        return device is not null &&
            hidHide is not null &&
            IsHiddenDevice(device) &&
            !hidHide.IsScopeDevice(device.DeviceInstancePath);
    }

    private bool IsHiddenDevice(HidHideDevice device)
    {
        bool deviceHidden =
            DeviceIdentity.FromDeviceInstancePath(device.DeviceInstancePath) is { } identity &&
            hiddenDevices.DeviceInstancePaths.Contains(identity);
        bool containerHidden =
            DeviceIdentity.FromDeviceInstancePath(device.BaseContainerDeviceInstancePath) is { } container &&
            hiddenDevices.BaseContainerDeviceInstancePaths.Contains(container);

        // Controllers can expose several HID interfaces. If another tool hid
        // any interface in the same base container, ignore the whole physical
        // controller so we do not duplicate DSX/DS4Windows-owned devices.
        return deviceHidden || containerHidden;
    }

    private static bool IsOwnedVirtualController(
        SdlControllerInfo controller,
        HidHideDevice? device)
    {
        return ViiperDevices.IsController(
            controller.VendorId,
            controller.ProductId,
            controller.Name,
            controller.Path) ||
            IsOwnedHidHideDevice(controller.VendorId, controller.ProductId, device);
    }

    private static bool IsOwnedVirtualController(
        ClientControllerInfo controller,
        HidHideDevice? device)
    {
        return ViiperDevices.IsController(
            controller.VendorId,
            controller.ProductId,
            controller.Label,
            GetControllerPath(controller.PhysicalDeviceId) ?? GetControllerPath(controller.PhysicalControllerId)) ||
            IsOwnedHidHideDevice(controller.VendorId, controller.ProductId, device);
    }

    private static bool IsOwnedHidHideDevice(
        ushort vendorId,
        ushort productId,
        HidHideDevice? device)
    {
        return device is not null &&
            (ViiperDevices.IsController(vendorId, productId, device.FriendlyName, device.SymbolicLink) ||
                ViiperDevices.IsController(vendorId, productId, device.Description, device.DeviceInstancePath));
    }

    private static string? GetControllerPath(string? controllerId)
    {
        const string Prefix = "path:";
        return !string.IsNullOrWhiteSpace(controllerId) &&
            controllerId.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)
            ? controllerId[Prefix.Length..]
            : null;
    }
}

internal static class ServerControllerInputFilterPaths
{
    internal static HidHideDevice? FindByInstancePath(
        IReadOnlyList<HidHideDevice> devices,
        string? deviceInstancePath)
    {
        if (string.IsNullOrWhiteSpace(deviceInstancePath))
        {
            return null;
        }

        string normalized = Normalize(deviceInstancePath);
        foreach (HidHideDevice device in devices)
        {
            if (Normalize(device.DeviceInstancePath) == normalized)
            {
                return device;
            }
        }

        return null;
    }

    internal static string Normalize(string value)
    {
        return value.Replace("\\", "#", StringComparison.Ordinal)
            .Trim()
            .ToUpperInvariant();
    }
}

internal sealed record HiddenHidHideDevices(
    IReadOnlySet<DeviceIdentity> DeviceInstancePaths,
    IReadOnlySet<DeviceIdentity> BaseContainerDeviceInstancePaths);
