using System.Collections.Generic;
using System.Linq;
using SteamInputBridge.HidHide;

namespace SteamInputBridge.Tests;

/// <summary>Tests HidHide profile scoping.</summary>
[TestClass]
public sealed class HidHideServiceTests
{
    private static readonly string[] ApplyThenClearCommands =
    [
        "--dev-list",
        "--app-list",
        "--cloak-state",
        "--inv-state",
        "--inv-off --cloak-on --dev-hide dev-1",
        "--dev-unhide dev-1 --cloak-off --inv-off",
    ];

    private static readonly string[] AppListCommand = ["--app-list"];
    private static readonly string[] StatusHiddenDevices = ["dev-1", "dev-2"];
    private static readonly string[] StatusRegisteredApplications = ["app-1"];

    /// <summary>Applies normal-mode scope and restores previous device/app state.</summary>
    [TestMethod]
    public void ApplyThenClearRestoresPreviousState()
    {
        FakeRunner runner = new(
            devList: "",
            appList: "SteamInputBridge.exe",
            cloakState: "off",
            inverseState: "off");
        using HidHideService firewall = CreateFirewall(runner);

        firewall.Apply(HidHideScope.Create(["dev-1"]));
        firewall.Clear();

        CollectionAssert.AreEqual(ApplyThenClearCommands, runner.Commands);
    }

    /// <summary>Clear restores pre-existing registrations instead of removing them.</summary>
    [TestMethod]
    public void ClearRestoresExistingHiddenDeviceAndRegisteredApp()
    {
        FakeRunner runner = new(
            devList: "dev-1",
            appList: "app-1\r\nSteamInputBridge.exe",
            cloakState: "on",
            inverseState: "on");
        using HidHideService firewall = CreateFirewall(runner);

        firewall.Apply(HidHideScope.Create(["dev-1"]));
        firewall.Clear();

        Assert.AreEqual(
            "--app-reg app-1 --dev-hide dev-1 --cloak-on --inv-on",
            runner.Commands[^1]);
    }

    /// <summary>Normal scopes temporarily remove unrelated app-list entries.</summary>
    [TestMethod]
    public void ApplyTemporarilyRemovesOtherApplications()
    {
        FakeRunner runner = new(
            devList: "",
            appList: "tool.exe\r\napp-1\r\nSteamInputBridge.exe",
            cloakState: "off",
            inverseState: "off");
        using HidHideService firewall = CreateFirewall(runner);

        firewall.Apply(HidHideScope.Create(["dev-1"]));
        Assert.AreEqual(
            "--app-unreg tool.exe --app-unreg app-1 --inv-off --cloak-on --dev-hide dev-1",
            runner.Commands[^1]);

        firewall.Clear();
        Assert.AreEqual(
            "--app-reg tool.exe --app-reg app-1 --dev-unhide dev-1 --cloak-off --inv-off",
            runner.Commands[^1]);
    }

    /// <summary>Normal-mode scopes do not require receiver executable paths.</summary>
    [TestMethod]
    public void ApplyRequiresOnlyDevicePaths()
    {
        FakeRunner runner = new(
            devList: "",
            appList: "SteamInputBridge.exe",
            cloakState: "off",
            inverseState: "off");
        using HidHideService firewall = CreateFirewall(runner);

        firewall.Apply(HidHideScope.Create(["dev-1"]));

        Assert.AreEqual(
            "--inv-off --cloak-on --dev-hide dev-1",
            runner.Commands[^1]);
    }

    /// <summary>Reads current global HidHide status.</summary>
    [TestMethod]
    public void GetStatusReportsCurrentState()
    {
        FakeRunner runner = new(
            devList: "dev-1\r\ndev-2",
            appList: "app-1",
            cloakState: "--cloak-on",
            inverseState: "--inv-off");
        using HidHideService firewall = CreateFirewall(runner);

        HidHideFirewallStatus status = firewall.GetStatus();

        Assert.IsFalse(status.ScopeActive);
        Assert.IsTrue(status.CloakEnabled);
        Assert.IsFalse(status.InverseEnabled);
        Assert.AreEqual(2, status.HiddenDeviceCount);
        Assert.AreEqual(1, status.RegisteredApplicationCount);
        CollectionAssert.AreEqual(StatusHiddenDevices, status.HiddenDevices.ToArray());
        CollectionAssert.AreEqual(StatusRegisteredApplications, status.RegisteredApplications.ToArray());
    }

