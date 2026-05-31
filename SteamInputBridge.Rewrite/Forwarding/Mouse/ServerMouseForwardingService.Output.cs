using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SteamInputBridge.Outputs.Mouse;
using SteamInputBridge.Profiles;

namespace SteamInputBridge.Forwarding.Mouse;

public sealed partial class ServerMouseForwardingService
{
    // MARK: Output Lifecycle
    // ========================================================================

    private async Task RefreshOutputAsync(CancellationToken cancellationToken)
    {
        MouseOutput outputKind = DesiredOutputKind();
        IMouseOutput? oldOutput;
        lock (_gate)
        {
            if (_outputKind == outputKind)
            {
                return;
            }

            oldOutput = _output;
            _output = null;
            _outputKind = MouseOutput.None;
        }

        Clear(oldOutput);
        await DisposeOutputAsync(oldOutput).ConfigureAwait(false);

        if (outputKind == MouseOutput.None)
        {
            return;
        }

        try
        {
            IMouseOutput output = await _outputFactory.ConnectAsync(outputKind, cancellationToken).ConfigureAwait(false);
            lock (_gate)
            {
                _output = output;
                _outputKind = outputKind;
            }

            await output.ClearAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or NotSupportedException or IOException or TimeoutException)
        {
            LogMouseOutputFailed(_logger, outputKind, exception.Message, null);
        }
    }

    private MouseOutput DesiredOutputKind()
    {
        foreach (ProfileStatus profile in profiles.Profiles)
        {
            if (profile.ClientProcessId.HasValue && profile.MouseOutput.HasValue)
            {
                return profile.MouseOutput.Value;
            }
        }

        return MouseOutput.None;
    }

    private static void Clear(IMouseOutput? output)
    {
        if (output is null)
        {
            return;
        }

        ValueTask clear = output.ClearAsync();
        if (!clear.IsCompletedSuccessfully)
        {
            _ = ObserveSendAsync(clear);
        }
    }

    private async ValueTask DisposeOutputAsync()
    {
        IMouseOutput? output;
        lock (_gate)
        {
            output = _output;
            _output = null;
            _outputKind = MouseOutput.None;
        }

        await DisposeOutputAsync(output).ConfigureAwait(false);
    }

    private static async ValueTask DisposeOutputAsync(IMouseOutput? output)
    {
        if (output is not null)
        {
            await output.DisposeAsync().ConfigureAwait(false);
        }
    }
}
