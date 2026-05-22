using SteamInputBridge.Inputs.Sdl;
using SteamInputBridge.Outputs.Viiper;

namespace SteamInputBridge.Hosting;

internal static class SdlControllerFilters
{
    public static bool IsForwardable(SdlControllerInfo controller)
    {
        return !ViiperDevices.IsController(controller.VendorId, controller.ProductId);
    }
}