    /// <summary>Status includes whether this process applied a scope.</summary>
    [TestMethod]
    public void GetStatusReportsActiveScope()
    {
        FakeRunner runner = new(
            devList: "dev-1",
            appList: "SteamInputBridge.exe",
            cloakState: "--cloak-on",
            inverseState: "--inv-off");
        using HidHideService firewall = CreateFirewall(runner);

        firewall.Apply(HidHideScope.Create(["dev-1"]));
        HidHideFirewallStatus status = firewall.GetStatus();

        Assert.IsTrue(status.ScopeActive);
        Assert.IsFalse(status.InverseEnabled);
    }

    /// <summary>Repeated equivalent scopes do not restore and reapply HidHide.</summary>
    [TestMethod]
    public void EquivalentApplyDoesNotReapply()
    {
        FakeRunner runner = new(
            devList: "",
            appList: "SteamInputBridge.exe",
            cloakState: "off",
            inverseState: "off");
        using HidHideService firewall = CreateFirewall(runner);

        firewall.Apply(HidHideScope.Create(["dev-1"]));
        firewall.Apply(HidHideScope.Create(["dev-1"]));

        Assert.AreEqual("--inv-off --cloak-on --dev-hide dev-1", runner.Commands[^1]);
        Assert.HasCount(5, runner.Commands);
    }

    /// <summary>Registers this application only when it is not already allowed.</summary>
    [TestMethod]
    public void CurrentApplicationAccessRegistersMissingApp()
    {
        FakeRunner runner = new(
            devList: "",
            appList: "existing-app",
            cloakState: "off",
            inverseState: "off");
        using HidHideService access = CreateFirewall(runner);

        access.AllowCurrentProcess();

        Assert.AreEqual("--app-reg SteamInputBridge.exe", runner.Commands[^1]);
    }

    /// <summary>Leaves existing application registration alone.</summary>
    [TestMethod]
    public void CurrentApplicationAccessSkipsRegisteredApp()
    {
        FakeRunner runner = new(
            devList: "",
            appList: "--app-reg \"SteamInputBridge.exe\"",
            cloakState: "off",
            inverseState: "off");
        using HidHideService access = CreateFirewall(runner);

        access.AllowCurrentProcess();

        CollectionAssert.AreEqual(AppListCommand, runner.Commands);
    }

    /// <summary>Application access checks exact HidHide entries, not substrings.</summary>
    [TestMethod]
    public void CurrentApplicationAccessDoesNotMatchSubstring()
    {
        FakeRunner runner = new(
            devList: "",
            appList: "OtherSteamInputBridge.exe",
            cloakState: "off",
            inverseState: "off");
        using HidHideService access = CreateFirewall(runner);

        access.AllowCurrentProcess();

        Assert.AreEqual("--app-reg SteamInputBridge.exe", runner.Commands[^1]);
    }

    /// <summary>Matches a transport path to HidHide's device instance path.</summary>
    [TestMethod]
    public void FindDeviceInstancePathMatchesSymbolicLink()
    {
        const string Devices = """
            [
              {
                "friendlyName": "DualSense",
                "devices": [
                  {
                    "present": true,
                    "gamingDevice": true,
                    "vendor": "054C",
                    "product": "0CE6",
                    "usage": "0005",
                    "symbolicLink": "\\\\?\\HID#VID_054C&PID_0CE6#ABC",
                    "deviceInstancePath": "HID\\VID_054C&PID_0CE6\\ABC"
                  }
                ]
              }
            ]
            """;
        HidHideDeviceCatalog catalog = new(new DeviceListRunner(Devices));

        string? path = catalog.FindDeviceInstancePath(@"\\?\hid\vid_054c&pid_0ce6\abc");

        Assert.AreEqual(@"HID\VID_054C&PID_0CE6\ABC", path);
    }

    private sealed class FakeRunner(
        string devList,
        string appList,
        string cloakState,
        string inverseState) : IHidHideCommandRunner
    {
        public List<string> Commands { get; } = [];

        public string Run(IReadOnlyList<string> args)
        {
            string command = string.Join(" ", args);
            Commands.Add(command);
            return command switch
            {
                "--dev-list" => devList,
                "--app-list" => appList,
                "--cloak-state" => cloakState,
                "--inv-state" => inverseState,
                _ => "",
            };
        }
    }

    private static HidHideService CreateFirewall(FakeRunner runner)
    {
        return new HidHideService(runner, getCurrentProcessPath: static () => "SteamInputBridge.exe");
    }

    private sealed class DeviceListRunner(string devices) : IHidHideCommandRunner
    {
        public string Run(IReadOnlyList<string> args)
        {
            return string.Join(" ", args) == "--dev-all"
                ? devices
                : "";
        }
    }
}
