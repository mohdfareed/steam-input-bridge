using System;
using System.Globalization;
using global::Viiper.Client.Types;

namespace SteamInputBridge.Outputs.Viiper.Shared;

internal sealed record ViiperDeviceDefinition(
    string DeviceType,
    ushort VendorId,
    ushort ProductId,
    string DisplayName)
{
    private const string OwnedDisplayNamePrefix = "Steam Input Bridge - ";

    public bool IsOwnedDevice(Device device)
    {
        return string.Equals(device.Type, DeviceType, StringComparison.Ordinal) &&
            string.Equals(device.Vid, FormatUsbId(VendorId), StringComparison.OrdinalIgnoreCase) &&
            string.Equals(device.Pid, FormatUsbId(ProductId), StringComparison.OrdinalIgnoreCase);
    }

    public static string FormatOwnedDisplayName(string displayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        string trimmed = displayName.Trim();
        return trimmed.StartsWith(OwnedDisplayNamePrefix, StringComparison.Ordinal)
            ? trimmed
            : OwnedDisplayNamePrefix + trimmed;
    }

    public static string FormatUsbId(ushort value)
    {
        return $"0x{value.ToString("x4", CultureInfo.InvariantCulture)}";
    }
}
