using System;
using System.Collections.Generic;
using System.Linq;

namespace SteamInputBridge.HidHide;

/// <summary>Devices hidden while matching clients are running.</summary>
internal sealed record HidHideScope(
    IReadOnlyList<string> DeviceInstancePaths)
{
    /// <summary>Creates a normalized HidHide scope.</summary>
    public static HidHideScope Create(IEnumerable<string> deviceInstancePaths)
    {
        return new HidHideScope(Normalize(deviceInstancePaths));
    }

    /// <summary>Gets whether this scope has no useful HidHide work.</summary>
    public bool IsEmpty => DeviceInstancePaths.Count == 0;

    /// <summary>Checks whether two normalized scopes target the same devices.</summary>
    public bool HasSameValues(HidHideScope other)
    {
        ArgumentNullException.ThrowIfNull(other);

        return DeviceInstancePaths.SequenceEqual(
            other.DeviceInstancePaths,
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Gets whether the normalized scope contains a device path.</summary>
    public bool Contains(string deviceInstancePath)
    {
        return DeviceInstancePaths.Contains(deviceInstancePath, StringComparer.OrdinalIgnoreCase);
    }

    private static string[] Normalize(IEnumerable<string> values)
    {
        return [.. values
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)];
    }
}
