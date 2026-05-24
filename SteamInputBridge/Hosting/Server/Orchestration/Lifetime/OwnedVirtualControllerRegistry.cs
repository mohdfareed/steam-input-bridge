using System;
using System.Collections.Generic;
using System.Threading;
using SteamInputBridge.Forwarding.Controller;
using SteamInputBridge.Inputs.Sdl;
using SteamInputBridge.Outputs.Viiper;

namespace SteamInputBridge.Hosting.Server.Orchestration.Lifetime;

/// <summary>Tracks exact SDL identities observed for virtual controllers created by this process.</summary>
internal sealed class OwnedVirtualControllerRegistry
{
    private static readonly TimeSpan PendingLifetime = TimeSpan.FromSeconds(30);
    private readonly Lock _gate = new();
    private readonly HashSet<SdlControllerId> _ids = [];
    private readonly HashSet<DeviceIdentity> _paths = [];
    private readonly List<PendingOutput> _pending = [];

    public Guid BeginTrackingOutput(
        ControllerOutput output,
        IReadOnlyList<SdlControllerInfo> before)
    {
        if (!CanMatchOutput(output))
        {
            return Guid.Empty;
        }

        Guid trackingId = Guid.NewGuid();
        lock (_gate)
        {
            _pending.Add(new PendingOutput(
                trackingId,
                output,
                CapturePathIdentities(before),
                DateTimeOffset.UtcNow + PendingLifetime));
        }

        return trackingId;
    }

    public void CancelTrackingOutput(Guid trackingId)
    {
        if (trackingId == Guid.Empty)
        {
            return;
        }

        lock (_gate)
        {
            _ = _pending.RemoveAll(pending => pending.TrackingId == trackingId);
        }
    }

    public void ObserveControllers(IReadOnlyList<SdlControllerInfo> controllers)
    {
        lock (_gate)
        {
            PruneExpiredPending();
            foreach (SdlControllerInfo controller in controllers)
            {
                _ = TryClaimPendingController(controller);
            }
        }
    }

    public bool IsOwned(SdlControllerInfo controller)
    {
        lock (_gate)
        {
            PruneExpiredPending();
            return _ids.Contains(controller.Id) ||
                (DeviceIdentity.FromDevicePath(controller.Path) is { } path &&
                    (_paths.Contains(path) || TryClaimPendingPath(controller.Id, controller.VendorId, controller.ProductId, path)));
        }
    }

    public bool IsOwned(ClientControllerInfo controller)
    {
        DeviceIdentity? path =
            DeviceIdentity.FromRouteId(controller.PhysicalDeviceId) ??
            DeviceIdentity.FromRouteId(controller.PhysicalControllerId);
        if (path is not { Kind: DeviceIdentityKind.DevicePath })
        {
            return false;
        }

        lock (_gate)
        {
            PruneExpiredPending();
            return _paths.Contains(path.Value) ||
                TryClaimPendingPath(null, controller.VendorId, controller.ProductId, path.Value);
        }
    }

    private bool TryClaimPendingController(SdlControllerInfo controller)
    {
        return DeviceIdentity.FromDevicePath(controller.Path) is { } path &&
            TryClaimPendingPath(controller.Id, controller.VendorId, controller.ProductId, path);
    }

    private bool TryClaimPendingPath(
        SdlControllerId? controllerId,
        ushort vendorId,
        ushort productId,
        DeviceIdentity path)
    {
        if (_paths.Contains(path))
        {
            return true;
        }

        foreach (PendingOutput pending in _pending.ToArray())
        {
            if (!MatchesOutput(pending.Output, vendorId, productId) ||
                pending.ExistingPaths.Contains(path))
            {
                continue;
            }

            // VIIPER does not return the Windows HID path it created. VID/PID
            // is used only inside this short pending window to discover the
            // exact new SDL path; all later filtering is exact-path based.
            if (controllerId is { } id)
            {
                _ = _ids.Add(id);
            }

            _ = _paths.Add(path);
            _ = _pending.Remove(pending);
            return true;
        }

        return false;
    }

    private static HashSet<DeviceIdentity> CapturePathIdentities(
        IReadOnlyList<SdlControllerInfo> controllers)
    {
        HashSet<DeviceIdentity> paths = [];
        foreach (SdlControllerInfo controller in controllers)
        {
            if (DeviceIdentity.FromDevicePath(controller.Path) is { } path)
            {
                _ = paths.Add(path);
            }
        }

        return paths;
    }

    private static bool CanMatchOutput(ControllerOutput output)
    {
        return output is ControllerOutput.Xbox360 or ControllerOutput.Ds4;
    }

    private static bool MatchesOutput(ControllerOutput output, ushort vendorId, ushort productId)
    {
        return output switch
        {
            ControllerOutput.None => false,
            ControllerOutput.Xbox360 => vendorId == ViiperXbox360Output.OwnedVendorId &&
                productId == ViiperXbox360Output.OwnedProductId,
            ControllerOutput.Ds4 => vendorId == ViiperDs4Output.OwnedVendorId &&
                productId == ViiperDs4Output.OwnedProductId,
            _ => false,
        };
    }

    private void PruneExpiredPending()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        _ = _pending.RemoveAll(pending => pending.ExpiresAt <= now);
    }

    private sealed record PendingOutput(
        Guid TrackingId,
        ControllerOutput Output,
        HashSet<DeviceIdentity> ExistingPaths,
        DateTimeOffset ExpiresAt);
}
