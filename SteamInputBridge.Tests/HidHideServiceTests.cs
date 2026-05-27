using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SteamInputBridge.Forwarding.Controller;
using SteamInputBridge.Forwarding.Controller.Routing;
using SteamInputBridge.HidHide;
using SteamInputBridge.Hosting;
using SteamInputBridge.Hosting.Server.Orchestration;
using SteamInputBridge.Hosting.Server.Orchestration.Active;
using SteamInputBridge.Hosting.Server.Orchestration.Lifetime;
using SteamInputBridge.Hosting.Server.Pipes;
using SteamInputBridge.Inputs.Sdl;
using SteamInputBridge.Runtime;
using SteamInputBridge.Settings;
using SteamInputBridge.Settings.Profiles;
using ForwardingControllerOutput = SteamInputBridge.Forwarding.Controller.ControllerOutput;

namespace SteamInputBridge.Tests;

/// <summary>Tests HidHide profile scoping.</summary>
[TestClass]
public sealed class HidHideServiceTests
{
    private static readonly string[] ApplyThenClearCommands =
    [
        "--cloak-state",
        "--inv-state",
        "--inv-off --cloak-on --dev-hide dev-1",
        "--dev-unhide dev-1 --cloak-off --inv-off",
    ];

    private static readonly string[] AppListCommand = ["--app-list"];
    private static readonly string[] StatusHiddenDevices = ["dev-1", "dev-2"];
    private static readonly string[] StatusRegisteredApplications = ["app-1"];

    /// <summary>Applies normal-mode scope and restores previous device/mode state.</summary>
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

