# Steam Input Bridge

Steam Input profile management of non-Steam games with controller/mouse
emulation support.

## Requirements

- Steam, for Steam Input profiles and game shortcut support
- [VIIPER](https://alia5.github.io/VIIPER/stable/getting-started/installation/)
server/runtime, for virtual controller and mouse emulation
- Teensy 4.0 board, for physical mouse emulation
- [Steam ROM Manager](https://github.com/SteamGridDB/steam-rom-manager) (optional),
only if export SRM manifest

## Usage

| Command                        | Description                                                              |
| ------------------------------ | ------------------------------------------------------------------------ |
| `client run <profile>`         | Run a profile and register it with the server.                           |
| `server status [--json]`       | Show the server and connected clients.                                   |
| `steam list`                   | List installed Steam games and app IDs.                                  |
| `steam open-config [app-id]`   | Open a Steam Input configuration; defaults to the desktop configuration. |
| `steam export [--path <path>]` | Export configured profiles as a Steam ROM Manager manifest.              |

### Profiles and Steam shortcuts

Launch a profile from a non-Steam game shortcut:

```powershell
SteamInputBridge.App.exe shortcut <profile>
```

Configured profiles can be exported as a Steam ROM Manager manifest to
automate creating Steam shortcuts.
A profile can specify a custom Steam app ID, allowing multiple games to share
the same Steam Input configuration.

### Controller emulation

Controller emulation lets games that do not recognize Steam Input receive its
controller output. Only Steam Controllers are forwarded. Supporting other
physical controllers without reliable controller hiding could cause games to
receive both the physical and emulated inputs.

### Mouse emulation

Mouse output can use VIIPER or a Teensy 4.0 board. The Teensy firmware is bundled
with the app.

Mouse input has two modes:

- `"MouseInput": "Windows"` forwards Windows mouse input and is the default.
- `"MouseInput": "Steam"` maps the active Steam virtual controller directly:
  the right stick moves the pointer, RT/LT click left/right, RB/LB scroll
  down/up, and R3 middle-clicks.

### Steam Input shortcut actions

Keyboard shortcuts expose app actions to Steam Input. This supports the two
DualSense workflows that motivated the project—action-set color indicators and
a controller-mappable system mute—along with mouse-output control:

- `Microphone` toggles the system microphone and displays an always-on-top mute
  and activity indicator. The shortcut can be mapped to any controller button.
- `#RRGGBB` adds a color to the active color stack. Map different always-on
  commands in each action set or layer to show which controls are active.
- `MousePointer` toggles mouse output, which is useful for menu-specific action
  sets. Disable emulated output when a game uses Steam Input's own mouse support
  to avoid double input.

## Development

The app can be built and installed locally for development and personal usage.

**Requirements:**

- .NET 10 SDK
- PlatformIO CLI on PATH or the PlatformIO
[VS Code extension](https://marketplace.visualstudio.com/items?itemName=platformio.platformio-ide)
- clang-format on PATH

### Scripts

- `.\Scripts\Build-Solution.ps1` - format and build the solution and Teensy firmware
- `.\Scripts\Test-Solution.ps1` - run unit tests
- `.\Scripts\Deploy-App.ps1 [-Start]` - publish the app and optionally start it

### Installation

Run the following to build and deploy the app locally.

```powershell
git clone "https://github.com/mohdfareed/steam-input-bridge.git"
cd "steam-input-bridge"
.\Scripts\Install-App.ps1 -Local
```

The installer uses Windows' current-user Programs directory (normally
`%LOCALAPPDATA%\Programs\SteamInputBridge`), creates a Start Menu shortcut,
adds `steam-input-bridge` to the current user's PATH, installs VIIPER when
needed, and starts the app. Open a new terminal after installation to use the
CLI:

```powershell
steam-input-bridge --help
```

At runtime, the first existing `appsettings.json` is used from the current
working directory, the executable directory, or `%LOCALAPPDATA%\SteamInputBridge`,
in that order. If none exists, the Local AppData path is selected without
creating the file. Logs are always written to a `logs` directory beside the
selected settings file unless `Logging:LogDirectory` overrides it. Relative
paths such as `./logs` are resolved from the settings file's directory.

### Uninstallation

The installer places a standalone uninstaller beside the installed app:

```powershell
& "$env:LOCALAPPDATA\Programs\SteamInputBridge\Uninstall-App.ps1"
```

The repository copy runs the same uninstall:

```powershell
.\Scripts\Uninstall-App.ps1
```

It preserves `%LOCALAPPDATA%\SteamInputBridge` by default; add `-Purge` to
remove that user data too. The CLI's PATH entry is removed automatically.
VIIPER is a shared dependency and is not uninstalled.

## TODO

- [ ] Packaging, versioning, deployment, and installation/update.
  - Bundle the PlatformIO Teensy board uploader with the app.
  - [x] Add install/update scripts for the app
  - [ ] Add workflows for building and deploying the app package
  - [ ] Add support to install/update scripts for GitHub releases and/or artifacts
    - [ ] Tray app update button and indicator
    - [x] Tray app uninstall button
- [ ] Machine-readable diagnostics, and richer observability/logging.
