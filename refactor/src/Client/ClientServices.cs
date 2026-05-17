using Microsoft.Extensions.DependencyInjection;

namespace VirtualMouse.Client;

public static class ClientServices
{
    public static IServiceCollection AddApplicationClient(this IServiceCollection services)
    {
        _ = services.AddTransient<VirtualMouseClient>();
        return services;
    }
}
