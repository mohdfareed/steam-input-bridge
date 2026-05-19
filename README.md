# Local Input Forwarding

Project for local input forwarding transports.

## Projects

- `cli`: CLI harness
- `firmware`: microcontroller firmware source
- `scripts`: build, test, deployment, and CLI scripts
- `src/Inputs`: input source contracts, models, and implementations
- `src/Outputs`: output contracts, models, and implementations
- `src/Hosting`: local forwarding host/control process primitives
- `src/SteamInput`: Steam Input control helpers
- `cli/Tools`: CLI-only diagnostics and benchmark helpers
- `tests`: unit and integration tests

## Scripts

- `.\scripts\build.ps1` - build and format solution
- `.\scripts\test.ps1` - run tests
- `.\scripts\cli.ps1` - run CLI commands (see below)
- `.\scripts\deploy.ps1` - package and deploy the apps

## Runtime Timing

Current runtime defaults favor responsive game/controller changes without polling
hot paths:

- Foreground active-client checks: `100ms`
- Receiver process checks: `100ms`
- Client keepalive and reconnect retry: `1000ms`
- SDL controller discovery/reopen retry: `1000ms`
- SDL event wait wake/cancel timeout: `100ms`
- Tray status refresh: `500ms`
- Tray shutdown cleanup wait: `5s`

## TODO

- [ ] HidHide integration for physical controller blocking during xpad emulation.
- [ ] Rename project and merge refactored project, add docs, and update README.
- [ ] SDL-VIIPER DS4 support, and feedback capabilities.
  - [ ] touchpad, light-bar RGB, flash timing, trigger rumble
- [ ] Keyboard shortcuts support.
  - Toggle mouse output, motion output, Steam Input forcing
- [ ] Implement proper Steam Input controller identification.
  - Current implementation only support a single physical instance per controller model.
  - All Steam Input clients use VID/PID-based identification to pair with physical controllers.
  - Since all controllers of the same model share the same VID/PID, only one instance per model will be paired with all clients.
- [ ] Teensy output architecture and firmware.
- [ ] Packaging and deployment with install script and self-update (auto?).
- [ ] Versioning, machine-readable diagnostics, and richer observability.
