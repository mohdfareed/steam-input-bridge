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

## Usage

Shortcut `Targets` can include `Motion`, `Pointer`, `Mic`, and `#RRGGBB`
overlay colors. The tray shows a top-right action color dot while color targets
are active, plus a mic dot when the system mic is muted or actively used.

## FIXME

- [ ] HidHide implementation is unstable. Conflicts with tools like DSX.
- [ ] HidHide sometimes does not hide the physical controller (for example, 2xKO).

## TODO

- [ ] Teensy mouse output and firmware.
- [ ] Packaging, versioning, deployment with install script and self-update (auto?).
- [ ] Update README.md and add usage examples and documentation.
- [ ] Update SDL package when Steam Controller touchpad support lands in the native NuGet package.
- [ ] Machine-readable diagnostics, and richer observability.
