using System;
using Microsoft.Extensions.DependencyInjection;

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
        _ = services.AddHostedService<BridgeClient>();
        return services;
    }
}
