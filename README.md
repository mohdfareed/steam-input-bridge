# Steam Input Bridge

Steam Input orchestration and local input forwarding.

## Scripts

- `.\Scripts\Build-Solution.ps1` - build and format solution
- `.\Scripts\Test-Solution.ps1` - run normal tests
- `.\Scripts\Test-Solution.ps1 -Tier Dependency` - run explicit dependency tests
- `.\Scripts\Test-Solution.ps1 -Tier Manual` - run manual hardware/profile tests
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

## TODO

- [ ] Rewrite tray app using Windows standards.
- [ ] SDL-VIIPER DS4 support with gyro integration.
- [ ] Touchpad support and feedback capabilities.
  - Light-bar RGB, flash timing, trigger rumble
- [ ] Support multiple Valve controllers.
  - Implement proper Steam Input controller identification.
  - Current implementation only support a single physical instance per controller model.
  - All Steam Input clients use VID/PID-based identification to pair with physical controllers.
  - Since all controllers of the same model share the same VID/PID, only one instance per model will be paired with all clients.
- [ ] Teensy output and firmware.
- [ ] Packaging and deployment with install script and self-update (auto?).
- [ ] Update README.md and add usage examples and documentation.
- [ ] Versioning, machine-readable diagnostics, and richer observability.
