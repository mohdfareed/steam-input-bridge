# Steam Input Bridge

Steam Input orchestration and local input forwarding.

## Scripts

- `.\Scripts\Build-Solution.ps1` - build and format solution
- `.\Scripts\Test-Solution.ps1` - run tests
- `.\Scripts\Deploy-App.ps1` - package and deploy the apps
- `.\Scripts\CLI.ps1` - run CLI commands (see below)

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

## Profile Outputs

- `ControllerOutput: None` leaves the native physical controller path alone.
- `ControllerOutput: Xbox360` or `Ds4` means virtual controller replacement:
  client-visible input is forwarded to VIIPER and the matching physical
  controller is hidden from the receiver.
- Keyboard shortcuts are global server settings. They set `Motion` or `Pointer`
  to `Enabled` or `Disabled`; Steam Input owns any hold/toggle/action-layer
  behavior that decides when those shortcuts fire.

## TODO

- [ ] Rewrite tray app using Windows standards.
- [ ] SDL-VIIPER DS4 support with gyro integration.
- [ ] Touchpad support and feedback capabilities.
  - Light-bar RGB, flash timing, trigger rumble
- [ ] Implement proper Steam Input controller identification.
  - Current implementation only support a single physical instance per controller model.
  - All Steam Input clients use VID/PID-based identification to pair with physical controllers.
  - Since all controllers of the same model share the same VID/PID, only one instance per model will be paired with all clients.
- [ ] Teensy output and firmware.
- [ ] Packaging and deployment with install script and self-update (auto?).
- [ ] Versioning, machine-readable diagnostics, and richer observability.
