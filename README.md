# Local Input Forwarding

Project for local input forwarding transports.

## Architecture

- `Inputs` owns canonical input models and source implementations.
- `Outputs` owns output reports and output sessions.
- `Hosting` exposes `ForwardingServer`/`ForwardingClient` APIs and owns source-to-output routing plus local host process ownership.
- `cli` wires commands to library code, prints results, and owns CLI-only tools.

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

## CLI

- `.\scripts\cli host run`: run the local forwarding host
- `.\scripts\cli host status`: print host and route status
- `.\scripts\cli host stop`: request the running host to stop
- `.\scripts\cli client run --route <mouse|xpad>`: enable a route for the client lifetime
- `.\scripts\cli client emulation <enable|disable|toggle>`: control the global emulation gate
- `.\scripts\cli client physical-motion <enable|disable|toggle>`: control the global physical motion gate
- `.\scripts\cli test mouse input`: print Raw Input mouse reports
- `.\scripts\cli test mouse nullify`: forward inverted Raw Input mouse reports for testing
- `.\scripts\cli test mouse bench viiper`: benchmark Raw Input to VIIPER boundaries
- `.\scripts\cli test xpad probe`: list SDL gamepads
- `.\scripts\cli test xpad input`: print SDL gamepad state changes
- `.\scripts\cli test xpad press`: send a short VIIPER Xbox 360 test report
- `.\scripts\cli test xpad bench viiper`: benchmark SDL to VIIPER boundaries
- `.\scripts\cli steam list`: list Steam and non-Steam games
- `.\scripts\cli steam force <app-id>`: force a Steam Input config
- `.\scripts\cli steam clear`: clear Steam Input config forcing

## TODO

- [ ] Fix CTRL-C handling on server and client (noop correctly).
- [ ] DS4 output contract and DS4-specific mapping.
- [ ] SDL-VIIPER touchpad, and other controller capability models.
  - [ ] rumble, trigger rumble, light-bar RGB, flash timing
  - [ ] buttons, sticks, triggers, gyro, accel, touchpad
- [ ] Rename project
- [ ] Merge refactored project files back into the main branch.
- [ ] Teensy output architecture and firmware.
- [ ] Implement proper Steam Input controller identification.
  - Current implementation only support a single physical Valve-made controller.
- [ ] HidHide integration for physical controller blocking during xpad emulation.
- [ ] Server WPF tray app with dashboard for diagnostics and profile/client management.
- [ ] Packaging and deployment as a self-contained executable.
- [ ] Versioning, machine-readable diagnostics, and richer observability.