    /// <summary>Clear unhides current-scope devices even if they were already hidden.</summary>
    [TestMethod]
    public void ClearUnhidesCurrentScopeDevice()
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
            "--dev-unhide dev-1 --cloak-on --inv-on",
            runner.Commands[^1]);
    }

    /// <summary>Normal scopes do not mutate unrelated app-list entries.</summary>
    [TestMethod]
    public void ApplyDoesNotMutateApplicationList()
    {
        FakeRunner runner = new(
            devList: "",
            appList: "tool.exe\r\napp-1\r\nSteamInputBridge.exe",
            cloakState: "off",
            inverseState: "off");
        using HidHideService firewall = CreateFirewall(runner);

        firewall.Apply(HidHideScope.Create(["dev-1"]));
        Assert.AreEqual(
            "--inv-off --cloak-on --dev-hide dev-1",
            runner.Commands[^1]);

        firewall.Clear();
        Assert.AreEqual(
            "--dev-unhide dev-1 --cloak-off --inv-off",
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
        Assert.HasCount(3, runner.Commands);
    }

    /// <summary>Applied scopes are persisted so a later server can clear stale hidden devices.</summary>
    [TestMethod]
    public void ApplyPersistsOwnedScopeAndClearRemovesIt()
    {
        string directory = CreateTempDirectory();
        string path = Path.Combine(directory, "scope.json");
        try
        {
            FakeRunner runner = new(
                devList: "",
                appList: "SteamInputBridge.exe",
                cloakState: "off",
                inverseState: "off");
            using HidHideService firewall = CreateFirewall(
                runner,
                ownedScopeStore: new HidHideOwnedScopeStore(path));

            firewall.Apply(HidHideScope.Create(["dev-1"]));
            Assert.IsTrue(File.Exists(path));

            firewall.Clear();
            Assert.IsFalse(File.Exists(path));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>Startup cleanup clears devices hidden by a previous server lifetime.</summary>
    [TestMethod]
    public void ClearPreviousOwnedScopeRestoresPersistedScope()
    {
        string directory = CreateTempDirectory();
        string path = Path.Combine(directory, "scope.json");
        try
        {
            HidHideOwnedScopeStore store = new(path);
            store.Save(new HidHideSnapshot(
                "off",
                "off",
                new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
                {
                    ["dev-1"] = false,
                }));
            FakeRunner runner = new(
                devList: "dev-1",
                appList: "SteamInputBridge.exe",
                cloakState: "on",
                inverseState: "off");
            using HidHideService firewall = CreateFirewall(runner, ownedScopeStore: store);

            firewall.ClearPreviousOwnedScope();

            Assert.AreEqual("--dev-unhide dev-1 --cloak-off --inv-off", runner.Commands[^1]);
            Assert.IsFalse(File.Exists(path));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>Registers this application only when it is not already allowed.</summary>
    [TestMethod]
    public void RequiredApplicationAccessRegistersMissingApps()
    {
        FakeRunner runner = new(
            devList: "",
            appList: "existing-app",
            cloakState: "off",
            inverseState: "off");
        using HidHideService access = CreateFirewall(
            runner,
            getApplicationAccessPaths: static () => ["SteamInputBridge.exe", "HidHideCLI.exe"]);

        access.AllowRequiredApplications();

        Assert.AreEqual("--app-reg SteamInputBridge.exe", runner.Commands[^2]);
        Assert.AreEqual("--app-reg HidHideCLI.exe", runner.Commands[^1]);
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

        access.AllowRequiredApplications();

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

        access.AllowRequiredApplications();

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

    /// <summary>Foreign HidHide-hidden devices are not accepted as input routes.</summary>
    [TestMethod]
    public void InputFilterRejectsForeignHiddenDevice()
    {
        FakeRunner runner = new(
            devList: @"HID\VID_054C&PID_0CE6\ABC",
            appList: "SteamInputBridge.exe",
            cloakState: "on",
            inverseState: "off",
            devAll: DeviceListJson());
        using HidHideService firewall = CreateFirewall(runner);
        ServerControllerInputFilter filter = new(new HidHideDeviceCatalog(runner), firewall);

        Assert.IsFalse(filter.Allows(Controller(@"\\?\HID#VID_054C&PID_0CE6#ABC")));
    }

    /// <summary>Foreign-hidden controller interfaces hide the whole physical controller container.</summary>
    [TestMethod]
    public void InputFilterRejectsForeignHiddenDeviceContainer()
    {
        FakeRunner runner = new(
            devList: @"HID\VID_054C&PID_0DF2&MI_03\ABC",
            appList: "SteamInputBridge.exe",
            cloakState: "on",
            inverseState: "off",
            devAll: DeviceListWithSharedContainerJson());
        using HidHideService firewall = CreateFirewall(runner);
        ServerControllerInputFilter filter = new(new HidHideDeviceCatalog(runner), firewall);

        Assert.IsFalse(filter.Allows(Controller(@"\\?\HID#VID_054C&PID_0DF2&MI_00#ABC")));
    }

    /// <summary>Devices hidden by the current app scope remain accepted as owned input routes.</summary>
    [TestMethod]
    public void InputFilterAllowsOwnedHiddenDevice()
    {
        FakeRunner runner = new(
            devList: @"HID\VID_054C&PID_0CE6\ABC",
            appList: "SteamInputBridge.exe",
            cloakState: "on",
            inverseState: "off",
            devAll: DeviceListJson());
        using HidHideService firewall = CreateFirewall(runner);
        firewall.Apply(HidHideScope.Create([@"HID\VID_054C&PID_0CE6\ABC"]));
        ServerControllerInputFilter filter = new(new HidHideDeviceCatalog(runner), firewall);

        Assert.IsTrue(filter.Allows(Controller(@"\\?\HID#VID_054C&PID_0CE6#ABC")));
        Assert.IsTrue(filter.CreateSnapshot().IsCurrentScopeDevice(Controller(@"\\?\HID#VID_054C&PID_0CE6#ABC")));
    }

    /// <summary>HidHide stays active while the client run exists, even without foreground ownership.</summary>
    [TestMethod]
    public void CoordinatorKeepsScopeForInactiveClientRun()
    {
        FakeRunner runner = new(
            devList: "",
            appList: "SteamInputBridge.exe",
            cloakState: "off",
            inverseState: "off");
        using HidHideService firewall = CreateFirewall(runner);
        using ServiceProvider services = CreateProfileServices();
        ActiveClientRegistry clients = CreateActiveClient(out Guid clientId);
        ServerHidHideCoordinator coordinator = new(
            clients,
            NullLogger.Instance,
            services.GetRequiredService<ProfilesService>(),
            firewall,
            static _ => ["dev-1"],
            static _ => [],
            forwarding: null);

        coordinator.Refresh(clientId);
        coordinator.Refresh(null);

        Assert.Contains("--inv-off --cloak-on --dev-hide dev-1", runner.Commands);
        Assert.DoesNotContain("--dev-unhide dev-1 --cloak-off --inv-off", runner.Commands);
    }

    /// <summary>Coordinator diagnostics report HidHide's live app/device state.</summary>
    [TestMethod]
    public void CoordinatorStatusReadsLiveHidHideState()
    {
        FakeRunner runner = new(
            devList: "dev-1\r\ndev-2",
            appList: "SteamInputBridge.exe\r\nsteam.exe",
            cloakState: "on",
            inverseState: "off");
        using HidHideService firewall = CreateFirewall(runner);
        ServerHidHideCoordinator coordinator = new(
            new ActiveClientRegistry(),
            NullLogger.Instance,
            profiles: null,
            firewall,
            getDevices: null,
            static devices => [.. devices.Select(device => $"label:{device}")],
            forwarding: null);

        ServerHidHideStatus status = coordinator.GetStatus();

        Assert.IsTrue(status.CloakEnabled);
        Assert.IsFalse(status.InverseEnabled);
        CollectionAssert.AreEqual(StatusHiddenDevices, status.HiddenDevices.ToArray());
        Assert.AreEqual("label:dev-1,label:dev-2", string.Join(",", status.HiddenDeviceLabels));
        Assert.AreEqual("SteamInputBridge.exe,steam.exe", string.Join(",", status.RegisteredApplications));
    }

    /// <summary>Transient missing route snapshots do not unhide a running client's controller.</summary>
    [TestMethod]
    public void CoordinatorKeepsScopeAfterClientRouteTemporarilyDisappears()
    {
        FakeRunner runner = new(
            devList: "",
            appList: "SteamInputBridge.exe",
            cloakState: "off",
            inverseState: "off");
        using HidHideService firewall = CreateFirewall(runner);
        using ServiceProvider services = CreateProfileServices();
        ActiveClientRegistry clients = CreateActiveClient(out Guid clientId);
        bool reportDevice = true;
        ServerHidHideCoordinator coordinator = new(
            clients,
            NullLogger.Instance,
            services.GetRequiredService<ProfilesService>(),
            firewall,
            _ => reportDevice ? ["dev-1"] : [],
            static _ => [],
            forwarding: null);

        coordinator.Refresh(clientId);
        reportDevice = false;
        coordinator.Refresh(clientId);

        Assert.Contains("--inv-off --cloak-on --dev-hide dev-1", runner.Commands);
        Assert.DoesNotContain("--dev-unhide dev-1 --cloak-off --inv-off", runner.Commands);
    }

    /// <summary>Client end/disconnect clears the active HidHide scope.</summary>
    [TestMethod]
    public void CoordinatorClearsWhenClientRunEnds()
    {
        FakeRunner runner = new(
            devList: "",
            appList: "SteamInputBridge.exe",
            cloakState: "off",
            inverseState: "off");
        using HidHideService firewall = CreateFirewall(runner);
        using ServiceProvider services = CreateProfileServices();
        ActiveClientRegistry clients = CreateActiveClient(out Guid clientId);
        ServerHidHideCoordinator coordinator = new(
            clients,
            NullLogger.Instance,
            services.GetRequiredService<ProfilesService>(),
            firewall,
            static _ => ["dev-1"],
            static _ => [],
            forwarding: null);

        coordinator.Refresh(clientId);
        clients.RemoveClient(clientId);
        coordinator.Refresh(null);

        Assert.Contains("--inv-off --cloak-on --dev-hide dev-1", runner.Commands);
        Assert.Contains("--dev-unhide dev-1 --cloak-off --inv-off", runner.Commands);
    }

    /// <summary>HidHide device selection uses client-registered physical routes.</summary>
    [TestMethod]
    public async Task DeviceResolverUsesClientRegisteredPhysicalRoutes()
    {
        Guid clientId = Guid.NewGuid();
        using ControllerBroker broker = new(new FakeControllerOutputFactory());
        await using ControllerPipeSessions pipes = new(broker, NullLogger.Instance);
        ServerHidHideDeviceResolver resolver = new(
            new HidHideDeviceCatalog(new DeviceListRunner(DeviceListJson())),
            pipes);

        broker.RegisterClient(clientId, ForwardingControllerOutput.Ds4);
        _ = pipes.Start(clientId);
        _ = pipes.RegisterControllers(
            clientId,
            [new ClientControllerInfo(
                0,
                @"path:\\?\HID#VID_054C&PID_0CE6#ABC",
                "DualSense",
                ControllerFeatures.StandardControls,
                @"path:\\?\HID#VID_054C&PID_0CE6#ABC")]);

        IReadOnlyList<string> devices = resolver.GetDevicePaths(clientId);

        Assert.Contains(@"HID\VID_054C&PID_0CE6\ABC", devices);
    }

    private sealed class FakeRunner(
        string devList,
        string appList,
        string cloakState,
        string inverseState,
        string devAll = "") : IHidHideCommandRunner
    {
        public List<string> Commands { get; } = [];

        public string Run(IReadOnlyList<string> args)
        {
            string command = string.Join(" ", args);
            Commands.Add(command);
            return command switch
            {
                "--dev-all" => devAll,
                "--dev-list" => devList,
                "--app-list" => appList,
                "--cloak-state" => cloakState,
                "--inv-state" => inverseState,
                _ => "",
            };
        }
    }

    private static SdlControllerInfo Controller(string path)
    {
        SdlControllerInfo controller = new(
            default,
            InstanceId: 1,
            "DualSense Wireless Controller",
            SdlControllerSource.Physical,
            SteamHandle: 0,
            VendorId: 0x054c,
            ProductId: 0x0ce6,
            path,
            HasGyro: true,
            HasAccelerometer: true);
        return controller with { Id = SdlControllerId.Create(controller) };
    }

    private static string DeviceListJson()
    {
        return """
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
                    "deviceInstancePath": "HID\\VID_054C&PID_0CE6\\ABC",
                    "baseContainerDeviceInstancePath": "USB\\VID_054C&PID_0CE6\\BASE"
                  }
                ]
              }
            ]
            """;
    }

    private static string DeviceListWithSharedContainerJson()
    {
        return """
            [
              {
                "friendlyName": "DualSense Edge",
                "devices": [
                  {
                    "present": true,
                    "gamingDevice": true,
                    "vendor": "054C",
                    "product": "0DF2",
                    "usage": "Gamepad",
                    "symbolicLink": "\\\\?\\HID#VID_054C&PID_0DF2&MI_00#ABC",
                    "deviceInstancePath": "HID\\VID_054C&PID_0DF2&MI_00\\ABC",
                    "baseContainerDeviceInstancePath": "USB\\VID_054C&PID_0DF2\\BASE"
                  },
                  {
                    "present": true,
                    "gamingDevice": true,
                    "vendor": "054C",
                    "product": "0DF2",
                    "usage": "Gamepad",
                    "symbolicLink": "\\\\?\\HID#VID_054C&PID_0DF2&MI_03#ABC",
                    "deviceInstancePath": "HID\\VID_054C&PID_0DF2&MI_03\\ABC",
                    "baseContainerDeviceInstancePath": "USB\\VID_054C&PID_0DF2\\BASE"
                  }
                ]
              }
            ]
            """;
    }

    private static HidHideService CreateFirewall(
        FakeRunner runner,
        Func<IReadOnlyList<string>>? getApplicationAccessPaths = null,
        HidHideOwnedScopeStore? ownedScopeStore = null)
    {
        return new HidHideService(
            runner,
            getCurrentProcessPath: static () => "SteamInputBridge.exe",
            getApplicationAccessPaths: getApplicationAccessPaths,
            ownedScopeStore: ownedScopeStore);
    }

    private static ActiveClientRegistry CreateActiveClient(out Guid clientId)
    {
        clientId = Guid.NewGuid();
        ActiveClientRegistry clients = new();
        clients.RegisterClient(clientId, 100, "game", null, ["Game.exe"]);
        clients.UpdateClient(clientId, [new ObservedGameProcess(200, "Game.exe")]);
        clients.RefreshClients(200);
        return clients;
    }

    private static ServiceProvider CreateProfileServices()
    {
        Dictionary<string, string?> settings = new()
        {
            ["SteamInputBridge:Games:game:Title"] = "Game",
            ["SteamInputBridge:Games:game:Executable"] = @"C:\Games\Game.exe",
            ["SteamInputBridge:Games:game:ControllerOutput"] = "Ds4",
            ["SteamInputBridge:Games:game:ReceiverProcesses:0"] = "Game.exe",
        };
        IConfigurationRoot configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();
        ServiceCollection services = new();
        _ = services.AddSingleton<ILogger<ApplicationSettingsService>>(
            NullLogger<ApplicationSettingsService>.Instance);
        _ = services.AddSingleton<ILogger<ProfilesService>>(
            NullLogger<ProfilesService>.Instance);
        _ = services.AddApplicationSettings(configuration, "test-appsettings.json");
        _ = services.AddProfiles();
        return services.BuildServiceProvider();
    }

    private static string CreateTempDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), "SteamInputBridge.Tests", Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(directory);
        return directory;
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

    private sealed class FakeControllerOutputFactory : IControllerOutputFactory
    {
        public IControllerOutput Connect(ControllerId controllerId, ForwardingControllerOutput output)
        {
            _ = controllerId;
            _ = output;
            return new FakeControllerOutput();
        }
    }

    private sealed class FakeControllerOutput : IControllerOutput
    {
        public void Send(in ControllerState state)
        {
            _ = state;
        }

        public IDisposable ListenFeedback(Action<ControllerFeedback> handler)
        {
            _ = handler;
            return EmptyDisposable.Instance;
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }

    private sealed class EmptyDisposable : IDisposable
    {
        public static EmptyDisposable Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}
