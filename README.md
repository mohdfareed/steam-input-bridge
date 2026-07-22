# Steam Input Bridge

Steam Input profile management of non-Steam games with controller/mouse emulation support.

## Requirements

- Steam, for Steam Input profiles and game shortcut support
- [VIIPER](https://github.com/Alia5/VIIPER) server/runtime, for virtual controller and mouse emulation
- Teensy 4.0 board (optional), for physical mouse emulation
- [Steam ROM Manager](https://github.com/SteamGridDB/steam-rom-manager) (optional), only if export SRM manifest

## Usage

- Start profiles through non-Steam games shortcut: `SteamInputBridge.App.exe shortcut <profile>`.
- A game profile can provide a custom Steam app ID to share Steam Input configurations between games.
- SRM manifest of all configured games can be exported for external management of non-Steam games.
- Controller emulation adds support for Steam Input in non-Steam games that don't recognize it.
  - It only support Steam Controllers to prevent double input.
  - This is due to controller hiding limitations preventing a clean solution to double input.
- Mouse emulation allows Steam Input to emulate a mouse in games that require a physical mouse.
  - It supports both VIIPER and a Teensy board. Board firmware is bundled with the app.
  - `"MouseInput": "Windows"` forwards Windows mouse input and is the default.
  - `"MouseInput": "Steam"` maps the resolved Steam virtual controller directly in the active client:
    right stick moves, RT/LT click left/right, RB/LB scroll down/up, and R3 middle-clicks.
- Keyboard shortcuts allow the following actions to be mapped in Steam Input configurations:
  - `Microphone` - toggles the system microphone, with an always-on-top indicator.
  - `#RRGGBB` - adds the color to the stack of active action colors.
    - Different sets/layers can map their always-on commands to shortcuts with different colors.
    - The color is an always-on-top indicator of the active Steam Input action set/layer.
  - `MousePointer` - toggles mouse outputs on/off (gyro control toggle).
    - This can be mapped to action sets dedicated for menus where the Steam Input mouse is supported.
    - If a game supports the Steam Input mouse, double input will occur if mouse output is not suppressed.
- Useful CLI commands:
  - `client run <profile>` - runs a profile and registers it with the server,
  - `server status [--json]` - shows the status of the server and its clients,
  - `steam list` - list installed Steam games and their app-ids,
  - `steam open-config [app-id]` - opens a Steam Input configuration, defaulting to the desktop configuration
  - `steam export [--path <path>]` - exports the SRM manifest for the configured game profiles.

The project provides support for two extra features/functionalities on DualSense controllers that I really liked:

- **Action colors:** the ability to set always-on commands in Steam Input action sets/layers.
  - This allows the controller to visually indicate the active action set/layer to know which controls are active.
  - The app provides the ability to define assign keyboard shortcuts to different colors.
  - The app provides an always-on-top indicator of the colors of the active keyboard shortcuts
  - The shortcuts can be mapped to always-on commands for action sets/layers in Steam Input.
  - This visually indicates the active Steam Input action set/layer and the active controls.
- **Microphone toggle:** the ability to toggle the microphone on a system level with a visual mute indicator.
  - This is a privacy feature to avoid accidentally talking in voice chat when not intended.
  - Also, Steam Input and Windows don't provide a universal mute shortcut that can be mapped natively.
  - The app provide an always-on-top indicator of mute and mic activity status.
  - It also provides a configurable mute keyboard shortcut that can be mapped to any controller button in Steam Input.

## Development

The app can be built and deployed locally for development personal usage.

**Requirements:**

- .NET 10 SDK
- PlatformIO CLI or the [VS Code extension](https://marketplace.visualstudio.com/items?itemName=platformio.platformio-ide)
- clang-format, available on PATH

Run the following to build and deploy the app locally.

```powershell
git clone "https://github.com/mohdfareed/steam-input-bridge.git"
cd "steam-input-bridge"
.\Scripts\Deploy-App.ps1 -Start
```

The following scripts are available for development:

- `.\Scripts\Build-Solution.ps1` - format/build the solution and Teensy firmware
- `.\Scripts\Test-Solution.ps1` - run unit tests and firmware tests
- `.\Scripts\CLI.ps1` - run CLI commands

## TODO

- [ ] Benchmark and optimize mouse/controller emulation performance.
- [ ] Packaging, versioning, deployment, and installation/update.
  - Bundle the PlatformIO Teensy board uploader with the app.
- [ ] Machine-readable diagnostics, and richer observability/logging.
