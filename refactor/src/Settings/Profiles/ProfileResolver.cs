using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace VirtualMouse.Settings.Profiles;

/// <summary>Runtime-ready game profile.</summary>
public sealed record ResolvedGameProfile(
    string Id,
    string Title,
    string? Executable,
    string Arguments,
    uint? SteamAppId,
    string? WorkingDirectory,
    IReadOnlyList<string> ReceiverProcesses,
    ControllerOutput ControllerOutput,
    MouseOutput MouseOutput);

/// <summary>Resolves raw profile settings into runtime-ready profiles.</summary>
public static class ProfileResolver
{
    /// <summary>Resolves one game profile.</summary>
    public static ResolvedGameProfile Resolve(string profileId, GameProfile profile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        ArgumentNullException.ThrowIfNull(profile);

        string? executable = NormalizePath(profile.Executable);

        string? workingDirectory = NormalizePath(profile.WorkingDirectory);
        if (string.IsNullOrWhiteSpace(workingDirectory) &&
            !string.IsNullOrWhiteSpace(executable))
        {
            workingDirectory = Path.GetDirectoryName(executable) ?? AppContext.BaseDirectory;
        }

        string[] receiverProcesses = ResolveReceiverProcesses(profile, executable);

        return receiverProcesses.Length == 0
            ? throw new InvalidOperationException(
                $"Profile \"{profileId}\" needs receiverProcesses when executable is missing.")
            : new ResolvedGameProfile(
                profileId,
                ResolveTitle(profileId, profile),
                executable,
                profile.Arguments ?? string.Empty,
                profile.SteamAppId,
                workingDirectory,
                receiverProcesses,
                profile.ControllerOutput ?? ControllerOutput.None,
                profile.MouseOutput ?? MouseOutput.None);
    }

    private static string ResolveTitle(string profileId, GameProfile profile)
    {
        return string.IsNullOrWhiteSpace(profile.Title)
            ? ToTitle(profileId)
            : profile.Title.Trim();
    }

    private static string[] ResolveReceiverProcesses(GameProfile profile, string? executable)
    {
        List<string> receivers = [];
        foreach (string receiver in profile.ReceiverProcesses)
        {
            if (!string.IsNullOrWhiteSpace(receiver))
            {
                receivers.Add(Path.GetFileName(receiver.Trim()));
            }
        }

        if (receivers.Count == 0 && !string.IsNullOrWhiteSpace(executable))
        {
            string executableName = Path.GetFileName(executable);
            if (!string.IsNullOrWhiteSpace(executableName))
            {
                receivers.Add(executableName);
            }
        }

        return [.. receivers];
    }

    private static string? NormalizePath(string? path)
    {
        return string.IsNullOrWhiteSpace(path)
            ? null
            : Environment.ExpandEnvironmentVariables(path.Trim());
    }

    private static string ToTitle(string profileId)
    {
        string spaced = profileId.Replace('-', ' ').Replace('_', ' ');
        return CultureInfo.CurrentCulture.TextInfo.ToTitleCase(spaced);
    }
}
