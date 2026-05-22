using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SteamInputBridge.Steam.GameCatalog;

namespace SteamInputBridge.Steam;

/// <summary>Reads local Steam state and controls Steam Input through Steam URLs.</summary>
/// <param name="openUrl">Steam URL opener. Defaults to the OS URL handler.</param>
public sealed class SteamInputClient(Func<Uri, CancellationToken, ValueTask>? openUrl = null)
{
    /// <summary>Steam's desktop controller configuration app id.</summary>
    public const uint DesktopConfigAppId = 413080;

    private readonly Func<Uri, CancellationToken, ValueTask> _openUrl = openUrl ?? OpenSteamUrlAsync;

    // MARK: Publics
    // ========================================================================

    /// <summary>Lists Steam and non-Steam games known locally.</summary>
    /// <param name="steamPath">Steam install path. When omitted, the local install is discovered.</param>
    /// <param name="steamUserId">Steam user id for non-Steam shortcuts. When omitted, the active user is used.</param>
    public static IReadOnlyList<SteamGame> ListGames(string? steamPath = null, uint? steamUserId = null)
    {
        string resolvedPath = ResolveSteamPath(steamPath);
        uint? resolvedUserId = steamUserId ?? SteamLocator.FindActiveUserId();
        return new SteamGameCatalog(resolvedPath).ListGames(resolvedUserId);
    }

    /// <summary>Reads the Steam app id exposed to a Steam-launched process.</summary>
    public static uint? ResolveAppId()
    {
        return TryParseAppId(Environment.GetEnvironmentVariable("SteamAppId")) ??
            TryParseAppId(Environment.GetEnvironmentVariable("SteamGameId"));
    }

    /// <summary>Forces Steam Input to use an app config, or clears forcing when null.</summary>
    /// <param name="appId">Steam app id, non-Steam shortcut app id, or null to clear forcing.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public ValueTask ForceConfigAsync(uint? appId, CancellationToken cancellationToken = default)
    {
        uint forcedAppId = appId switch
        {
            0 => throw new ArgumentOutOfRangeException(nameof(appId), "Steam app id must be non-zero."),
            null => 0,
            uint value => value,
        };

        return OpenAsync(CreateForceInputUri(forcedAppId), cancellationToken);
    }

    /// <summary>Opens Steam's controller configurator for an app.</summary>
    /// <param name="appId">Steam app id or non-Steam shortcut app id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public ValueTask OpenControllerConfigAsync(uint appId, CancellationToken cancellationToken = default)
    {
        ValidateAppId(appId);
        return OpenAsync(CreateOpenConfigUri(appId), cancellationToken);
    }

    // MARK: Privates
    // ========================================================================

    private ValueTask OpenAsync(Uri url, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return _openUrl(url, cancellationToken);
    }

    private static Uri CreateForceInputUri(uint appId)
    {
        return new Uri($"steam://forceinputappid/{appId.ToString(CultureInfo.InvariantCulture)}");
    }

    private static Uri CreateOpenConfigUri(uint appId)
    {
        return new Uri($"steam://controllerconfig/{appId.ToString(CultureInfo.InvariantCulture)}");
    }

    private static void ValidateAppId(uint appId)
    {
        if (appId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(appId), "Steam app id must be non-zero.");
        }
    }

    private static uint? TryParseAppId(string? value)
    {
        return uint.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out uint appId)
            ? appId
            : null;
    }

    private static string ResolveSteamPath(string? steamPath)
    {
        string? resolvedPath = string.IsNullOrWhiteSpace(steamPath)
            ? SteamLocator.FindSteamPath()
            : Path.GetFullPath(steamPath);

        return string.IsNullOrWhiteSpace(resolvedPath) || !Directory.Exists(resolvedPath)
            ? throw new InvalidOperationException("Could not find Steam. Pass a Steam path.")
            : resolvedPath;
    }

    private static ValueTask OpenSteamUrlAsync(Uri url, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(url);
        cancellationToken.ThrowIfCancellationRequested();

        if (!string.Equals(url.Scheme, "steam", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Only steam:// URLs are supported.", nameof(url));
        }

        ProcessStartInfo startInfo = OperatingSystem.IsLinux()
            ? new ProcessStartInfo("xdg-open", url.AbsoluteUri)
            : new ProcessStartInfo
            {
                FileName = url.AbsoluteUri,
                UseShellExecute = true,
            };

        using Process process = Process.Start(startInfo) ??
            throw new InvalidOperationException("Could not open the Steam URL.");
        return ValueTask.CompletedTask;
    }
}
