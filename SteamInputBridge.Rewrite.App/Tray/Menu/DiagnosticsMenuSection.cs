using System.Windows.Forms;
using SteamInputBridge.Hosting;
using SteamInputBridge.Microphone;

namespace SteamInputBridge.App.Tray.Menu;

internal sealed class DiagnosticsMenuSection
{
    private ToolStripMenuItem? _microphone;
    private ToolStripMenuItem? _actionColor;
    private ToolStripMenuItem? _mouse;
    private ToolStripMenuItem? _mouseOutput;
    private ToolStripMenuItem? _mouseForwarding;
    private ToolStripMenuItem? _mousePointer;
    private ToolStripMenuItem? _controller;
    private ToolStripMenuItem? _controllerClient;
    private ToolStripMenuItem? _controllerSteam;
    private ToolStripMenuItem? _controllerVirtual;
    private ToolStripMenuItem? _steamConfig;

    // MARK: Publics
    // ========================================================================

    public ToolStripMenuItem Build(BridgeServerStatus status, MicrophoneStatus microphone, string? actionColor)
    {
        ToolStripMenuItem menu = TrayMenuItems.Menu("Diagnostics");
        _mouse = TrayMenuItems.Menu("Mouse");
        TrayMenuItems.SetCheckMark(_mouse, status.Mouse.Forwarding);
        _mouseOutput = TrayMenuItems.Item("Output", status.Mouse.Output);
        TrayMenuItems.SetCheckMark(_mouseOutput, status.Mouse.OutputConnected);
        _mouseForwarding = TrayMenuItems.Item("Forwarding", TrayMenuItems.Active(status.Mouse.Forwarding));
        TrayMenuItems.SetCheckMark(_mouseForwarding, status.Mouse.Forwarding);
        _mousePointer = TrayMenuItems.Item("Pointer", TrayMenuItems.Enabled(status.Mouse.PointerEnabled));
        TrayMenuItems.SetCheckMark(_mousePointer, status.Mouse.PointerEnabled);

        _ = _mouse.DropDownItems.Add(_mouseOutput);
        _ = _mouse.DropDownItems.Add(_mouseForwarding);
        _ = _mouse.DropDownItems.Add(_mousePointer);

        _controller = TrayMenuItems.Menu("Controller");
        TrayMenuItems.SetCheckMark(_controller, status.Controller.Forwarding);
        _controllerClient = TrayMenuItems.Item("Client", status.Controller.Client);
        TrayMenuItems.SetCheckMark(_controllerClient, status.Controller.Client == "Active");
        _controllerSteam = TrayMenuItems.Item("Steam controllers", TrayMenuItems.Number(status.Controller.SteamControllers));
        TrayMenuItems.SetCheckMark(_controllerSteam, status.Controller.SteamControllers > 0);
        _controllerVirtual = TrayMenuItems.Item("Virtual controllers", TrayMenuItems.Number(status.Controller.VirtualControllers));
        TrayMenuItems.SetCheckMark(_controllerVirtual, status.Controller.VirtualControllers > 0);

        _ = _controller.DropDownItems.Add(_controllerClient);
        _ = _controller.DropDownItems.Add(_controllerSteam);
        _ = _controller.DropDownItems.Add(_controllerVirtual);

        _microphone = TrayMenuItems.Item("Microphone", FormatMicrophone(microphone));
        TrayMenuItems.SetCheckMark(_microphone, IsMicrophoneActive(microphone));
        _actionColor = TrayMenuItems.Item("Action color", FormatActionColor(actionColor));
        TrayMenuItems.SetCheckMark(_actionColor, HasActionColor(actionColor));
        _steamConfig = TrayMenuItems.Item("Steam config", FormatSteamConfig(status.SteamInput));
        TrayMenuItems.SetCheckMark(_steamConfig, HasSteamConfig(status.SteamInput));

        _ = menu.DropDownItems.Add(_mouse);
        _ = menu.DropDownItems.Add(_controller);
        _ = menu.DropDownItems.Add(_microphone);
        _ = menu.DropDownItems.Add(_actionColor);
        _ = menu.DropDownItems.Add(_steamConfig);
        return menu;
    }

