using Microsoft.Extensions.DependencyInjection;

namespace VirtualMouse.Server;

public static class ServerServices
{
    public static IServiceCollection AddApplicationServer(this IServiceCollection services)
    {
        _ = services.AddSingleton<VirtualMouseServer>();
        return services;
    }
}
