using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace SteamInputBridge.HidHide;

/// <summary>Persists the HidHide scope this app owns across server restarts.</summary>
internal sealed class HidHideOwnedScopeStore(string path)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    public string Path => path;

    public static HidHideOwnedScopeStore CreateDefault()
    {
        string directory = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SteamInputBridge");
        return new HidHideOwnedScopeStore(System.IO.Path.Combine(directory, "hidhide-owned-scope.json"));
    }

    public HidHideSnapshot? Load()
    {
        if (!File.Exists(path))
        {
            return null;
        }

        string json = File.ReadAllText(path);
        PersistedScope? scope = JsonSerializer.Deserialize<PersistedScope>(json, JsonOptions);
        if (scope is null || scope.DeviceInstancePaths.Count == 0)
        {
            return null;
        }

        Dictionary<string, bool> hiddenDevices = new(StringComparer.OrdinalIgnoreCase);
        foreach (string device in scope.DeviceInstancePaths)
        {
            if (!string.IsNullOrWhiteSpace(device))
            {
                hiddenDevices[device] = false;
            }
        }

        return hiddenDevices.Count == 0
            ? null
            : new HidHideSnapshot(scope.CloakState, scope.InverseState, hiddenDevices);
    }

    public void Save(HidHideSnapshot snapshot)
    {
        _ = Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path) ?? ".");
        PersistedScope scope = new(
            snapshot.CloakState,
            snapshot.InverseState,
            [.. snapshot.HiddenDevices.Keys]);
        string tempPath = path + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(scope, JsonOptions));
        File.Move(tempPath, path, overwrite: true);
    }

    public void Clear()
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private sealed record PersistedScope(
        string CloakState,
        string InverseState,
        IReadOnlyList<string> DeviceInstancePaths);
}
