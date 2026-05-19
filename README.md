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

- [ ] Create tray app for hosting server.
- [ ] SDL-VIIPER DS4 support, and feedback capabilities.
  - [ ] touchpad, light-bar RGB, flash timing, trigger rumble
  - [ ] Force DualSense mic button/LED to toggle system mic and follow its state (on/off).
- [ ] Keyboard shortcuts support.
  - Toggle mouse output, motion output, Steam Input forcing
- [ ] Rename project and merge refactored project, add docs, and update README.
- [ ] Implement proper Steam Input controller identification.
  - Current implementation only support a single physical instance per controller model.
  - All Steam Input clients use VID/PID-based identification to pair with physical controllers.
  - Since all controllers of the same model share the same VID/PID, only one instance per model will be paired with all clients.
- [ ] Teensy output architecture and firmware.
- [ ] HidHide integration for physical controller blocking during xpad emulation.
- [ ] Packaging and deployment as a self-contained executable.
- [ ] Server WPF tray app with dashboard for diagnostics and profile/client management.
- [ ] Versioning, machine-readable diagnostics, and richer observability.
