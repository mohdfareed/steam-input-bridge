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
    private static readonly IReadOnlySet<DeviceIdentity> EmptyHiddenDevices = new HashSet<DeviceIdentity>();

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
        return new ServerControllerInputFilterSnapshot(
            hidHideDevices,
            hidHide,
            ownedVirtualControllers,
            GetHiddenDevices());
    }

    private IReadOnlySet<DeviceIdentity> GetHiddenDevices()
    {
        if (hidHide is null)
        {
            return EmptyHiddenDevices;
        }

        try
        {
            HashSet<DeviceIdentity> devices = [];
            foreach (string device in hidHide.GetStatus().HiddenDevices)
            {
                if (DeviceIdentity.FromDeviceInstancePath(device) is { } identity)
                {
                    _ = devices.Add(identity);
                }
            }

            return devices;
        }
        catch (Exception exception) when (exception is InvalidOperationException or JsonException)
        {
            return EmptyHiddenDevices;
        }
    }
}

internal sealed class ServerControllerInputFilterSnapshot(
    HidHideDeviceCatalog? hidHideDevices,
    HidHideService? hidHide,
    OwnedVirtualControllerRegistry? ownedVirtualControllers,
    IReadOnlySet<DeviceIdentity> hiddenDevices)
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

        try
        {
            return hidHideDevices?.FindDeviceBySymbolicLink(path);
        }
        catch (Exception exception) when (exception is InvalidOperationException or JsonException)
        {
            return null;
        }
    }

    private bool IsForeignHiddenDevice(HidHideDevice? device)
    {
        return device is not null &&
            hidHide is not null &&
            DeviceIdentity.FromDeviceInstancePath(device.DeviceInstancePath) is { } identity &&
            hiddenDevices.Contains(identity) &&
            !hidHide.IsScopeDevice(device.DeviceInstancePath);
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
