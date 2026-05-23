using System;
using System.Collections.Generic;
using System.Linq;

namespace SteamInputBridge.HidHide;

/// <summary>Devices hidden from receiver applications while matching clients are running.</summary>
internal sealed record HidHideScope(
    IReadOnlyList<string> DeviceInstancePaths,
    IReadOnlyList<string> ApplicationPaths)
{
    /// <summary>Creates a normalized HidHide scope.</summary>
    public static HidHideScope Create(
        IEnumerable<string> deviceInstancePaths,
        IEnumerable<string> applicationPaths)
    {
        return new HidHideScope(
            Normalize(deviceInstancePaths),
            Normalize(applicationPaths));
    }

    /// <summary>Gets whether this scope has no useful HidHide work.</summary>
    public bool IsEmpty => DeviceInstancePaths.Count == 0 || ApplicationPaths.Count == 0;

    /// <summary>Checks whether two normalized scopes target the same devices and applications.</summary>
    public bool HasSameValues(HidHideScope other)
    {
        ArgumentNullException.ThrowIfNull(other);

        return HasSameValues(DeviceInstancePaths, other.DeviceInstancePaths) &&
            HasSameValues(ApplicationPaths, other.ApplicationPaths);
    }

    private static string[] Normalize(IEnumerable<string> values)
    {
        return [.. values
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)];
    }

    private static bool HasSameValues(IReadOnlyList<string> left, IReadOnlyList<string> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (int i = 0; i < left.Count; i++)
        {
            if (!string.Equals(left[i], right[i], StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }
}
