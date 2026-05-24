using System;
using System.Collections.Generic;
using SteamInputBridge.Forwarding.Controller;
using SteamInputBridge.Inputs.Sdl;

namespace SteamInputBridge.Hosting.Server.Orchestration.Lifetime;

/// <summary>Wraps controller output creation and records exact SDL echoes created by the output.</summary>
internal sealed class TrackingControllerOutputFactory(
    IControllerOutputFactory inner,
    OwnedVirtualControllerRegistry ownedVirtualControllers) : IControllerOutputFactory
{
    public IControllerOutput Connect(ControllerId controllerId, ControllerOutput output)
    {
        IReadOnlyList<SdlControllerInfo> before = SnapshotControllers();
        Guid trackingId = ownedVirtualControllers.BeginTrackingOutput(output, before);
        try
        {
            IControllerOutput created = inner.Connect(controllerId, output);
            ownedVirtualControllers.ObserveControllers(SnapshotControllers());
            return created;
        }
        catch
        {
            ownedVirtualControllers.CancelTrackingOutput(trackingId);
            throw;
        }
    }

    private static IReadOnlyList<SdlControllerInfo> SnapshotControllers()
    {
        try
        {
            return SdlControllerCatalog.GetControllers();
        }
        catch (InvalidOperationException)
        {
            return [];
        }
    }
}
