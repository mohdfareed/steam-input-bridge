using System;
using System.Collections.Generic;
using SteamInputBridge.Hosting.Server.Orchestration;

namespace SteamInputBridge.Hosting.Server.Pipes;

internal sealed partial class ClientControllerPipe
{
    public void RegisterControllers(IReadOnlyList<ClientControllerInfo> controllers)
    {
        ArgumentNullException.ThrowIfNull(controllers);

        lock (_controllers)
        {
            if (HasSameControllers(controllers))
            {
                return;
            }
        }

        broker.RemoveClientControllers(clientId);

        lock (_controllers)
        {
            _controllers.Clear();
            foreach (ClientControllerInfo controller in controllers)
            {
                _controllers[controller.ControllerIndex] = controller;
                _ = _inputFrameCounts.TryAdd(controller.ControllerIndex, 0);
            }
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
            string.Equals(current.PhysicalDeviceId, next.PhysicalDeviceId, StringComparison.Ordinal);
    }

    private long GetInputFrameCount(ushort controllerIndex)
    {
        return _inputFrameCounts.TryGetValue(controllerIndex, out long count)
            ? count
            : 0;
    }
}
