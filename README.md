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

## TODO

- [ ] Fix CTRL-C handling on server and client (noop correctly).
- [ ] DS4 output contract and DS4-specific mapping.
- [ ] SDL-VIIPER touchpad, and other controller capability models.
  - [ ] rumble, trigger rumble, light-bar RGB, flash timing
  - [ ] buttons, sticks, triggers, gyro, accel, touchpad
- [ ] Rename project
- [ ] Merge refactored project files back into the main branch.
- [ ] Documentation and README.
- [ ] Implement proper Steam Input controller identification.
  - Current implementation only support a single physical Valve-made controller.
- [ ] Teensy output architecture and firmware.
- [ ] HidHide integration for physical controller blocking during xpad emulation.
- [ ] Packaging and deployment as a self-contained executable.
- [ ] Server WPF tray app with dashboard for diagnostics and profile/client management.
- [ ] Versioning, machine-readable diagnostics, and richer observability.
