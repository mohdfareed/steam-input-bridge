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
            if (_getCurrentProcessPath() is { Length: > 0 } processPath)
            {
                AllowApplicationCore(processPath);
            }
        }
    }

    /// <summary>Registers an executable with HidHide's allowed application list.</summary>
    public void AllowApplication(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        lock (_gate)
        {
            AllowApplicationCore(path);
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
            ThrowIfDisposed();

            string hiddenDevices = runner.Run(["--dev-list"]);
            string registeredApps = runner.Run(["--app-list"]);
            string cloakState = runner.Run(["--cloak-state"]);
            string inverseState = runner.Run(["--inv-state"]);

            return new HidHideFirewallStatus(
                _scope is not null,
                HidHideSnapshot.IsOn(cloakState),
                HidHideSnapshot.IsOn(inverseState),
                HidHideSnapshot.ReadCommandValues(hiddenDevices, "--dev-hide"),
                HidHideSnapshot.ReadCommandValues(registeredApps, "--app-reg"));
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

    private void AllowApplicationCore(string path)
    {
        string applications = runner.Run(["--app-list"]);
        if (ContainsLineValue(applications, path))
        {
            return;
        }

        _ = runner.Run(["--app-reg", path]);
    }

    private void ApplyCore(HidHideScope scope)
    {
        ThrowIfDisposed();

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
            // For this experiment, only this executable sees scoped hidden devices.
            List<string> args = [];
            foreach (string app in snapshot.ExistingRegisteredApplications)
            {
                if (!string.Equals(app, currentProcessPath, StringComparison.OrdinalIgnoreCase))
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

            args.Add("--app-reg");
            args.Add(currentProcessPath);

            _ = runner.Run(args);
            _snapshot = snapshot;
            _scope = scope;
            HidHideLog.Applied(logger, scope.DeviceInstancePaths.Count, scope.ApplicationPaths.Count);
        }
        catch
        {
            snapshot.Restore(runner);
            throw;
        }
    }

    private void ClearCore()
    {
        ThrowIfDisposed();
        if (_snapshot is null)
        {
            return;
        }

        _snapshot.Restore(runner);
        HidHideLog.Restored(logger);
        _snapshot = null;
        _scope = null;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private static bool ContainsLineValue(string output, string value)
    {
        foreach (string line in output.Split(
            ['\r', '\n'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (line.Contains(value, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
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
    IReadOnlyDictionary<string, bool> ScopedApplications,
    IReadOnlyList<string> ExistingRegisteredApplications)
{
    internal static HidHideSnapshot Capture(
        IHidHideCommandRunner runner,
        HidHideScope scope,
        IReadOnlyList<string> scopedApplications)
    {
        string hiddenDevices = runner.Run(["--dev-list"]);
        string registeredApps = runner.Run(["--app-list"]);
        IReadOnlyList<string> registeredApplicationPaths = ReadCommandValues(registeredApps, "--app-reg");

        return new HidHideSnapshot(
            runner.Run(["--cloak-state"]),
            runner.Run(["--inv-state"]),
            scope.DeviceInstancePaths.ToDictionary(
                static device => device,
                device => ContainsLineValue(hiddenDevices, device),
                StringComparer.OrdinalIgnoreCase),
            scopedApplications.ToDictionary(
                static app => app,
                app => registeredApplicationPaths.Contains(app, StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase),
            registeredApplicationPaths);
    }

    internal void Restore(IHidHideCommandRunner runner)
    {
        List<string> args = [];
        foreach (string app in ExistingRegisteredApplications)
        {
            if (!ScopedApplications.ContainsKey(app))
            {
                args.Add("--app-reg");
                args.Add(app);
            }
        }

        foreach ((string app, bool registered) in ScopedApplications)
        {
            args.Add(registered ? "--app-reg" : "--app-unreg");
            args.Add(app);
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

    private static bool ContainsLineValue(string output, string value)
    {
        return output.Contains(value, StringComparison.OrdinalIgnoreCase);
    }

    internal static bool IsOn(string value)
    {
        return value.Contains("yes", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("true", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("on", StringComparison.OrdinalIgnoreCase);
    }

    internal static IReadOnlyList<string> ReadCommandValues(string output, string command)
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
    private static readonly Action<ILogger, int, int, Exception?> AppliedMessage =
        LoggerMessage.Define<int, int>(
            LogLevel.Information,
            new EventId(1, nameof(Applied)),
            "Applied HidHide scope: devices={DeviceCount} applications={ApplicationCount}");

    private static readonly Action<ILogger, Exception?> RestoredMessage =
        LoggerMessage.Define(
            LogLevel.Information,
            new EventId(2, nameof(Restored)),
            "Restored HidHide state.");

    internal static void Applied(ILogger? logger, int deviceCount, int applicationCount)
    {
        if (logger is not null)
        {
            AppliedMessage(logger, deviceCount, applicationCount, null);
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
