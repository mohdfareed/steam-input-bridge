using System;
using System.Globalization;

namespace SteamInputBridge.Tests;

internal static class TestCategories
{
    public const string Dependency = "Dependency";
    public const string Manual = "Manual";
}

internal static class TestEnvironment
{
    public static string? Get(string name)
    {
        string? value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    public static string Require(string name)
    {
        return Get(name) ??
            throw new AssertInconclusiveException($"Set {name} to run this explicit test.");
    }

    public static int GetInt(string name, int defaultValue)
    {
        string? value = Get(name);
        return value is null
            ? defaultValue
            : int.Parse(value, CultureInfo.InvariantCulture);
    }

    public static uint? GetUInt32(string name)
    {
        string? value = Get(name);
        return value is null
            ? null
            : uint.Parse(value, CultureInfo.InvariantCulture);
    }

    public static bool GetBool(string name)
    {
        string? value = Get(name);
        return value is not null &&
            (string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase));
    }
}
