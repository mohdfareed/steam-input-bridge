using Microsoft.Extensions.DependencyInjection;
using SteamInputBridge.Hosting.Client.Run;

namespace SteamInputBridge.Hosting.Client.Connection;

/// <summary>Dependency injection registration for the app-facing client.</summary>
public static class ClientServices
{
    /// <summary>Adds the local server client.</summary>
    public static IServiceCollection AddApplicationClient(this IServiceCollection services)
    {
        _ = services.AddTransient<ClientService>();
        _ = services.AddTransient<GameClient>();
        return services;
    }
}
