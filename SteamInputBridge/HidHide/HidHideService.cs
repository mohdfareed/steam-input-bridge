using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace SteamInputBridge.HidHide;

/// <summary>Applies HidHide profile scopes and process access rules.</summary>
internal sealed class HidHideService(
    IHidHideCommandRunner runner,
    ILogger<HidHideService>? logger = null,
    Func<string?>? getCurrentProcessPath = null) : IDisposable
{
    private readonly Lock _gate = new();
    private readonly Func<string?> _getCurrentProcessPath =
        getCurrentProcessPath ?? (static () => Environment.ProcessPath);
    private HidHideSnapshot? _snapshot;
    private HidHideScope? _scope;
    private bool _disposed;

    /// <summary>Registers the current executable with HidHide's allowed application list.</summary>
    public void AllowCurrentProcess()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_getCurrentProcessPath() is not { Length: > 0 } processPath)
            {
                return;
            }

            string applications = runner.Run(["--app-list"]);
            if (!HidHideCommandOutput.ContainsValue(applications, "--app-reg", processPath))
            {
                _ = runner.Run(["--app-reg", processPath]);
            }
        }
    }

    /// <summary>Applies a profile scope using HidHide normal mode.</summary>
    public void Apply(HidHideScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);

        lock (_gate)
        {
            ApplyCore(scope);
        }
    }

    /// <summary>Restores the previous HidHide state.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            ClearCore();
        }
    }

    /// <summary>Gets current HidHide state.</summary>
    public HidHideFirewallStatus GetStatus()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            string hiddenDevices = runner.Run(["--dev-list"]);
            string registeredApps = runner.Run(["--app-list"]);
            string cloakState = runner.Run(["--cloak-state"]);
            string inverseState = runner.Run(["--inv-state"]);

            return new HidHideFirewallStatus(
                _scope is not null,
                HidHideSnapshot.IsOn(cloakState),
                HidHideSnapshot.IsOn(inverseState),
                HidHideCommandOutput.ReadValues(hiddenDevices, "--dev-hide"),
                HidHideCommandOutput.ReadValues(registeredApps, "--app-reg"));
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            ClearCore();
            _disposed = true;
        }
    }

    private void ApplyCore(HidHideScope scope)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (scope.IsEmpty)
        {
            ClearCore();
            return;
        }

        if (_scope?.HasSameValues(scope) == true)
        {
            return;
        }

        ClearCore();
        string currentProcessPath = _getCurrentProcessPath() ??
            throw new InvalidOperationException("Current process path is required for HidHide scope access.");
        HidHideSnapshot snapshot = HidHideSnapshot.Capture(runner, scope, [currentProcessPath]);
        try
        {
            // Normal mode uses HidHide's application list as the allow list.
            // Server startup keeps this executable allowed; scope changes only
            // hide devices and temporarily remove other applications from that
            // global allow list.
            List<string> args = [];
            foreach (string app in snapshot.ExistingRegisteredApplications)
            {
                if (!snapshot.PermanentAllowedApplications.Contains(app))
                {
                    args.Add("--app-unreg");
                    args.Add(app);
                }
            }

            args.Add("--inv-off");
            args.Add("--cloak-on");
            foreach (string device in scope.DeviceInstancePaths)
            {
                args.Add("--dev-hide");
                args.Add(device);
            }

            _ = runner.Run(args);
            _snapshot = snapshot;
            _scope = scope;
            HidHideLog.Applied(logger, scope.DeviceInstancePaths.Count);
        }
        catch
        {
            snapshot.Restore(runner);
            throw;
        }
    }

    private void ClearCore()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_snapshot is null)
        {
            return;
        }

        _snapshot.Restore(runner);
        HidHideLog.Restored(logger);
        _snapshot = null;
        _scope = null;
    }
}

