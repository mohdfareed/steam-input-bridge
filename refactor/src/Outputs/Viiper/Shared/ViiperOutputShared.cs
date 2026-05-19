using System;
using System.Globalization;
using global::Viiper.Client.Types;
using Microsoft.Extensions.Logging;

namespace VirtualMouse.Outputs.Viiper;

// MARK: Models
// ============================================================================

internal sealed record ViiperOutputDeviceDefinition(
    string DeviceType,
    ushort VendorId,
    ushort ProductId,
    string DisplayName)
{
    public bool IsOwnedDevice(Device device)
    {
        return string.Equals(device.Type, DeviceType, StringComparison.Ordinal) &&
            string.Equals(device.Vid, FormatUsbId(VendorId), StringComparison.OrdinalIgnoreCase) &&
            string.Equals(device.Pid, FormatUsbId(ProductId), StringComparison.OrdinalIgnoreCase);
    }

    public static string FormatUsbId(ushort value)
    {
        return $"0x{value.ToString("x4", CultureInfo.InvariantCulture)}";
    }
}

// MARK: Logging
// ============================================================================

internal static class ViiperOutputLog
{
    private static readonly Action<ILogger, string, uint, Exception?> CreatedDeviceMessage =
        LoggerMessage.Define<string, uint>(
            LogLevel.Information,
            new EventId(1, nameof(CreatedDevice)),
            "Created VIIPER {Name} device on bus {BusId}.");

    private static readonly Action<ILogger, string, string, uint, Exception?> ConnectedDeviceMessage =
        LoggerMessage.Define<string, string, uint>(
            LogLevel.Information,
            new EventId(2, nameof(ConnectedDevice)),
            "Connected VIIPER {Name} device {DeviceId} on bus {BusId}.");

    private static readonly Action<ILogger, string, string, uint, Exception?> RemovedDeviceMessage =
        LoggerMessage.Define<string, string, uint>(
            LogLevel.Information,
            new EventId(3, nameof(RemovedDevice)),
            "Removed VIIPER {Name} device {DeviceId} from bus {BusId}.");

    private static readonly Action<ILogger, string, string, uint, Exception?> DisconnectedDeviceMessage =
        LoggerMessage.Define<string, string, uint>(
            LogLevel.Warning,
            new EventId(4, nameof(DisconnectedDevice)),
            "VIIPER {Name} device {DeviceId} disconnected from bus {BusId}.");

    public static void CreatedDevice(ILogger? logger, string name, uint busId)
    {
        if (logger is not null)
        {
            CreatedDeviceMessage(logger, name, busId, null);
        }
    }

    public static void ConnectedDevice(ILogger? logger, string name, uint busId, string deviceId)
    {
        if (logger is not null)
        {
            ConnectedDeviceMessage(logger, name, deviceId, busId, null);
        }
    }

    public static void RemovedDevice(ILogger? logger, string name, uint busId, string deviceId)
    {
        if (logger is not null)
        {
            RemovedDeviceMessage(logger, name, deviceId, busId, null);
        }
    }

    public static void DisconnectedDevice(ILogger? logger, string name, uint busId, string deviceId)
    {
        if (logger is not null)
        {
            DisconnectedDeviceMessage(logger, name, deviceId, busId, null);
        }
    }
}
