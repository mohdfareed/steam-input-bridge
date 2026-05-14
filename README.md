# Virtual Mouse

Project for physical mouse forwarding transports.

## Projects

- `src/PhysicalMouse`: core contracts
- `src/PhysicalMouse.Viiper`: VIIPER transport
- `src/PhysicalMouse.Teensy`: Teensy 4.0 transport
- `tests/PhysicalMouse.Tests`: tests
- `tools/PhysicalMouse.Cli`: CLI for connection checks, visible motion tests, and send benchmarks
- `firmware`: microcontroller-side work

## Scripts

- `.\scripts\build.ps1`
- `.\scripts\test.ps1`
- `.\scripts\cli.ps1`
- `.\scripts\steam.ps1`
- `.\scripts\builld-testbench.ps1`

## TODO

- Replace the current single-owner VIIPER session model with a proper broker if concurrent game instances need to share one output mouse cleanly.
