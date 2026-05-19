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
using ForwardingMouseOutput = VirtualMouse.Forwarding.MouseOutput;

namespace VirtualMouse.Hosting;

/// <summary>Dependency injection registration for the local server.</summary>
public static class ServerServices
{
    /// <summary>Adds the local server.</summary>
    public static IServiceCollection AddApplicationServer(this IServiceCollection services)
    {
        _ = services.AddSingleton<ActiveClientRegistry>();
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
        _ = services.AddSingleton<ControllerBroker>();
        _ = services.AddSingleton<MouseBroker>();
        _ = services.AddSingleton(static services =>
        {
            ViiperOutputFactory viiper = services.GetRequiredService<ViiperOutputFactory>();
            return new ServerService(
                services.GetRequiredService<IOptions<HostingSettings>>(),
                services.GetRequiredService<ILogger<ServerService>>(),
                services.GetService<SettingsFile>(),
                services.GetService<ProfilesService>(),
                services.GetRequiredService<ActiveClientRegistry>(),
                activeClients: null,
                services.GetRequiredService<ControllerBroker>(),
                services.GetRequiredService<MouseBroker>(),
                viiper.ReclaimDevicesAsync);
        });
        return services;
    }

    private sealed class ServerMouseOutputFactory(
        ViiperOutputFactory viiper,
        TeensyOutputFactory teensy) : IMouseOutputFactory
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
