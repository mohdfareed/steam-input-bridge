using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Forms;
using SteamInputBridge.Hosting;
using SteamInputBridge.Profiles;
using SteamInputBridge.Settings;

namespace SteamInputBridge.App.Tray.Menu;

internal sealed class ProfilesMenuSection
{
    private readonly Dictionary<string, ProfileMenuBinding> _profiles = new(StringComparer.OrdinalIgnoreCase);

    // MARK: Publics
    // ========================================================================

    public ToolStripMenuItem Build(
        IReadOnlyList<ProfileStatus> profiles,
        string? lastActiveProfileId,
        IReadOnlyList<BridgeShortcutStatus> activeShortcuts,
        Func<uint, Task> openSteamConfig,
        Action<Exception> onError,
        Action<Guid> stopClient)
    {
        _profiles.Clear();
        ToolStripMenuItem menu = TrayMenuItems.Menu("Profiles");
        if (profiles.Count == 0)
        {
            _ = menu.DropDownItems.Add(TrayMenuItems.Disabled("None"));
            return menu;
        }

        foreach (ProfileStatus profile in profiles)
        {
            _ = menu.DropDownItems.Add(CreateProfileMenu(
                profile,
                IsLastActive(profile, lastActiveProfileId),
                activeShortcuts,
                openSteamConfig,
                onError,
                stopClient));
        }

        return menu;
    }

    public void Update(
        IReadOnlyList<ProfileStatus> profiles,
        string? lastActiveProfileId,
        IReadOnlyList<BridgeShortcutStatus> activeShortcuts)
    {
        foreach (ProfileStatus profile in profiles)
        {
            if (_profiles.TryGetValue(profile.Id, out ProfileMenuBinding? items))
            {
                items.Update(profile, IsLastActive(profile, lastActiveProfileId), activeShortcuts);
            }
        }
    }

    public static bool ShapeChanged(IReadOnlyList<ProfileStatus> previous, IReadOnlyList<ProfileStatus> current)
    {
        if (previous.Count != current.Count)
        {
            return true;
        }

        for (int i = 0; i < previous.Count; i++)
        {
            if (previous[i].Id != current[i].Id ||
                previous[i].Definition != current[i].Definition ||
                previous[i].EffectiveSteamAppId != current[i].EffectiveSteamAppId ||
                previous[i].ClientProcessId != current[i].ClientProcessId)
            {
                return true;
            }
        }

        return false;
    }

    // MARK: Build
    // ========================================================================

    private ToolStripMenuItem CreateProfileMenu(
        ProfileStatus profile,
        bool lastActive,
        IReadOnlyList<BridgeShortcutStatus> activeShortcuts,
        Func<uint, Task> openSteamConfig,
        Action<Exception> onError,
        Action<Guid> stopClient)
    {
        ToolStripMenuItem menu = TrayMenuItems.Menu(profile.Definition.Title);
        SetProfileCheckMark(menu, profile, lastActive);

        ToolStripMenuItem appId = TrayMenuItems.Item("Steam app ID", TrayMenuItems.Number(profile.EffectiveSteamAppId));
        ToolStripMenuItem openConfig = CreateOpenConfigItem(profile, openSteamConfig, onError);
        ToolStripMenuItem client = TrayMenuItems.Item("Client", TrayMenuItems.Number(profile.ClientProcessId));
        TrayMenuItems.SetCheckMark(client, HasClient(profile));
        ToolStripMenuItem gameProcesses = TrayMenuItems.Item(
            "Game processes",
            TrayMenuItems.Number(profile.GameProcessIds.Count));
        TrayMenuItems.SetCheckMark(gameProcesses, HasGameProcesses(profile));
        ShortcutsMenuSection shortcuts = new();

        _ = menu.DropDownItems.Add(TrayMenuItems.Item("ID", profile.Id));
        _ = menu.DropDownItems.Add(appId);
        _ = menu.DropDownItems.Add(TrayMenuItems.Item("Mouse output", TrayMenuItems.Output(profile.Definition.MouseOutput)));
        _ = menu.DropDownItems.Add(TrayMenuItems.Item("Controller output", TrayMenuItems.Output(profile.Definition.ControllerOutput)));
        _ = menu.DropDownItems.Add(client);
        _ = menu.DropDownItems.Add(gameProcesses);
        _ = menu.DropDownItems.Add(shortcuts.Build(ShortcutStatuses(profile, activeShortcuts)));
        _ = menu.DropDownItems.Add(new ToolStripSeparator());
        _ = menu.DropDownItems.Add(openConfig);
        if (profile.ClientConnectionId is Guid connectionId)
        {
            _ = menu.DropDownItems.Add(TrayMenuItems.ActionItem("Stop client", () => stopClient(connectionId)));
        }

        _profiles[profile.Id] = new(menu, appId, client, gameProcesses, shortcuts);
        return menu;
    }

