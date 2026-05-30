using Vanara.PInvoke;
using static Vanara.PInvoke.User32;

namespace SteamInputBridge.Hosting.Server.Orchestration.Active;

internal static class ServerForegroundProcess
{
    public static int GetId()
    {
        HWND window = GetForegroundWindow();
        if (window.IsNull)
        {
            return 0;
        }

        _ = GetWindowThreadProcessId(window, out uint processId);
        return processId <= int.MaxValue ? (int)processId : 0;
    }
}
