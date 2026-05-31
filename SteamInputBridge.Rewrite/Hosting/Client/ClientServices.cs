using System;
using Microsoft.Extensions.DependencyInjection;
using SteamInputBridge.Forwarding;
using SteamInputBridge.Outputs.Viiper.Controller;

namespace SteamInputBridge.Hosting.Client;

/// <summary>Dependency injection registration for the bridge client.</summary>
public static class ClientServices
{
    /// <summary>Adds client runtime services.</summary>
    public static IServiceCollection AddBridgeClient(this IServiceCollection services, string profileId)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);

        _ = services.AddSingleton(new ClientRunOptions(profileId));
        _ = services.AddSingleton<ViiperControllerOutputFactory>();
        _ = services.AddSingleton<ClientControllerForwardingService>();
        _ = services.AddHostedService(static services => services.GetRequiredService<ClientControllerForwardingService>());
        _ = services.AddHostedService<BridgeClient>();
        return services;
    }
}
