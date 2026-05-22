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

    /// <summary>Returns whether a Raw Input device name is the owned VIIPER mouse.</summary>
    public static bool IsMouseDeviceName(string? deviceName)
    {
        return !string.IsNullOrWhiteSpace(deviceName) &&
            deviceName.Contains(ViiperMouseOutput.OwnedVendorName, StringComparison.OrdinalIgnoreCase) &&
            deviceName.Contains(ViiperMouseOutput.OwnedProductName, StringComparison.OrdinalIgnoreCase);
    }
}
