using System;
using System.Collections.Generic;
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
using SteamInputBridge.Settings;
using SteamInputBridge.Settings.Profiles;
using SteamInputBridge.Shortcuts;
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
                    GetHidHideApplicationAccessPaths(settings.CliPath));
        });

        // Keyboard shortcuts
        _ = services.AddSingleton<IKeyboardShortcutListener, GlobalKeyboardShortcutListener>();

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
        // The server needs access to hidden devices, and the integration also
        // launches HidHideCLI.exe for every read/write. Keeping the CLI allowed
        // prevents the app from locking its own management path out after cloak
        // mode is enabled.
        List<string> paths = [];
        if (Environment.ProcessPath is { Length: > 0 } processPath)
        {
            paths.Add(processPath);
        }

        if (!string.IsNullOrWhiteSpace(cliPath))
        {
            paths.Add(cliPath);
        }

        return paths;
    }
}
