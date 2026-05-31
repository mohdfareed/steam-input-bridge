using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SteamInputBridge.Outputs.Mouse;
using SteamInputBridge.Settings;

namespace SteamInputBridge.Outputs.Viiper.Mouse;

/// <summary>Creates VIIPER mouse outputs.</summary>
public sealed class ViiperMouseOutputFactory(SettingsService settings, ILogger<ViiperMouseOutputFactory> logger)
{
    /// <summary>Connects a VIIPER mouse output.</summary>
    public async ValueTask<IMouseOutput> ConnectAsync(MouseOutput output, CancellationToken cancellationToken = default)
    {
        if (output != MouseOutput.Viiper)
        {
            throw new NotSupportedException($"VIIPER does not support {output} mouse output.");
        }

        ViiperSettings viiper = settings.Current.Viiper;
        return await ViiperMouseOutput.ConnectAsync(new()
        {
            Host = viiper.Host,
            Port = viiper.Port,
            Logger = logger,
        }, cancellationToken).ConfigureAwait(false);
    }
}
