using System;
using System.Threading;
using System.Threading.Tasks;
using Inputs;
using Outputs;

namespace Hosting;

internal static class ForwardingRouteIds
{
    public const string Mouse = "mouse";
}

internal interface IForwardingRoute : IAsyncDisposable
{
    /// <summary>Gets the route id.</summary>
    string RouteId { get; }

    /// <summary>Gets whether route input and output are connected.</summary>
    bool IsConnected { get; }

    /// <summary>Runs forwarding until cancelled.</summary>
    /// <param name="shouldForward">Returns whether this route should forward now.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    void Run(Func<bool> shouldForward, CancellationToken cancellationToken = default);
}

internal sealed class MouseForwardingRoute(
    IMouseInputSource input,
    IMouseOutput output,
    string routeId = ForwardingRouteIds.Mouse) : IForwardingRoute
{
    private readonly IMouseInputSource _input = input ?? throw new ArgumentNullException(nameof(input));
    private readonly IMouseOutput _output = output ?? throw new ArgumentNullException(nameof(output));

    /// <inheritdoc />
    public string RouteId { get; } = string.IsNullOrWhiteSpace(routeId)
        ? throw new ArgumentException("Route id is required.", nameof(routeId))
        : routeId;

    /// <inheritdoc />
    public bool IsConnected => _input.IsConnected && _output.IsConnected;

    /// <inheritdoc />
    public void Run(Func<bool> shouldForward, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(shouldForward);
        _input.RunTo(_output, ShouldForward, cancellationToken);

        bool ShouldForward(in MouseInput input)
        {
            _ = input;
            return shouldForward();
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _input.DisposeAsync().ConfigureAwait(false);
        await _output.DisposeAsync().ConfigureAwait(false);
    }
}

internal sealed class ForwardingHostState
{
    private int _emulationEnabled = 1;
    private int _physicalMotionEnabled = 1;

    public bool EmulationEnabled => Volatile.Read(ref _emulationEnabled) != 0;

    public bool PhysicalMotionEnabled => Volatile.Read(ref _physicalMotionEnabled) != 0;

    public void SetEmulationEnabled(bool enabled)
    {
        _ = Interlocked.Exchange(ref _emulationEnabled, enabled ? 1 : 0);
    }

    public bool ToggleEmulationEnabled()
    {
        return Toggle(ref _emulationEnabled);
    }

    public void SetPhysicalMotionEnabled(bool enabled)
    {
        _ = Interlocked.Exchange(ref _physicalMotionEnabled, enabled ? 1 : 0);
    }

    public bool TogglePhysicalMotionEnabled()
    {
        return Toggle(ref _physicalMotionEnabled);
    }

    private static bool Toggle(ref int state)
    {
        while (true)
        {
            int current = Volatile.Read(ref state);
            int next = current == 0 ? 1 : 0;
            if (Interlocked.CompareExchange(ref state, next, current) == current)
            {
                return next != 0;
            }
        }
    }
}
