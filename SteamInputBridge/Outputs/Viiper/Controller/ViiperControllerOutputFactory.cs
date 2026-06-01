using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SteamInputBridge.Outputs.Controller;
using SteamInputBridge.Settings;

namespace SteamInputBridge.Outputs.Viiper.Controller;

/// <summary>Creates VIIPER controller outputs.</summary>
public sealed class ViiperControllerOutputFactory(SettingsService settings, ILogger<ViiperControllerOutputFactory> logger)
{
    /// <summary>Connects one VIIPER Xbox 360 output.</summary>
    public async ValueTask<IControllerOutput> ConnectXbox360Async(
        string displayName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ViiperSettings viiper = settings.Current.Viiper;
        return await ViiperXbox360ControllerOutput.ConnectAsync(new()
        {
            Host = viiper.Host,
            Port = viiper.Port,
            Logger = logger,
        }, displayName, cancellationToken).ConfigureAwait(false);
    }
}
