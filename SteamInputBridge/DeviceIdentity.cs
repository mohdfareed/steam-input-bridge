using System;

namespace SteamInputBridge;

internal enum DeviceIdentityKind
{
    DevicePath,
    DeviceInstancePath,
    SteamHandle,
}

internal readonly record struct DeviceIdentity(DeviceIdentityKind Kind, string Value)
{
    public static DeviceIdentity? FromDevicePath(string? path)
    {
        return string.IsNullOrWhiteSpace(path)
            ? null
            : new DeviceIdentity(DeviceIdentityKind.DevicePath, NormalizePath(path));
    }

    public static DeviceIdentity? FromDeviceInstancePath(string? path)
    {
        return string.IsNullOrWhiteSpace(path)
            ? null
            : new DeviceIdentity(DeviceIdentityKind.DeviceInstancePath, NormalizePath(path));
    }

    public static DeviceIdentity? FromRouteId(string? routeId)
    {
        const string PathPrefix = "path:";
        const string SteamPrefix = "steam:";
        return string.IsNullOrWhiteSpace(routeId)
            ? null
            : routeId.StartsWith(PathPrefix, StringComparison.OrdinalIgnoreCase)
            ? FromDevicePath(routeId[PathPrefix.Length..])
            : routeId.StartsWith(SteamPrefix, StringComparison.OrdinalIgnoreCase)
            ? new DeviceIdentity(DeviceIdentityKind.SteamHandle, routeId[SteamPrefix.Length..].ToUpperInvariant())
            : null;
    }

    private static string NormalizePath(string path)
    {
        return path.Trim().Replace('/', '\\').ToUpperInvariant();
    }
}