    public void Update(BridgeServerStatus status, MicrophoneStatus microphone, string? actionColor)
    {
        SetMouse(status.Mouse);
        SetController(status.Controller);

        if (_microphone is not null)
        {
            TrayMenuItems.SetValue(_microphone, FormatMicrophone(microphone));
            TrayMenuItems.SetCheckMark(_microphone, IsMicrophoneActive(microphone));
        }

        if (_actionColor is not null)
        {
            TrayMenuItems.SetValue(_actionColor, FormatActionColor(actionColor));
            TrayMenuItems.SetCheckMark(_actionColor, HasActionColor(actionColor));
        }

        if (_steamConfig is not null)
        {
            TrayMenuItems.SetValue(_steamConfig, FormatSteamConfig(status.SteamInput));
            TrayMenuItems.SetCheckMark(_steamConfig, HasSteamConfig(status.SteamInput));
        }
    }

    // MARK: Format
    // ========================================================================

    private static string FormatMicrophone(MicrophoneStatus status)
    {
        return status switch
        {
            { Available: false } => "None",
            { Muted: true } => "Muted",
            { IsActive: true } => "Active",
            _ => "Available",
        };
    }

    private static string FormatActionColor(string? color)
    {
        return string.IsNullOrWhiteSpace(color) ? "None" : color;
    }

    private static bool IsMicrophoneActive(MicrophoneStatus status)
    {
        return status.Available && !status.Muted && status.IsActive;
    }

    private static bool HasActionColor(string? color)
    {
        return !string.IsNullOrWhiteSpace(color);
    }

    private void SetMouse(BridgeMouseStatus mouse)
    {
        if (_mouse is not null)
        {
            TrayMenuItems.SetCheckMark(_mouse, mouse.Forwarding);
        }

        if (_mouseOutput is not null)
        {
            TrayMenuItems.SetValue(_mouseOutput, mouse.Output);
            TrayMenuItems.SetCheckMark(_mouseOutput, mouse.OutputConnected);
        }

        if (_mouseForwarding is not null)
        {
            TrayMenuItems.SetValue(_mouseForwarding, TrayMenuItems.Active(mouse.Forwarding));
            TrayMenuItems.SetCheckMark(_mouseForwarding, mouse.Forwarding);
        }

        if (_mousePointer is not null)
        {
            TrayMenuItems.SetValue(_mousePointer, TrayMenuItems.Enabled(mouse.PointerEnabled));
            TrayMenuItems.SetCheckMark(_mousePointer, mouse.PointerEnabled);
        }
    }

    private void SetController(BridgeControllerStatus controller)
    {
        if (_controller is not null)
        {
            TrayMenuItems.SetCheckMark(_controller, controller.Forwarding);
        }

        if (_controllerClient is not null)
        {
            TrayMenuItems.SetValue(_controllerClient, controller.Client);
            TrayMenuItems.SetCheckMark(_controllerClient, controller.Client == "Active");
        }

        if (_controllerSteam is not null)
        {
            TrayMenuItems.SetValue(_controllerSteam, TrayMenuItems.Number(controller.SteamControllers));
            TrayMenuItems.SetCheckMark(_controllerSteam, controller.SteamControllers > 0);
        }

        if (_controllerVirtual is not null)
        {
            TrayMenuItems.SetValue(_controllerVirtual, TrayMenuItems.Number(controller.VirtualControllers));
            TrayMenuItems.SetCheckMark(_controllerVirtual, controller.VirtualControllers > 0);
        }
    }

    private static string FormatSteamConfig(BridgeSteamInputStatus status)
    {
        return !string.IsNullOrWhiteSpace(status.LastError)
            ? "Error"
            : string.IsNullOrWhiteSpace(status.ProfileId) ? "None" : status.ProfileId;
    }

    private static bool HasSteamConfig(BridgeSteamInputStatus status)
    {
        return string.IsNullOrWhiteSpace(status.LastError) &&
            !string.IsNullOrWhiteSpace(status.ProfileId);
    }
}
