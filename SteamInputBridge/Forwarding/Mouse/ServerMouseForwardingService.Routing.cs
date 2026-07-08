using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using SteamInputBridge.Inputs.Mouse;
using SteamInputBridge.Outputs.Mouse;
using SteamInputBridge.Profiles;

namespace SteamInputBridge.Forwarding.Mouse;

public sealed partial class ServerMouseForwardingService
{
    // MARK: Routing
    // ========================================================================

    private void RunInput(CancellationToken cancellationToken)
    {
        try
        {
            IMouseInputSource input = _input ?? throw new InvalidOperationException("Mouse input source is not connected.");
            input.Run(Send, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception or COMException)
        {
            LogMouseInputFailed(_logger, exception.Message, null);
        }
    }

    private void Send(in MouseInput input)
    {
        IMouseOutput? output;
        MouseOutput outputKind;
        bool pointerEnabled;
        lock (_gate)
        {
            output = _output;
            outputKind = _outputKind;
            pointerEnabled = _pointerEnabled;
        }

        ProfileStatus? activeProfile = _profiles.ActiveProfile;
        if (output is null ||
            !pointerEnabled ||
            input.Report.IsEmpty ||
            activeProfile?.Definition.MouseOutput != outputKind)
        {
            return;
        }

        ValueTask send = output.SendAsync(in input);
        if (!send.IsCompletedSuccessfully)
        {
            _ = ObserveSendAsync(send);
        }
    }

    private static async Task ObserveSendAsync(ValueTask send)
    {
        try
        {
            await send.ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is InvalidOperationException or ObjectDisposedException)
        {
        }
    }
}