/// <summary>Current HidHide state.</summary>
internal sealed record HidHideFirewallStatus(
    bool ScopeActive,
    bool CloakEnabled,
    bool InverseEnabled,
    IReadOnlyList<string> HiddenDevices,
    IReadOnlyList<string> RegisteredApplications)
{
    /// <summary>Current number of hidden devices.</summary>
    public int HiddenDeviceCount => HiddenDevices.Count;

    /// <summary>Current number of registered applications.</summary>
    public int RegisteredApplicationCount => RegisteredApplications.Count;
}

internal sealed record HidHideSnapshot(
    string CloakState,
    string InverseState,
    IReadOnlyDictionary<string, bool> HiddenDevices,
    IReadOnlySet<string> PermanentAllowedApplications,
    IReadOnlyList<string> ExistingRegisteredApplications)
{
    internal static HidHideSnapshot Capture(
        IHidHideCommandRunner runner,
        HidHideScope scope,
        IReadOnlyList<string> permanentAllowedApplications)
    {
        string hiddenDevices = runner.Run(["--dev-list"]);
        string registeredApps = runner.Run(["--app-list"]);
        IReadOnlyList<string> existingRegisteredApplications =
            HidHideCommandOutput.ReadValues(registeredApps, "--app-reg");

        return new HidHideSnapshot(
            runner.Run(["--cloak-state"]),
            runner.Run(["--inv-state"]),
            scope.DeviceInstancePaths.ToDictionary(
                static device => device,
                device => HidHideCommandOutput.ContainsValue(hiddenDevices, "--dev-hide", device),
                StringComparer.OrdinalIgnoreCase),
            permanentAllowedApplications.ToHashSet(StringComparer.OrdinalIgnoreCase),
            existingRegisteredApplications);
    }

    internal void Restore(IHidHideCommandRunner runner)
    {
        List<string> args = [];
        foreach (string app in ExistingRegisteredApplications)
        {
            if (!PermanentAllowedApplications.Contains(app))
            {
                args.Add("--app-reg");
                args.Add(app);
            }
        }

        foreach ((string device, bool hidden) in HiddenDevices)
        {
            args.Add(hidden ? "--dev-hide" : "--dev-unhide");
            args.Add(device);
        }

        args.Add(IsOn(CloakState) ? "--cloak-on" : "--cloak-off");
        args.Add(IsOn(InverseState) ? "--inv-on" : "--inv-off");
        _ = runner.Run(args);
    }

    internal static bool IsOn(string value)
    {
        return value.Contains("yes", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("true", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("on", StringComparison.OrdinalIgnoreCase);
    }
}

internal static class HidHideCommandOutput
{
    internal static bool ContainsValue(string output, string command, string value)
    {
        return ReadValues(output, command).Contains(value, StringComparer.OrdinalIgnoreCase);
    }

    internal static IReadOnlyList<string> ReadValues(string output, string command)
    {
        List<string> values = [];
        foreach (string line in output.Split(
                ['\r', '\n'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string value = line.StartsWith(command, StringComparison.OrdinalIgnoreCase)
                ? line[command.Length..].Trim()
                : line.Trim();
            values.Add(value.Trim('"'));
        }

        return values;
    }
}

internal static partial class HidHideLog
{
    private static readonly Action<ILogger, int, Exception?> AppliedMessage =
        LoggerMessage.Define<int>(
            LogLevel.Information,
            new EventId(1, nameof(Applied)),
            "Applied HidHide scope: devices={DeviceCount}");

    private static readonly Action<ILogger, Exception?> RestoredMessage =
        LoggerMessage.Define(
            LogLevel.Information,
            new EventId(2, nameof(Restored)),
            "Restored HidHide state.");

    internal static void Applied(ILogger? logger, int deviceCount)
    {
        if (logger is not null)
        {
            AppliedMessage(logger, deviceCount, null);
        }
    }

    internal static void Restored(ILogger? logger)
    {
        if (logger is not null)
        {
            RestoredMessage(logger, null);
        }
    }
}
