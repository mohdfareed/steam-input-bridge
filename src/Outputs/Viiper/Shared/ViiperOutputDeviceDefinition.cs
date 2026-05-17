using System;
using global::Viiper.Client.Types;

namespace Outputs.Viiper;

internal readonly record struct ViiperOutputDeviceDefinition(
    string DeviceType,
    ushort VendorId,
    ushort ProductId,
    string OwnershipName,
    string DisplayName)
{
    public bool IsOwnedDevice(Device device)
    {
        return string.Equals(device.Type, DeviceType, StringComparison.Ordinal) &&
            string.Equals(device.Vid, FormatUsbId(VendorId), StringComparison.OrdinalIgnoreCase) &&
            string.Equals(device.Pid, FormatUsbId(ProductId), StringComparison.OrdinalIgnoreCase);
    }

    public bool IsOwnedDeviceName(string? deviceName)
    {
        return deviceName?.Contains(OwnedDeviceNameFragment, StringComparison.OrdinalIgnoreCase) == true;
    }

    public bool IsOwnedViiperDeviceName(string? deviceName)
    {
        return IsOwnedDeviceName(deviceName) &&
            deviceName?.Contains("VIIPER", StringComparison.OrdinalIgnoreCase) == true;
    }

    public static string FormatUsbId(ushort value)
    {
        return $"0x{value:x4}";
    }

    private string OwnedDeviceNameFragment => $"VID_{VendorId:X4}&PID_{ProductId:X4}";
}
