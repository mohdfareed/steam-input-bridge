using System;
using System.Threading;
using System.Threading.Tasks;
using Inputs;
using Outputs;

namespace Hosting;

/// <summary>Filters one mouse input report.</summary>
/// <param name="input">Mouse input.</param>
/// <returns><see langword="true" /> to forward the report.</returns>
internal delegate bool MouseInputFilter(in MouseInput input);

/// <summary>Forwards mouse input to mouse output.</summary>
internal static class MouseForwardingExtensions
{
    /// <summary>Forwards input reports to a mouse output.</summary>
    public static void RunTo(
        this IMouseInputSource input,
        IMouseOutput output,
        CancellationToken cancellationToken = default)
    {
        input.RunTo(output, filter: null, cancellationToken);
    }

    /// <summary>Forwards filtered input reports to a mouse output.</summary>
    public static void RunTo(
        this IMouseInputSource input,
        IMouseOutput output,
        MouseInputFilter? filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);

        input.Run(HandleInput, cancellationToken);

        void HandleInput(in MouseInput source)
        {
            if (!output.FilterInput(source.DeviceName))
            {
                return;
            }

            if (filter is not null && !filter(in source))
            {
                return;
            }

            if (!source.Report.IsEmpty)
            {
                SendSynchronously(output, source.Report, cancellationToken);
            }
        }
    }

    private static void SendSynchronously(
        IMouseOutput output,
        MouseReport report,
        CancellationToken cancellationToken)
    {
        ValueTask sendTask = output.SendAsync(report, cancellationToken);
        if (sendTask.IsCompleted)
        {
            sendTask.GetAwaiter().GetResult();
            return;
        }

        sendTask.AsTask().GetAwaiter().GetResult();
    }
}
