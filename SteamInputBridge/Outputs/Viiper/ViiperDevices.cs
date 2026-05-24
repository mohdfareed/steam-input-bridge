using System;

namespace SteamInputBridge.Outputs.Viiper;

/// <summary>Identifies VIIPER-created devices that must not feed back into input discovery.</summary>
internal static class ViiperDevices
{
    /// <summary>Returns whether the controller looks like a VIIPER device owned by this app.</summary>
    public static bool IsController(
        ushort vendorId,
        ushort productId,
        string? name,
        string? path)
    {
        return IsOwnedController(vendorId, productId, name) ||
            IsOwnedController(vendorId, productId, path);
    }

    /// <summary>Returns whether a Raw Input device name is the owned VIIPER mouse.</summary>
    public static bool IsMouseDeviceName(string? deviceName)
    {
        return !string.IsNullOrWhiteSpace(deviceName) &&
            deviceName.Contains(ViiperMouseOutput.OwnedVendorName, StringComparison.OrdinalIgnoreCase) &&
            deviceName.Contains(ViiperMouseOutput.OwnedProductName, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsOwnedController(
        ushort vendorId,
        ushort productId,
        string? value)
    {
        // Xbox and DS4 emulation both use real USB ids. Filtering by VID/PID
        // alone either drops real controllers or relies on the user's current
        // hardware setup. Require an app-owned string when the platform gives
        // us one; otherwise the server-side route resolver must reject echoes.
        return IsOwnedControllerId(vendorId, productId) &&
            !string.IsNullOrWhiteSpace(value) &&
            value.Contains("Steam Input Bridge", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsOwnedControllerId(ushort vendorId, ushort productId)
    {
        return (vendorId == ViiperXbox360Output.OwnedVendorId &&
                productId == ViiperXbox360Output.OwnedProductId) ||
            (vendorId == ViiperDs4Output.OwnedVendorId &&
                productId == ViiperDs4Output.OwnedProductId);
    }
}
