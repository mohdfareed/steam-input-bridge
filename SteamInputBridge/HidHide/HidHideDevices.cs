using System;
using System.Collections.Generic;
using System.Text.Json;

namespace SteamInputBridge.HidHide;

/// <summary>HidHide device reported by HidHideCLI.</summary>
internal sealed record HidHideDevice(
    bool Present,
    bool GamingDevice,
    string FriendlyName,
    string Vendor,
    string Product,
    string Usage,
    string Description,
    string SymbolicLink,
    string DeviceInstancePath,
    string BaseContainerDeviceInstancePath);

/// <summary>Lists HidHide devices and matches them to transport paths.</summary>
internal sealed class HidHideDeviceCatalog(IHidHideCommandRunner runner)
{
    /// <summary>Lists HidHide devices.</summary>
    public IReadOnlyList<HidHideDevice> ListDevices()
    {
        string output = runner.Run(["--dev-all"]);
        using JsonDocument document = JsonDocument.Parse(output);
        List<HidHideDevice> devices = [];
        foreach (JsonElement group in document.RootElement.EnumerateArray())
        {
            if (!group.TryGetProperty("devices", out JsonElement children))
            {
                continue;
            }

            foreach (JsonElement device in children.EnumerateArray())
            {
                devices.Add(new HidHideDevice(
                    GetBool(device, "present"),
                    GetBool(device, "gamingDevice"),
                    GetString(group, "friendlyName"),
                    GetString(device, "vendor"),
                    GetString(device, "product"),
                    GetString(device, "usage"),
                    GetString(device, "description"),
                    GetString(device, "symbolicLink"),
                    GetString(device, "deviceInstancePath"),
                    GetString(device, "baseContainerDeviceInstancePath")));
            }
        }

        return devices;
    }

    /// <summary>Finds a HidHide device instance path by symbolic device path.</summary>
    public string? FindDeviceInstancePath(string? symbolicLink)
    {
        return FindDeviceBySymbolicLink(symbolicLink)?.DeviceInstancePath;
    }

    /// <summary>Finds a HidHide device by symbolic device path.</summary>
    public HidHideDevice? FindDeviceBySymbolicLink(string? symbolicLink)
    {
        if (string.IsNullOrWhiteSpace(symbolicLink))
        {
            return null;
        }

        string normalized = NormalizeDevicePath(symbolicLink);
        foreach (HidHideDevice device in ListDevices())
        {
            if (NormalizeDevicePath(device.SymbolicLink) == normalized &&
                !string.IsNullOrWhiteSpace(device.DeviceInstancePath))
            {
                return device;
            }
        }

        return null;
    }

    /// <summary>Finds a HidHide device by device instance path.</summary>
    public HidHideDevice? FindDevice(string? deviceInstancePath)
    {
        if (string.IsNullOrWhiteSpace(deviceInstancePath))
        {
            return null;
        }

        string normalized = NormalizeDevicePath(deviceInstancePath);
        foreach (HidHideDevice device in ListDevices())
        {
            if (NormalizeDevicePath(device.DeviceInstancePath) == normalized)
            {
                return device;
            }
        }

        return null;
    }

    private static string GetString(JsonElement element, string property)
    {
        return element.TryGetProperty(property, out JsonElement value)
            ? value.GetString() ?? string.Empty
            : string.Empty;
    }

    private static bool GetBool(JsonElement element, string property)
    {
        return element.TryGetProperty(property, out JsonElement value) &&
            value.ValueKind == JsonValueKind.True;
    }

    private static string NormalizeDevicePath(string value)
    {
        return value.Replace("\\", "#", StringComparison.Ordinal)
            .Trim()
            .ToUpperInvariant();
    }
}
