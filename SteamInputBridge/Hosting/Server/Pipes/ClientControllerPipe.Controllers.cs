using System;
using System.Collections.Generic;
using SteamInputBridge.Forwarding.Controller;
using SteamInputBridge.Forwarding.Controller.Routing;
using SteamInputBridge.Hosting.Server.Orchestration;

namespace SteamInputBridge.Hosting.Server.Pipes;

internal sealed partial class ClientControllerPipe
{
    public IReadOnlyList<ClientControllerInfo> RegisterControllers(IReadOnlyList<ClientControllerInfo> controllers)
    {
        ArgumentNullException.ThrowIfNull(controllers);

        lock (_controllers)
        {
            _requestedControllers.Clear();
            foreach (ClientControllerInfo controller in controllers)
            {
                _requestedControllers[controller.ControllerIndex] = controller;
            }

            List<ClientControllerInfo> resolved = ResolveControllers(_requestedControllers.Values);
            ApplyResolvedControllers(resolved);
            return resolved;
        }
    }

    public void RefreshResolvedControllers()
    {
        lock (_controllers)
        {
            if (_requestedControllers.Count == 0)
            {
                return;
            }

            ApplyResolvedControllers(ResolveControllers(_requestedControllers.Values));
        }
    }

    public ControllerPipeStatus GetStatus(Guid clientId)
    {
        List<ClientControllerStatus> controllers = [];
        lock (_controllers)
        {
            foreach (ClientControllerInfo controller in _controllers.Values)
            {
                controllers.Add(new ClientControllerStatus(
                    controller.ControllerIndex,
                    controller.PhysicalControllerId,
                    controller.Label,
                    controller.Features,
                    controller.PhysicalDeviceId)
                {
                    InputFrameCount = GetInputFrameCount(controller.ControllerIndex),
                });
            }
        }

        return new ControllerPipeStatus(
            clientId,
            PipeName,
            _pipe?.IsConnected == true,
            controllers);
    }

    private bool TryGetController(ushort controllerIndex, out ClientControllerInfo? controller)
    {
        lock (_controllers)
        {
            return _controllers.TryGetValue(controllerIndex, out controller);
        }
    }

    private bool HasSameControllers(IReadOnlyList<ClientControllerInfo> controllers)
    {
        if (_controllers.Count != controllers.Count)
        {
            return false;
        }

        foreach (ClientControllerInfo controller in controllers)
        {
            if (!_controllers.TryGetValue(controller.ControllerIndex, out ClientControllerInfo? current) ||
                !IsSameController(current, controller))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsSameController(
        ClientControllerInfo current,
        ClientControllerInfo next)
    {
        return current.ControllerIndex == next.ControllerIndex &&
            current.PhysicalControllerId == next.PhysicalControllerId &&
            string.Equals(current.Label, next.Label, StringComparison.Ordinal) &&
            current.Features == next.Features &&
            string.Equals(current.PhysicalDeviceId, next.PhysicalDeviceId, StringComparison.Ordinal) &&
            current.VendorId == next.VendorId &&
            current.ProductId == next.ProductId;
    }

    private static bool ContainsSameController(
        IReadOnlyList<ClientControllerInfo> controllers,
        ClientControllerInfo current)
    {
        foreach (ClientControllerInfo controller in controllers)
        {
            if (IsSameController(current, controller))
            {
                return true;
            }
        }

        return false;
    }

    private long GetInputFrameCount(ushort controllerIndex)
    {
        return _inputFrameCounts.TryGetValue(controllerIndex, out long count)
            ? count
            : 0;
    }

    private void ApplyResolvedControllers(IReadOnlyList<ClientControllerInfo> controllers)
    {
        if (HasSameControllers(controllers))
        {
            return;
        }

        UpdateInputFrameCounts(controllers);
        broker.SetClientControllers(clientId, CreateBrokerRegistrations(controllers));

        _controllers.Clear();
        foreach (ClientControllerInfo controller in controllers)
        {
            _controllers[controller.ControllerIndex] = controller;
        }
    }

    private void UpdateInputFrameCounts(IReadOnlyList<ClientControllerInfo> controllers)
    {
        foreach (ClientControllerInfo current in _controllers.Values)
        {
            if (ContainsSameController(controllers, current))
            {
                continue;
            }

            _ = _inputFrameCounts.Remove(current.ControllerIndex);
        }

        foreach (ClientControllerInfo controller in controllers)
        {
            _ = _inputFrameCounts.TryAdd(controller.ControllerIndex, 0);
        }
    }

    private static List<ControllerClientRegistration> CreateBrokerRegistrations(
        IReadOnlyList<ClientControllerInfo> controllers)
    {
        List<ControllerClientRegistration> registrations = [];
        foreach (ClientControllerInfo controller in controllers)
        {
            registrations.Add(new ControllerClientRegistration(
                controller.ControllerIndex,
                new ControllerId(controller.PhysicalControllerId, controller.Label),
                controller.Features));
        }

        return registrations;
    }

    private List<ClientControllerInfo> ResolveControllers(IEnumerable<ClientControllerInfo> controllers)
    {
        List<ClientControllerInfo> resolved = [];
        foreach (ClientControllerInfo controller in controllers)
        {
            resolved.Add(_physicalControllers?.ResolveClientController(controller) ?? controller);
        }

        return resolved;
    }
}
