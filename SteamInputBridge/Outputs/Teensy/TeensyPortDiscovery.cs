using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using Microsoft.Win32;

namespace SteamInputBridge.Outputs.Teensy;

internal class TeensyPortDiscovery
{
    public virtual IReadOnlyList<string> GetCandidatePorts(string? configuredPort)
    {
        if (!string.IsNullOrWhiteSpace(configuredPort))
        {
            return [configuredPort.Trim()];
        }

        string[] ports;
        try
        {
            ports = SerialPort.GetPortNames();
        }
        catch (Exception exception) when (IsPortEnumerationFailure(exception))
        {
            return [];
        }

        Array.Sort(ports, StringComparer.OrdinalIgnoreCase);
        return OrderCandidatePorts(ports, FindLikelyTeensyPorts(), configuredPort: null);
    }

    public virtual bool PortExists(string portName)
    {
        string[] ports;
        try
        {
            ports = SerialPort.GetPortNames();
        }
        catch (Exception exception) when (IsPortEnumerationFailure(exception))
        {
            return true;
        }

        foreach (string current in ports)
        {
            if (string.Equals(current, portName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsPortEnumerationFailure(Exception exception)
    {
        return exception is IOException or InvalidOperationException or UnauthorizedAccessException;
    }

    internal static string? NormalizeConfiguredPort(string? configuredPort)
    {
        return string.IsNullOrWhiteSpace(configuredPort) ? null : configuredPort.Trim();
    }

    internal static IReadOnlyList<string> OrderCandidatePorts(
        IReadOnlyList<string> ports,
        IReadOnlyList<string> likelyTeensyPorts,
        string? configuredPort)
    {
        if (!string.IsNullOrWhiteSpace(configuredPort))
        {
            return [configuredPort.Trim()];
        }

        List<string> ordered = [];
        AddRange(ordered, likelyTeensyPorts);
        AddRange(ordered, ports);
        return ordered;
    }

    private static void AddRange(List<string> ordered, IReadOnlyList<string> ports)
    {
        foreach (string port in ports)
        {
            if (!string.IsNullOrWhiteSpace(port) &&
                !ordered.Exists(existing => string.Equals(existing, port, StringComparison.OrdinalIgnoreCase)))
            {
                ordered.Add(port.Trim());
            }
        }
    }

    private static string[] FindLikelyTeensyPorts()
    {
        const string enumPath = @"SYSTEM\CurrentControlSet\Enum\USB";
        using RegistryKey? root = Registry.LocalMachine.OpenSubKey(enumPath);
        if (root is null)
        {
            return [];
        }

        HashSet<string> ports = new(StringComparer.OrdinalIgnoreCase);
        foreach (string vendorKeyName in root.GetSubKeyNames())
        {
            if (!vendorKeyName.Contains("VID_16C0", StringComparison.OrdinalIgnoreCase) &&
                !vendorKeyName.Contains("TEENSY", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            using RegistryKey? vendorKey = root.OpenSubKey(vendorKeyName);
            if (vendorKey is null)
            {
                continue;
            }

            foreach (string instanceKeyName in vendorKey.GetSubKeyNames())
            {
                using RegistryKey? instanceKey = vendorKey.OpenSubKey(instanceKeyName);
                using RegistryKey? parameters = instanceKey?.OpenSubKey("Device Parameters");
                if (parameters?.GetValue("PortName") is string portName && !string.IsNullOrWhiteSpace(portName))
                {
                    _ = ports.Add(portName.Trim());
                }
            }
        }

        string[] resolved = [.. ports];
        Array.Sort(resolved, StringComparer.OrdinalIgnoreCase);
        return resolved;
    }
}
