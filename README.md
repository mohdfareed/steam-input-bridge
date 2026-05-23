# Steam Input Bridge

Steam Input orchestration and local input forwarding.

## Requirements

- Steam, for Steam Input profiles and game shortcut support
- VIIPER server/runtime, for virtual controller and mouse output
- HidHide, for hiding physical controller duplicates when enabled
- Steam ROM Manager (optional), only if using SRM shortcut export feature

## Development

- `.\Scripts\Build-Solution.ps1` - build and format solution
- `.\Scripts\Test-Solution.ps1` - run normal tests
- `.\Scripts\Deploy-App.ps1` - package and deploy the apps
- `.\Scripts\CLI.ps1` - run CLI commands (see below)

### Requirements

- .NET 10 SDK

## Runtime Timing

- Foreground active-client checks: `100ms`
- Receiver process checks: `100ms`
- Client keepalive and reconnect retry: `1000ms`
- SDL controller discovery/reopen retry: `1000ms`
- SDL event wait wake/cancel timeout: `100ms`
- Tray status refresh: `500ms`
- Tray shutdown cleanup wait: `5s`

## TODO

- [ ] Update SDL dependency to add Steam Controller touchpad support.
- [ ] Fix un-deterministic identification of Steam Input controllers.
  - Steam Input shuffles controller IDs whenever a controller is disconnected or reconnected.
  - This causes unstable mapping to physical controllers.
  - Restarting the server resolves the issue.
- [ ] Support multiple controllers of the same model.
  - Current implementation only support a single physical instance per controller model.
  - This is due to the identification method revolving around the vendor and product IDs.
- [ ] Teensy output and firmware.
- [ ] Packaging and deployment with install script and self-update (auto?).
- [ ] Update README.md and add usage examples and documentation.
- [ ] Versioning, machine-readable diagnostics, and richer observability.
