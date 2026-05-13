using System;
using Microsoft.Extensions.Logging;

namespace PhysicalMouse.Viiper;

public sealed partial class ViiperPhysicalMouse
{
    // MARK: Logging
    // ========================================================================

    private static class Log
    {
        public static readonly Action<ILogger, uint, Exception?> CreatingDevice =
            LoggerMessage.Define<uint>(
                LogLevel.Information,
                new EventId(1, nameof(CreatingDevice)),
                "Created VIIPER mouse device on bus {BusId}.");

        public static readonly Action<ILogger, uint, string, Exception?> RemovedDevice =
            LoggerMessage.Define<uint, string>(
                LogLevel.Information,
                new EventId(2, nameof(RemovedDevice)),
                "Removed VIIPER mouse device {BusId}/{DeviceId}.");

        public static readonly Action<ILogger, uint, string, Exception?> ConnectedKnownDevice =
            LoggerMessage.Define<uint, string>(
                LogLevel.Information,
                new EventId(3, nameof(ConnectedKnownDevice)),
                "VIIPER mouse device connected ({BusId}/{DeviceId}).");

        public static readonly Action<ILogger, uint, string, Exception?> DisconnectedKnownDevice =
            LoggerMessage.Define<uint, string>(
                LogLevel.Information,
                new EventId(3, nameof(DisconnectedKnownDevice)),
                "VIIPER mouse device disconnected ({BusId}/{DeviceId}).");
    }
}
