using System.Threading.Tasks;

namespace SteamInputBridge.Hosting.Server.Orchestration;

internal sealed partial class ServerSessions
{
    internal Task<ServerStatus> GetStatusAsync()
    {
        return Task.FromResult(new ServerStatus(_clients.Count)
        {
            Runtime = runtime.GetStatus(),
            Forwarding = forwarding.GetStatus(),
            MouseForwarding = mouseForwarding.GetStatus(),
            Inputs = getInputStatus?.Invoke() ??
                new ServerInputStatus(new MouseInputPumpStatus(false, false, null)),
            SteamInput = getSteamInputStatus?.Invoke() ?? new ServerSteamInputStatus(false, null, null, null),
            ControllerPipes = controllerPipes.GetStatus(),
            Overlay = getOverlayStatus?.Invoke() ?? OverlayStatus.Hidden,
            Shortcuts = getShortcutStatus?.Invoke() ?? ShortcutRuntimeStatus.Empty,
        });
    }
}
