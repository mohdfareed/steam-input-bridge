# Virtual Mouse

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

- `.\scripts\cli host run --route mouse`: run the mouse forwarding host
- `.\scripts\cli host run --route xpad`: run the xpad forwarding host
- `.\scripts\cli host enable --route <mouse|xpad>`: enable a running host route
- `.\scripts\cli host status --route <mouse|xpad>`: print host route status
- `.\scripts\cli mouse run`: start Raw Input mouse forwarding
- `.\scripts\cli xpad run`: enable a running xpad host route
- `.\scripts\cli xpad probe`: list SDL gamepads
- `.\scripts\cli xpad input`: print SDL gamepad state changes
- `.\scripts\cli xpad test`: send a short VIIPER Xbox 360 test report
- `.\scripts\cli steam list`: list Steam and non-Steam games
- `.\scripts\cli steam force <app-id>`: force a Steam Input config
- `.\scripts\cli steam clear`: clear Steam Input config forcing
- `.\scripts\cli bench <raw|sdl> <viiper|teensy>`: benchmark input to output boundaries

## TODO

- [ ] Rumble and force feedback routing.
- [ ] DS4 output contract and DS4-specific mapping.
- [ ] SDL gyro, accelerometer, touchpad, and other controller capability models.
- [ ] Teensy output architecture and firmware.
- [ ] Packaging/runtime distribution for SDL3 and VIIPER dependencies.
- [ ] Host protocol versioning, machine-readable diagnostics, and richer
      observability.
