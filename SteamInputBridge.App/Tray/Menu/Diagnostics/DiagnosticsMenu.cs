using SteamInputBridge.Hosting.Server.Orchestration;

namespace SteamInputBridge.App.Tray.Menu;

internal sealed partial class AppMenu
{
    private static TrayMenuItem CreateDiagnosticsMenu(ServerStatus? status)
    {
        return TrayMenuItem.Menu(
            "Diagnostics",
            [
                CreateInputsMenu(status),
                CreateOutputsMenu(status),
                CreateSteamInputMenu(status),
                CreateShortcutsMenu(status),
            ]);
    }

    private static TrayMenuItem CreateBoolStatus(string label, bool value)
    {
        return TrayMenuItem.Status(label, AppText.Enabled(value), isChecked: value);
    }

}
