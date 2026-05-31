using System;
using System.Collections.Generic;
using System.Windows.Forms;
using SteamInputBridge.Profiles;

namespace SteamInputBridge.App.Tray.Menu;

internal sealed class ProfilesMenuSection
{
    private readonly Dictionary<string, ProfileMenuBinding> _profiles = new(StringComparer.OrdinalIgnoreCase);

    // MARK: Publics
    // ========================================================================

    public ToolStripMenuItem Build(
        IReadOnlyList<ProfileStatus> profiles,
        string? lastActiveProfileId,
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
            _ = menu.DropDownItems.Add(CreateProfileMenu(profile, IsLastActive(profile, lastActiveProfileId), stopClient));
        }

        return menu;
    }

    public void Update(IReadOnlyList<ProfileStatus> profiles, string? lastActiveProfileId)
    {
        foreach (ProfileStatus profile in profiles)
        {
            if (_profiles.TryGetValue(profile.Id, out ProfileMenuBinding? items))
            {
                items.Update(profile, IsLastActive(profile, lastActiveProfileId));
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
                previous[i].Title != current[i].Title ||
                previous[i].MouseOutput != current[i].MouseOutput ||
                previous[i].ControllerOutput != current[i].ControllerOutput)
            {
                return true;
            }
        }

        return false;
    }

    // MARK: Build
    // ========================================================================

    private ToolStripMenuItem CreateProfileMenu(ProfileStatus profile, bool lastActive, Action<Guid> stopClient)
    {
        ToolStripMenuItem menu = TrayMenuItems.Menu(profile.Title);
        SetProfileCheckMark(menu, profile, lastActive);

        ToolStripMenuItem appId = TrayMenuItems.Item("Steam app ID", TrayMenuItems.Number(profile.EffectiveSteamAppId));
        ToolStripMenuItem active = TrayMenuItems.Item("Last active", TrayMenuItems.YesNo(lastActive));
        TrayMenuItems.SetCheckMark(active, lastActive);
        ToolStripMenuItem client = TrayMenuItems.Item("Client", TrayMenuItems.Number(profile.ClientProcessId));
        TrayMenuItems.SetCheckMark(client, HasClient(profile));
        ToolStripMenuItem gameProcesses = TrayMenuItems.Item(
            "Game processes",
            TrayMenuItems.Number(profile.GameProcessIds.Count));
        TrayMenuItems.SetCheckMark(gameProcesses, HasGameProcesses(profile));

        _ = menu.DropDownItems.Add(TrayMenuItems.Item("ID", profile.Id));
        _ = menu.DropDownItems.Add(appId);
        _ = menu.DropDownItems.Add(TrayMenuItems.Item("Mouse output", TrayMenuItems.Output(profile.MouseOutput)));
        _ = menu.DropDownItems.Add(TrayMenuItems.Item("Controller output", TrayMenuItems.Output(profile.ControllerOutput)));
        _ = menu.DropDownItems.Add(active);
        _ = menu.DropDownItems.Add(client);
        _ = menu.DropDownItems.Add(gameProcesses);
        if (profile.ClientConnectionId is Guid connectionId)
        {
            _ = menu.DropDownItems.Add(new ToolStripSeparator());
            _ = menu.DropDownItems.Add(TrayMenuItems.ActionItem("Stop client", () => stopClient(connectionId)));
        }

        _profiles[profile.Id] = new(menu, appId, active, client, gameProcesses);
        return menu;
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
        ToolStripMenuItem Active,
        ToolStripMenuItem Client,
        ToolStripMenuItem GameProcesses)
    {
        public void Update(ProfileStatus profile, bool lastActive)
        {
            SetProfileCheckMark(Menu, profile, lastActive);
            TrayMenuItems.SetValue(AppId, TrayMenuItems.Number(profile.EffectiveSteamAppId));
            TrayMenuItems.SetValue(Active, TrayMenuItems.YesNo(lastActive));
            TrayMenuItems.SetCheckMark(Active, lastActive);
            TrayMenuItems.SetValue(Client, TrayMenuItems.Number(profile.ClientProcessId));
            TrayMenuItems.SetCheckMark(Client, HasClient(profile));
            TrayMenuItems.SetValue(GameProcesses, TrayMenuItems.Number(profile.GameProcessIds.Count));
            TrayMenuItems.SetCheckMark(GameProcesses, HasGameProcesses(profile));
        }
    }
}
