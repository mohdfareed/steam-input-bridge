using System;
using System.Collections.Generic;
using System.Linq;

namespace SteamInputBridge.HidHide;

/// <summary>Devices hidden from applications while a profile is active.</summary>
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

    private static string[] Normalize(IEnumerable<string> values)
    {
        return [.. values
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)];
    }
}
