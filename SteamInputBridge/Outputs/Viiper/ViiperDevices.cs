using System;

namespace SteamInputBridge.Outputs.Viiper;

/// <summary>Identifies VIIPER-created devices that must not feed back into input discovery.</summary>
internal static class ViiperDevices
{
    /// <summary>Returns whether the USB id is a VIIPER virtual controller owned by this app.</summary>
    public static bool IsController(ushort vendorId, ushort productId)
    {
        return vendorId == ViiperXbox360Output.OwnedVendorId &&
            productId == ViiperXbox360Output.OwnedProductId;
    }

    /// <summary>Returns whether the controller looks like a VIIPER device owned by this app.</summary>
    public static bool IsController(
        ushort vendorId,
        ushort productId,
        string? name,
        string? path)
    {
        return IsController(vendorId, productId) ||
            IsOwnedDs4Controller(vendorId, productId, name) ||
            IsOwnedDs4Controller(vendorId, productId, path);
    }

    /// <summary>Returns whether a Raw Input device name is the owned VIIPER mouse.</summary>
    public static bool IsMouseDeviceName(string? deviceName)
    {
        return !string.IsNullOrWhiteSpace(deviceName) &&
            deviceName.Contains(ViiperMouseOutput.OwnedVendorName, StringComparison.OrdinalIgnoreCase) &&
            deviceName.Contains(ViiperMouseOutput.OwnedProductName, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsOwnedDs4Controller(
        ushort vendorId,
        ushort productId,
        string? value)
    {
        // DS4 emulation intentionally uses Sony's real VID/PID. Do not filter
        // by USB id alone or a real original DS4 becomes invisible to routing.
        return vendorId == ViiperDs4Output.OwnedVendorId &&
            productId == ViiperDs4Output.OwnedProductId &&
            !string.IsNullOrWhiteSpace(value) &&
            value.Contains("Steam Input Bridge", StringComparison.OrdinalIgnoreCase);
    }
}
