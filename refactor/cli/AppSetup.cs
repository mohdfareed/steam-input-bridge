using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using VirtualMouse.Client;
using VirtualMouse.Protocol;
using VirtualMouse.Server;

namespace Refactor.Cli;

internal static class AppSetup
{
    public static IHost Create()
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();

        // One appsettings file configures both commands so server and client agree on the pipe.
        _ = builder.Configuration.AddJsonFile("appsettings.json", optional: true);
        IConfigurationSection options = builder.Configuration.GetSection(ConnectionOptions.SectionName);
        _ = builder.Services.Configure<ConnectionOptions>(options);

        _ = builder.Services.AddApplicationClient();
        _ = builder.Services.AddApplicationServer();
        _ = builder.Logging.AddConsole();

        return builder.Build();
    }
}
