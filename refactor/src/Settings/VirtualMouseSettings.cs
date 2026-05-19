using System.Collections.Generic;
using VirtualMouse.Settings.Profiles;

namespace VirtualMouse.Settings;

// MARK: Settings Models
// ============================================================================

/// <summary>Resolved settings file path.</summary>
public sealed record SettingsFile(string Path);

/// <summary>Application-owned settings root.</summary>
public sealed class VirtualMouseSettings
{
    /// <summary>Root section for app-owned settings.</summary>
    public const string SectionName = "VirtualMouse";

    /// <summary>Local hosting settings.</summary>
    public HostingSettings Hosting { get; set; } = new();

    /// <summary>Application logging settings.</summary>
    public LoggingSettings Logging { get; set; } = new();

    /// <summary>VIIPER output settings.</summary>
    public ViiperSettings Viiper { get; set; } = new();

    /// <summary>Configured game profiles by profile id.</summary>
    public Dictionary<string, GameProfile> Games { get; } = [];
}

/// <summary>VIIPER output settings.</summary>
public sealed class ViiperSettings
{
    /// <summary>Configuration section name for VIIPER output settings.</summary>
    public const string SectionName = VirtualMouseSettings.SectionName + ":Viiper";

    /// <summary>VIIPER server host.</summary>
    public string Host { get; set; } = "localhost";

    /// <summary>VIIPER server port.</summary>
    public int Port { get; set; } = 3242;
}

/// <summary>Local hosting settings.</summary>
public sealed class HostingSettings
{
    /// <summary>Configuration section name for local hosting settings.</summary>
    public const string SectionName = VirtualMouseSettings.SectionName + ":Hosting";

    /// <summary>Named pipe used by the server and client.</summary>
    public string PipeName { get; set; } = "VirtualMouse.Refactor";

    /// <summary>Delay between reconnect attempts.</summary>
    public int ReconnectDelayMilliseconds { get; set; } = 1000;

    /// <summary>Delay between keepalive acknowledgements.</summary>
    public int KeepAliveMilliseconds { get; set; } = 1000;

    /// <summary>Delay between foreground-window checks.</summary>
    public int ForegroundPollMilliseconds { get; set; } = 100;
}

/// <summary>Application logging settings.</summary>
public sealed class LoggingSettings
{
    /// <summary>Configuration section name for logging settings.</summary>
    public const string SectionName = VirtualMouseSettings.SectionName + ":Logging";

    /// <summary>Optional log file path.</summary>
    public string? LogFile { get; set; }
}
