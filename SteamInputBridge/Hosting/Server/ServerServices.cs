using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SteamInputBridge.Forwarding.Controller;
using SteamInputBridge.Forwarding.Controller.Routing;
using SteamInputBridge.Forwarding.Mouse;
using SteamInputBridge.HidHide;
using SteamInputBridge.Hosting.Server.Orchestration.Lifetime;
using SteamInputBridge.Outputs.Teensy;
using SteamInputBridge.Outputs.Viiper;
using SteamInputBridge.Runtime;
using SteamInputBridge.Runtime.Audio;
using SteamInputBridge.Settings;
using SteamInputBridge.Settings.Profiles;
using SteamInputBridge.Shortcuts;
using SteamInputBridge.Steam.GameCatalog;
using ForwardingMouseOutput = SteamInputBridge.Forwarding.Mouse.MouseOutput;

namespace SteamInputBridge.Hosting.Server;

/// <summary>Dependency injection registration for the local server.</summary>
public static class ServerServices
{
    /// <summary>Adds the local server.</summary>
    public static IServiceCollection AddApplicationServer(this IServiceCollection services)
    {
        // Runtime state
        _ = services.AddSingleton<ActiveClientRegistry>();

        // HidHide firewall
        _ = services.AddSingleton<IHidHideCommandRunner>(static services =>
            new HidHideCliRunner(services.GetRequiredService<IOptions<HidHideSettings>>().Value.CliPath));
        _ = services.AddSingleton<HidHideDeviceCatalog>();
        _ = services.AddSingleton(static services =>
        {
            HidHideSettings settings = services.GetRequiredService<IOptions<HidHideSettings>>().Value;
            return new HidHideService(
                services.GetRequiredService<IHidHideCommandRunner>(),
                services.GetRequiredService<ILogger<HidHideService>>(),
                getApplicationAccessPaths: () =>
                    GetHidHideApplicationAccessPaths(settings.CliPath),
                ownedScopeStore: HidHideOwnedScopeStore.CreateDefault());
        });

        // Keyboard shortcuts
        _ = services.AddSingleton<IKeyboardShortcutListener, GlobalKeyboardShortcutListener>();
        _ = services.AddSingleton<IMicrophoneControl>(
            static _ => OperatingSystem.IsWindows()
                ? new WindowsMicrophoneControl()
                : new NoopMicrophoneControl());

        // Virtual outputs
        _ = services.AddSingleton(static services =>
        {
            ViiperSettings settings = services.GetRequiredService<IOptions<ViiperSettings>>().Value;
            ILoggerFactory loggerFactory = services.GetRequiredService<ILoggerFactory>();
            return new ViiperOutputFactory(new ViiperOptions
            {
                Host = settings.Host,
                Port = settings.Port,
                Logger = loggerFactory.CreateLogger<ViiperOutputFactory>(),
            });
        });
        _ = services.AddSingleton<OwnedVirtualControllerRegistry>();
        _ = services.AddSingleton<IControllerOutputFactory>(
            static services => new TrackingControllerOutputFactory(
                services.GetRequiredService<ViiperOutputFactory>(),
                services.GetRequiredService<OwnedVirtualControllerRegistry>()));
        _ = services.AddSingleton<TeensyOutputFactory>();
        _ = services.AddSingleton<ServerMouseOutputFactory>();
        _ = services.AddSingleton<IMouseOutputFactory>(
            static services => services.GetRequiredService<ServerMouseOutputFactory>());

        // Forwarding state
        _ = services.AddSingleton<ControllerBroker>();
        _ = services.AddSingleton<MouseBroker>();

        // Server orchestration
        _ = services.AddSingleton<ServerShortcutService>();
        _ = services.AddSingleton(static services =>
        {
            ViiperOutputFactory viiper = services.GetRequiredService<ViiperOutputFactory>();
            HidHideSettings hidHideSettings = services.GetRequiredService<IOptions<HidHideSettings>>().Value;
            return new ServerService(
                services.GetRequiredService<ILogger<ServerService>>(),
                services.GetService<SettingsFile>(),
                services.GetService<ProfilesService>(),
                services.GetRequiredService<ActiveClientRegistry>(),
                activeClients: null,
                services.GetRequiredService<ControllerBroker>(),
                services.GetRequiredService<MouseBroker>(),
                hidHideSettings.Enabled ? services.GetRequiredService<HidHideService>() : null,
                hidHideSettings.Enabled ? services.GetRequiredService<HidHideDeviceCatalog>() : null,
                services.GetRequiredService<ServerShortcutService>(),
                ownedVirtualControllers: services.GetRequiredService<OwnedVirtualControllerRegistry>(),
                startupCleanup: viiper.ReclaimDevicesAsync);
        });
        return services;
    }

    private sealed class ServerMouseOutputFactory(ViiperOutputFactory viiper, TeensyOutputFactory teensy)
        : IMouseOutputFactory
    {
        public IMouseOutput Connect(ForwardingMouseOutput output)
        {
            return output switch
            {
                ForwardingMouseOutput.Viiper => viiper.Connect(output),
                ForwardingMouseOutput.Teensy => teensy.Connect(output),
                ForwardingMouseOutput.None => throw new NotSupportedException("None is not a mouse output."),
                _ => throw new NotSupportedException($"Unsupported mouse output: {output}."),
            };
        }
    }

    private static List<string> GetHidHideApplicationAccessPaths(string cliPath)
    {
        List<string> paths = [];
        AddPath(paths, Environment.ProcessPath);
        AddPath(paths, cliPath);
        AddSteamPaths(paths);

        return paths;
    }

    private static void AddSteamPaths(List<string> paths)
    {
        string? steamExe = FindProcessPath("steam");
        AddExistingPath(paths, steamExe);

        string? steamPath = Path.GetDirectoryName(steamExe) ??
            FindDefaultSteamPath() ??
            SteamLocator.FindSteamPath();
        if (string.IsNullOrWhiteSpace(steamPath))
        {
            return;
        }

        // Steam Input is owned by the Steam client process and may use the
        // Steam service for device access. Do not allowlist steamwebhelper.exe;
        // it is the CEF UI process, not the controller reader.
        AddExistingPath(paths, Path.Combine(steamPath, "steam.exe"));
        AddExistingPath(paths, Path.Combine(steamPath, "bin", "SteamService.exe"));
    }

    private static string? FindProcessPath(string processName)
    {
        foreach (Process process in Process.GetProcessesByName(processName))
        {
            try
            {
                return process.MainModule?.FileName;
            }
            catch (Exception exception) when (
                exception is InvalidOperationException or
                    NotSupportedException or
                    System.ComponentModel.Win32Exception)
            {
            }
            finally
            {
                process.Dispose();
            }
        }

        return null;
    }

    private static string? FindDefaultSteamPath()
    {
        string path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            "Steam");
        return Directory.Exists(path) ? path : null;
    }

    private static void AddExistingPath(List<string> paths, string? path)
    {
        if (!string.IsNullOrWhiteSpace(path) &&
            File.Exists(path))
        {
            AddPath(paths, path);
        }
    }

    private static void AddPath(List<string> paths, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        foreach (string existing in paths)
        {
            if (string.Equals(existing, path, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        paths.Add(path);
    }
}
