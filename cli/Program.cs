using System;
using System.CommandLine;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

internal static class Program
{
    // MARK: Entry
    // ========================================================================

    private static async Task<int> Main(string[] args)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
        _ = builder.Logging.ClearProviders();
        _ = builder.Logging.AddSimpleConsole(options =>
        {
            options.SingleLine = true;
            options.TimestampFormat = "HH:mm:ss ";
        });
        _ = builder.Services.Configure<CliViiperSettings>(
            builder.Configuration.GetSection(CliViiperSettings.SectionName));

        using IHost host = builder.Build();
        IServiceProvider services = host.Services;
        RootCommand root = new("Local input forwarding CLI.");

        if (OperatingSystem.IsWindows())
        {
            root.Subcommands.Add(HostCommands.CreateHostCommand(services));
            root.Subcommands.Add(ClientCommands.CreateClientCommand());
            root.Subcommands.Add(SteamCommands.CreateSteamCommand());
            root.Subcommands.Add(TestCommands.CreateTestCommand(services));
        }

        return await root.Parse(args).InvokeAsync().ConfigureAwait(false);
    }
}
