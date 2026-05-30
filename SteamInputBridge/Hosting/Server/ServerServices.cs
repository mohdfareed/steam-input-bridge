using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SteamInputBridge.Forwarding.Controller;
using SteamInputBridge.Forwarding.Controller.Routing;
using SteamInputBridge.Forwarding.Mouse;
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
        _ = services.AddSingleton<IControllerOutputFactory>(
            static services => services.GetRequiredService<ViiperOutputFactory>());
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
            return new ServerService(
                services.GetRequiredService<ILogger<ServerService>>(),
                services.GetService<SettingsFile>(),
                services.GetService<ProfilesService>(),
                services.GetRequiredService<ActiveClientRegistry>(),
                activeClients: null,
                services.GetRequiredService<ControllerBroker>(),
                services.GetRequiredService<MouseBroker>(),
                services.GetRequiredService<ServerShortcutService>(),
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
}
