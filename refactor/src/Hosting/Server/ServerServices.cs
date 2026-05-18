using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VirtualMouse.Forwarding;
using VirtualMouse.Outputs.Teensy;
using VirtualMouse.Outputs.Viiper;
using VirtualMouse.Runtime;
using VirtualMouse.Settings;
using VirtualMouse.Settings.Profiles;

namespace VirtualMouse.Hosting;

/// <summary>Dependency injection registration for the local server.</summary>
public static class ServerServices
{
    /// <summary>Adds the local server.</summary>
    public static IServiceCollection AddApplicationServer(this IServiceCollection services)
    {
        _ = services.AddSingleton<ActiveClientRegistry>();
        _ = services.AddSingleton(CreateViiperOutputFactory);
        _ = services.AddSingleton<IControllerOutputFactory>(
            static services => services.GetRequiredService<ViiperOutputFactory>());
        _ = services.AddSingleton<TeensyOutputFactory>();
        _ = services.AddSingleton<ServerMouseOutputFactory>();
        _ = services.AddSingleton<IMouseOutputFactory>(
            static services => services.GetRequiredService<ServerMouseOutputFactory>());
        _ = services.AddSingleton<ControllerBroker>();
        _ = services.AddSingleton<MouseBroker>();
        _ = services.AddSingleton(CreateServer);
        return services;
    }

    private static ViiperOutputFactory CreateViiperOutputFactory(IServiceProvider services)
    {
        GeneralSettings settings = services.GetRequiredService<IOptions<GeneralSettings>>().Value;
        ILoggerFactory loggerFactory = services.GetRequiredService<ILoggerFactory>();
        return new ViiperOutputFactory(new ViiperOptions
        {
            Host = settings.ViiperHost,
            Port = settings.ViiperPort,
            Logger = loggerFactory.CreateLogger<ViiperOutputFactory>(),
        });
    }

    private static VirtualMouseServer CreateServer(IServiceProvider services)
    {
        return new VirtualMouseServer(
            services.GetRequiredService<IOptions<HostingSettings>>(),
            services.GetRequiredService<ILogger<VirtualMouseServer>>(),
            services.GetService<SettingsFile>(),
            services.GetService<ProfilesService>(),
            services.GetRequiredService<ActiveClientRegistry>(),
            activeClients: null,
            services.GetRequiredService<ControllerBroker>(),
            services.GetRequiredService<MouseBroker>());
    }
}
