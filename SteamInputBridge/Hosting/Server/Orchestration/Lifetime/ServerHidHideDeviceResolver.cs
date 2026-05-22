using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using SteamInputBridge.HidHide;
using SteamInputBridge.Hosting.Server.Pipes;
using SteamInputBridge.Runtime;

namespace SteamInputBridge.Hosting.Server.Orchestration.Lifetime;

internal sealed class ServerHidHideDeviceResolver(
    HidHideService? hidHide,
    HidHideDeviceCatalog? devices,
    ControllerPipeSessions controllerPipes,
    ILogger logger)
{
    public void RegisterApplicationAccess()
    {
        if (hidHide is null)
        {
            return;
        }

        try
        {
            hidHide.AllowCurrentProcess();
            HostingLog.HidHideApplicationAccessRegistered(logger);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or
                ArgumentException or
                System.ComponentModel.Win32Exception or
                System.IO.IOException or
                UnauthorizedAccessException)
        {
            HostingLog.HidHideApplicationAccessFailed(logger, exception.Message);
        }
    }

    public IReadOnlyList<string> GetDevicePaths(ActiveClientRegistryStatus status, Guid clientId)
    {
        _ = status;
        if (devices is null)
        {
            return [];
        }

        HashSet<string> hiddenDevices = new(StringComparer.OrdinalIgnoreCase);
        foreach (ControllerPipeStatus pipe in controllerPipes.GetStatus())
        {
            if (pipe.ClientId != clientId)
            {
                continue;
            }

            foreach (ClientControllerStatus controller in pipe.Controllers)
            {
                string? physicalControllerPath =
                    GetControllerPath(controller.PhysicalDeviceId) ??
                    GetControllerPath(controller.PhysicalControllerId);
                if (devices.FindDeviceInstancePath(physicalControllerPath) is { } path)
                {
                    _ = hiddenDevices.Add(path);
                }
            }
        }

        return [.. hiddenDevices];
    }

    public IReadOnlyList<string> GetDeviceLabels(IReadOnlyList<string> devicePaths)
    {
        if (devices is null || devicePaths.Count == 0)
        {
            return [];
        }

        List<string> labels = [];
        foreach (string devicePath in devicePaths)
        {
            labels.Add(FormatDeviceLabel(devicePath));
        }

        return labels;
    }

    private string FormatDeviceLabel(string devicePath)
    {
        if (devices?.FindDevice(devicePath) is not { } device)
        {
            return ShortenDevicePath(devicePath);
        }

        string name = !string.IsNullOrWhiteSpace(device.FriendlyName)
            ? device.FriendlyName
            : !string.IsNullOrWhiteSpace(device.Product)
            ? device.Product
            : FormatVidPid(devicePath);
        string usage = string.IsNullOrWhiteSpace(device.Usage)
            ? device.Description
            : device.Usage;

        return string.IsNullOrWhiteSpace(usage)
            ? name
            : $"{name} - {usage}";
    }

    private static string FormatVidPid(string devicePath)
    {
        string? vendor = FindHexPart(devicePath, "VID_");
        string? product = FindHexPart(devicePath, "PID_");
        return vendor is not null && product is not null
            ? $"{vendor}:{product}"
            : ShortenDevicePath(devicePath);
    }

    private static string ShortenDevicePath(string devicePath)
    {
        int slash = devicePath.LastIndexOf('\\');
        return slash >= 0 && slash + 1 < devicePath.Length
            ? devicePath[(slash + 1)..]
            : devicePath;
    }

    private static string? FindHexPart(string value, string prefix)
    {
        int index = value.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
        return index < 0 || index + prefix.Length + 4 > value.Length
            ? null
            : value.Substring(index + prefix.Length, 4).ToUpperInvariant();
    }

    private static string? GetControllerPath(string? physicalControllerId)
    {
        if (string.IsNullOrWhiteSpace(physicalControllerId))
        {
            return null;
        }

        const string Prefix = "path:";
        return physicalControllerId.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)
            ? physicalControllerId[Prefix.Length..]
            : null;
    }
}
