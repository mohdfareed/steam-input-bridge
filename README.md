# Steam Input Bridge

Steam Input orchestration and local input forwarding.

## Requirements

- Steam, for Steam Input profiles and game shortcut support
- VIIPER server/runtime, for virtual controller and mouse output
- Teensy 4.0 hardware, for Teensy mouse output
- Steam ROM Manager (optional), only if using SRM shortcut export feature

## Usage

- Start profiles through Steam shortcuts: `SteamInputBridge.App.exe shortcut <profile>`.
- Controller forwarding is Steam Controller focused; other physical controller
  types are ignored.
- Profiles may launch an executable or attach to existing processes.
- `ControllerOutput` and `MouseOutput` create VIIPER devices only when configured.
- `MouseOutput` also supports a Teensy board mouse emulator.
- Keyboard shortcut `Targets` can include `MousePointer`, `Microphone`, and `#RRGGBB`.
  - `MousePointer` toggle mouse outputs on/off (gyro control toggle).
  - `Microphone` toggles the system microphone, with an always-on-top indicator (mic activity indicator).
  - `#RRGGBB` adds the color to the stack of active colors (action set/layer indicator).
- Profile and shortcut edits reload while the app is running.
- SRM manifest export runs from the tray or CLI.
- Useful CLI commands: `client run <profile>`, `server status [--json]`,
    `steam list`, `steam open-config [app-id]`, `steam export [--path <path>]`.

## Development

- `.\Scripts\Build.ps1` - format/build the solution and build Teensy firmware
- `.\Scripts\Test-Solution.ps1` - run normal tests (`-Tier Dependency` or `-Tier Manual` for opt-in tests)
- `.\Scripts\Deploy-App.ps1` - build, package, and deploy the app
- `.\Scripts\Deploy-App.ps1 -Start` - build and deploy then start the tray app
- `.\Scripts\CLI.ps1` - run CLI commands

### Requirements

- .NET 10 SDK
- PlatformIO CLI or the VS Code PlatformIO extension

## TODO

- [ ] Packaging, versioning, deployment with install script and self-update (auto?).
- [ ] Update README.md and add usage examples and documentation.
- [ ] Machine-readable diagnostics, and richer observability.
