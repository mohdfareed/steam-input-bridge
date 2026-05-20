using System.Collections.Generic;
using System.Linq;
using SteamInputBridge.HidHide;

namespace SteamInputBridge.Tests;

/// <summary>Tests HidHide profile scoping.</summary>
[TestClass]
public sealed class HidHideProfileFirewallTests
{
    private static readonly string[] ApplyThenClearCommands =
    [
        "--dev-list",
        "--app-list",
        "--cloak-state",
        "--inv-state",
        "--inv-on --cloak-on --dev-hide dev-1 --app-reg app-1",
        "--app-unreg app-1 --dev-unhide dev-1 --cloak-off --inv-off",
    ];

    private static readonly string[] AppListCommand = ["--app-list"];
    private static readonly string[] StatusHiddenDevices = ["dev-1", "dev-2"];
    private static readonly string[] StatusRegisteredApplications = ["app-1"];

    /// <summary>Applies inverse-mode scope and restores previous device/app state.</summary>
    [TestMethod]
    public void ApplyThenClearRestoresPreviousState()
    {
        FakeRunner runner = new(
            devList: "",
            appList: "",
            cloakState: "off",
            inverseState: "off");
        using HidHideProfileFirewall firewall = new(runner);

        firewall.Apply(HidHideScope.Create(["dev-1"], ["app-1"]));
        firewall.Clear();

        CollectionAssert.AreEqual(ApplyThenClearCommands, runner.Commands);
    }

    /// <summary>Clear restores pre-existing registrations instead of removing them.</summary>
    [TestMethod]
    public void ClearRestoresExistingHiddenDeviceAndRegisteredApp()
    {
        FakeRunner runner = new(
            devList: "dev-1",
            appList: "app-1",
            cloakState: "on",
            inverseState: "on");
        using HidHideProfileFirewall firewall = new(runner);

        firewall.Apply(HidHideScope.Create(["dev-1"], ["app-1"]));
        firewall.Clear();

        Assert.AreEqual(
            "--app-reg app-1 --dev-hide dev-1 --cloak-on --inv-on",
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
        using HidHideProfileFirewall firewall = new(runner);

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
            appList: "app-1",
            cloakState: "--cloak-on",
            inverseState: "--inv-on");
        using HidHideProfileFirewall firewall = new(runner);

        firewall.Apply(HidHideScope.Create(["dev-1"], ["app-1"]));
        HidHideFirewallStatus status = firewall.GetStatus();

        Assert.IsTrue(status.ScopeActive);
        Assert.IsTrue(status.InverseEnabled);
    }

    /// <summary>Registers this application only when it is not already allowed.</summary>
    [TestMethod]
    public void ApplicationAccessRegistersMissingApp()
    {
        FakeRunner runner = new(
            devList: "",
            appList: "existing-app",
            cloakState: "off",
            inverseState: "off");
        HidHideApplicationAccess access = new(runner);

        access.AllowApplication("SteamInputBridge.exe");

        Assert.AreEqual("--app-reg SteamInputBridge.exe", runner.Commands[^1]);
    }

    /// <summary>Leaves existing application registration alone.</summary>
    [TestMethod]
    public void ApplicationAccessSkipsRegisteredApp()
    {
        FakeRunner runner = new(
            devList: "",
            appList: "--app-reg \"SteamInputBridge.exe\"",
            cloakState: "off",
            inverseState: "off");
        HidHideApplicationAccess access = new(runner);

        access.AllowApplication("SteamInputBridge.exe");

        CollectionAssert.AreEqual(AppListCommand, runner.Commands);
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
