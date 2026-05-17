using System;
using System.Collections.Generic;

namespace Inputs.Sdl;

/// <summary>Matches Steam controllers to physical controllers.</summary>
public static class SdlControllerMatcher
{
    private const ushort ValveVendorId = 0x28de;

    /// <summary>Finds the physical controller behind one Steam controller.</summary>
    public static SdlControllerInfo? FindPhysicalController(
        SdlControllerInfo steamController,
        IReadOnlyList<SdlControllerInfo> physicalControllers)
    {
        ArgumentNullException.ThrowIfNull(steamController);
        ArgumentNullException.ThrowIfNull(physicalControllers);

        if (steamController.Source != SdlControllerSource.Steam)
        {
            return null;
        }

        SdlControllerInfo? exactPath = string.IsNullOrWhiteSpace(steamController.Path)
            ? null
            : FindUnique(
                physicalControllers,
                controller => controller.Source == SdlControllerSource.Physical &&
                    string.Equals(controller.Path, steamController.Path, StringComparison.OrdinalIgnoreCase));
        if (exactPath is not null)
        {
            return exactPath;
        }

        SdlControllerInfo? exact = FindUnique(
            physicalControllers,
            controller => controller.Source == SdlControllerSource.Physical &&
                controller.VendorId == steamController.VendorId &&
                controller.ProductId == steamController.ProductId);

        // REVIEW: Valve Steam Controllers currently expose different Steam/physical product ids,
        // and multiple Valve controllers cannot be uniquely paired with only SDL's visible identity.
        // Therefore, only one Valve controller can be uniquely matched, and thus, supported at a time.
        return exact is not null || steamController.VendorId != ValveVendorId
            ? exact
            : FindUnique(
            physicalControllers,
            controller => controller.Source == SdlControllerSource.Physical &&
                controller.VendorId == ValveVendorId &&
                string.Equals(controller.Name, steamController.Name, StringComparison.OrdinalIgnoreCase));
    }

    private static SdlControllerInfo? FindUnique(
        IReadOnlyList<SdlControllerInfo> controllers,
        Func<SdlControllerInfo, bool> predicate)
    {
        SdlControllerInfo? match = null;
        int count = 0;
        foreach (SdlControllerInfo controller in controllers)
        {
            if (!predicate(controller))
            {
                continue;
            }

            match = controller;
            count++;
        }

        return count == 1 ? match : null;
    }
}
