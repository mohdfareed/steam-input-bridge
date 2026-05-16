using System;
using System.Threading;
using System.Threading.Tasks;
using Inputs;
using Outputs;

namespace Hosting;

/// <summary>Route owned by a local forwarding host.</summary>
public interface IForwardingRoute : IAsyncDisposable
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

/// <summary>Mouse input to mouse output route.</summary>
public sealed class MouseForwardingRoute(
    IMouseInputSource input,
    IMouseOutput output,
    string routeId = MouseForwardingRoute.DefaultRouteId) : IForwardingRoute
{
    /// <summary>Default mouse route id.</summary>
    public const string DefaultRouteId = "mouse";

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

/// <summary>Gamepad input to Xbox 360 output route.</summary>
public sealed class Xbox360ForwardingRoute(
    IGamepadInputSource input,
    IXbox360Output output,
    string routeId = Xbox360ForwardingRoute.DefaultRouteId) : IForwardingRoute
{
    /// <summary>Default Xbox 360 route id.</summary>
    public const string DefaultRouteId = "xpad";

    private readonly IGamepadInputSource _input = input ?? throw new ArgumentNullException(nameof(input));
    private readonly IXbox360Output _output = output ?? throw new ArgumentNullException(nameof(output));

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

        bool ShouldForward(in GamepadInput input)
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
