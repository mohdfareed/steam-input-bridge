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

- [ ] Overlay indicators for active action set/layer and mic status.
- [ ] Teensy mouse output and firmware.
- [ ] Packaging, versioning, deployment with install script and self-update (auto?).
- [ ] Update README.md and add usage examples and documentation.
- [ ] Update SDL package when Steam Controller touchpad support lands in the native NuGet package.
- [ ] Machine-readable diagnostics, and richer observability.
