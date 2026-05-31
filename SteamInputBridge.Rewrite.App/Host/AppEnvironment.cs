namespace SteamInputBridge.App.Host;

/// <summary>Application paths and version metadata.</summary>
/// <param name="BaseDirectory">Application base directory.</param>
/// <param name="ExecutablePath">Current executable path.</param>
/// <param name="SettingsPath">Settings file path.</param>
/// <param name="LogPath">Current log file path.</param>
/// <param name="Version">Application version.</param>
internal sealed record AppEnvironment(
    string BaseDirectory,
    string ExecutablePath,
    string SettingsPath,
    string LogPath,
    string Version);