    private static List<BridgeShortcutStatus> ShortcutStatuses(
        ProfileStatus profile,
        IReadOnlyList<BridgeShortcutStatus> activeShortcuts)
    {
        List<BridgeShortcutStatus> statuses = [];
        foreach (ShortcutEntry shortcut in profile.Definition.Shortcuts)
        {
            if (!shortcut.Target.HasValue)
            {
                continue;
            }

            string target = shortcut.Target.Value.ToString();
            string action = shortcut.Action.ToString();
            statuses.Add(new(
                shortcut.Keys,
                target,
                action,
                profile.Active && IsPressed(activeShortcuts, shortcut.Keys, target, action)));
        }

        return statuses;
    }

    private static bool IsPressed(
        IReadOnlyList<BridgeShortcutStatus> shortcuts,
        string keys,
        string target,
        string action)
    {
        foreach (BridgeShortcutStatus shortcut in shortcuts)
        {
            if (!shortcut.Pressed ||
                !string.Equals(shortcut.Keys, keys, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(shortcut.Action, action, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (string.Equals(shortcut.Target, target, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static ToolStripMenuItem CreateOpenConfigItem(
        ProfileStatus profile,
        Func<uint, Task> openSteamConfig,
        Action<Exception> onError)
    {
        return profile.EffectiveSteamAppId is uint appId
            ? TrayMenuItems.ActionItem("Open Steam Input config", () => openSteamConfig(appId), onError)
            : TrayMenuItems.Disabled("Open Steam Input config");
    }

    private static bool IsLastActive(ProfileStatus profile, string? lastActiveProfileId)
    {
        return string.Equals(profile.Id, lastActiveProfileId, StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasClient(ProfileStatus profile)
    {
        return profile.ClientProcessId.HasValue;
    }

    private static bool HasGameProcesses(ProfileStatus profile)
    {
        return profile.GameProcessIds.Count > 0;
    }

    private static void SetProfileCheckMark(ToolStripMenuItem menu, ProfileStatus profile, bool lastActive)
    {
        if (lastActive)
        {
            TrayMenuItems.SetGreenCheckMark(menu, HasClient(profile));
        }
        else
        {
            TrayMenuItems.SetCheckMark(menu, HasClient(profile));
        }
    }

    private sealed record ProfileMenuBinding(
        ToolStripMenuItem Menu,
        ToolStripMenuItem AppId,
        ToolStripMenuItem Client,
        ToolStripMenuItem GameProcesses,
        ShortcutsMenuSection Shortcuts)
    {
        public void Update(
            ProfileStatus profile,
            bool lastActive,
            IReadOnlyList<BridgeShortcutStatus> activeShortcuts)
        {
            SetProfileCheckMark(Menu, profile, lastActive);
            TrayMenuItems.SetValue(AppId, TrayMenuItems.Number(profile.EffectiveSteamAppId));
            TrayMenuItems.SetValue(Client, TrayMenuItems.Number(profile.ClientProcessId));
            TrayMenuItems.SetCheckMark(Client, HasClient(profile));
            TrayMenuItems.SetValue(GameProcesses, TrayMenuItems.Number(profile.GameProcessIds.Count));
            TrayMenuItems.SetCheckMark(GameProcesses, HasGameProcesses(profile));
            Shortcuts.Update(ShortcutStatuses(profile, activeShortcuts));
        }
    }
}
