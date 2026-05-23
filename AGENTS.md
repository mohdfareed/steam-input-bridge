# AGENTS.md

## Purpose

This repository is Steam Input Bridge (`steam-input-bridge`): a .NET app for
Steam Input orchestration and local input forwarding to physical or virtual
output transports.

Keep discussion and code scoped to this repository.

## Maintenance

Treat this file as living project memory.

- Update it when a durable project rule, convention, or decision changes.
- Add only guidance likely to matter again.
- Keep temporary notes, debugging details, and one-off tasks out of this file.
- Keep it concise enough to be useful.
- Track non-obvious external behavior and route-specific tricks in
  `NOTES.md`; this file keeps the durable rule, while `NOTES.md` keeps the
  searchable one-look index.

## Stack

- .NET 10
- C# 14 via the SDK default
- VS Code solution support through `SteamInputBridge.slnx`

Do not set `LangVersion=latest`.

## Layout

- `SteamInputBridge`: one backend library project, `SteamInputBridge.csproj`
- `SteamInputBridge/Forwarding`: reports, brokers, mappings, and controller pipe frames
- `SteamInputBridge/HidHide`: HidHide command integration and profile firewall behavior
- `SteamInputBridge/Hosting`: server/client IPC, route orchestration, active-run handling
- `SteamInputBridge/Inputs/RawInput`: Windows Raw Input mouse source
- `SteamInputBridge/Inputs/Sdl`: SDL controller source and Steam/physical matching
- `SteamInputBridge/Outputs/Teensy`: planned Teensy output transport
- `SteamInputBridge/Outputs/Viiper`: VIIPER virtual output transports
- `SteamInputBridge/Runtime`: process launch, receiver discovery, foreground checks, jobs
- `SteamInputBridge/Settings`: app settings, profiles, validation, file logging
- `SteamInputBridge/Shortcuts`: global keyboard shortcut parsing and Windows hotkeys
- `SteamInputBridge/Steam`: Steam Input control, game discovery, shortcut export helpers
- `SteamInputBridge.App`: executable with tray, shortcut, and CLI modes
- `SteamInputBridge.Tests`: unified test project
- `firmware`: microcontroller-side code
- `scripts`: build, test, CLI, and deploy scripts

## Architecture

- Keep the API small and build only the MVP for this repository.
- Delete abandoned ideas. For useful but unstable features, isolate the code and
  keep it unwired from the default route behind one explicit integration point;
  do not comment out integration code.
- Do not make architecture-scale calls implicitly. Any change that decides
  which process owns an input source, output device, route lifecycle, HidHide
  policy, Steam visibility, or controller identity must be confirmed first.
- Treat "already there, just inactive" as a hypothesis to prove from code. If
  the live reader, writer, route, or integration point is missing, stop and ask
  before adding it.
- When a fix would cross server/client, input/output, hosting/runtime, or
  settings/runtime boundaries, first state the intended owner, affected files,
  and expected behavior. Wait for confirmation before editing.
- Do not create broad infrastructure to enable a feature unless that exact
  architecture has already been agreed. Prefer the smallest reversible wiring
  around existing code, and ask when the existing code does not support it.
- Before finalizing cleanup after a mistaken implementation, verify both the
  working tree and staged diff for leftover concept names and behavior. Removing
  only the obvious call site is not enough.
- Prefer direct pass-through over abstraction layers.
- Prefer maintained first-party or popular libraries when they reduce repo
  complexity.
- Keep input contracts under `SteamInputBridge/Inputs` and output contracts
  under `SteamInputBridge/Outputs`; inputs must not depend on outputs.
- Keep source-to-output orchestration in `SteamInputBridge/Hosting` and shared
  forwarding contracts/mapping in `SteamInputBridge/Forwarding`.
- Keep CLI-only diagnostics, probes, and benchmarks under `SteamInputBridge.App/Cli`
  or `SteamInputBridge.Tests`, not in the backend library.
- Do not add buffering, smoothing, batching, retries, or background pipelines
  unless explicitly requested.
- Treat 1000 Hz mouse-rate input and sub-2-5 ms added latency as hot-path
  design targets.
- Keep hot paths free of avoidable allocations, logging, JSON, and RPC.
- Use `SteamInputBridge` as the root settings section and named-pipe identity.
- Profile `ControllerOutput` and `MouseOutput` treat missing, JSON `null`, and
  explicit `None` as no output. Keep profile JSON clean by omitting no-output
  entries in normal settings files.
- Treat `ARCHITECTURE.md` as reference material, not binding design, when it
  conflicts with this file.

## Host And Client Model

- Prefer one local host process for production forwarding.
- The host owns Raw Input, VIIPER outputs, active-run gating, route-local
  feedback, profile resolution, foreground selection, physical controller slot
  matching, and cleanup.
- Clients launch or attach one profile run, read client-visible SDL controllers,
  stream controller reports to the host, handle route-local feedback, and
  release their run normally.
- Steam-launched clients can only read what Steam exposes to that process. Do
  not assume they can read the hidden physical controller.
- If physical controller data is needed while Steam hides the device from the
  client, the reader must be in a process that can see the physical device, such
  as the host or another explicitly designed companion. Choosing that process is
  an architecture decision that requires confirmation.
- Host IPC is control-only except for client-to-host controller report pipes.
  Do not forward mouse report traffic over IPC unless explicitly revisited.
- The default host/server pipe must have exactly one owner. Use async-safe
  process-wide ownership such as a named semaphore; do not rely on named-pipe
  exclusivity because Windows named pipes can have multiple server instances.
- Support multiple client runs. Only the foreground/needed run should drive
  outputs at a time.
- Active-client registry state should reflect the exact claimed foreground
  receiver. Do not keep Steam Input forcing active for unclaimed foreground
  windows.
- Forwarding gates may use a short grace window after an active client clears to
  mask transient foreground misses, but that grace must not drive Steam Input
  forcing.
- Disconnecting a client releases only that client run and its routes.
- Reconcile controller route changes per endpoint. Do not remove every
  controller endpoint for a client just because one controller appears,
  disappears, or changes identity.
- Apply client controller route snapshots atomically. Steam may reshuffle
  handles and client indices during normal input; do not transiently remove
  routes in a way that disconnects VIIPER outputs before re-adding them.
- Registered controller endpoints should create or keep their VIIPER output
  immediately with empty state; foreground activation only gates report data.
- Prune empty controller slots after endpoint removal so stale disconnected
  routes do not remain in diagnostics or output state.
- Separate durable configuration from runtime state. Profiles, controllers,
  games, and global settings are configuration; client runs, controller routes,
  process ids, and created device ids are state.
- Keep settings shapes easy to navigate in IDEs. Prefer one central settings
  model file for small settings records/enums; if a settings type must move,
  keep one obvious F12 path from the root settings object to that type.
- Treat receiver processes as the primary game lifetime signal. A profile
  executable is only a startup hint and may exit immediately.
- Only stop processes this repository launched or explicitly owns.
- Keep process launch, receiver discovery, foreground checks, and kill helpers
  in `SteamInputBridge/Runtime`; Hosting orchestrates them.
- Keep active-client state and receiver-process claims in
  `ActiveClientRegistry`; server loops own side effects such as forwarding gates
  and Steam forcing.
- Keep status/query methods side-effect free. Do not mutate forwarding,
  HidHide, Steam Input, route state, or device lifecycles from diagnostics or
  status reads; refresh that state from explicit lifecycle changes instead.
- When asked for a cleanup pass, do not narrow the work to the current diff
  unless the user explicitly scopes it that way. Read and reason through the
  relevant project files by responsibility before editing.

## Inputs

- Raw Input is the mouse source and runs inside the host process.
- Keep Raw Input Win32 interop as one coherent manual boundary. Do not use
  CsWin32 for it.
- Raw Input filtering is caller-driven; do not bake Steam-specific assumptions
  into `Inputs.RawInput`.
- SDL is the controller source. Use event-driven SDL reads, not a polling loop.
- Treat Steam-routed SDL controllers and physical SDL controllers as different
  discovered devices.
- Steam-launched clients first read exactly the SDL controllers Steam exposes,
  without clearing Steam-provided SDL hiding/filter flags.
- Clear SDL hiding/filter flags only as a fallback after selecting the primary
  controller and only to recover missing features such as motion.
- Match physical fallback controllers generically, such as exact VID/PID
  matching. Do not hardcode controller model VID/PID mappings in `src`.
- Do not treat Steam-routed `XInput#N` paths as physical controller identity.
  Use a strict matched physical counterpart when available; otherwise use the
  Steam controller handle or a client-local route id.
- Drop SDL `Physical` devices reported as Valve `28de:11ff` with `XInput#N`
  paths from forwarding. Logs showed these are Steam virtual XInput fallback
  devices, not stable physical route identities.
- SDL can report duplicate Steam-routed entries with the same Steam handle, such
  as `28de:11ff` XInput fallback plus the real Steam Controller. Treat
  duplicate stable SDL ids as one route and prefer the non-fallback entry.
- SDL can reuse a Steam handle while changing the reported controller identity
  during Steam Input rebuilds. Treat an open source as stale when VID/PID or
  name changes even if the SDL id, Steam handle, instance id, and path match.
- SDL controller instance ids are runtime/lease-local. Do not list controllers
  under one SDL lease and open those instance ids under another lease; select
  from the fresh list inside `SdlControllerCatalog.OpenControllers`.
- Keep controller route planning as pure policy code separate from SDL stream
  lifetime, pipe I/O, foreground activation, and VIIPER output ownership.
- Keep SDL initialized for the client process lifetime after first controller
  use. Do not call `SDL_QuitSubSystem` on reconnect; Steam Input controller
  visibility may not recover inside the already-launched client process.
- SDL touchpad input should carry touchpad click plus the first two touch
  contacts across SDL touchpad surfaces/fingers. Keep contact coordinates
  normalized until mapping into a concrete output report.
- The host-side physical controller pump maintains physical slots only. It must
  not create a VIIPER output unless a client route with controller output
  attaches to that physical slot.
- Do not treat a Steam-launched client as able to read Steam-hidden physical
  controllers. If missing physical features are needed while Steam hides the
  device, solve that outside the Steam-hidden client path.
- Each open `SdlGamepadSource` owns its own SDL runtime lease.

## Outputs

- VIIPER is the main virtual output handoff target.
- The host is the only process that writes to VIIPER outputs.
- Use one VIIPER created-device model: create one route-specific output device
  on connect and remove it on dispose.
- Mark created VIIPER devices with fixed route-specific VID/PID pairs and
  reclaim only owned devices on startup.
- Enforce one active VIIPER owner with an async-safe named ownership primitive.
- Remove VIIPER devices and buses before waiting on connected streams to
  dispose.
- VIIPER `DevId` values are bus-local. Use `BusID + DevId` when identifying a
  created VIIPER device.
- VIIPER list responses do not expose the display name passed during device
  creation. Do not rely on display name as a reclaim/ownership marker.
- Rapid VIIPER device recreation can transiently fail with auto-attach conflict
  or immediate bus-not-found responses. Retry the whole bus/device creation
  attempt narrowly; do not retry by adding another device to the same bus.
- Xbox 360 output uses Microsoft `045E:028E`; DS4 output should use Sony
  `054C:05C4` unless a newer DS4 profile specifically needs `054C:09CC`.
- VIIPER DS4 output uses device type `dualshock4`. Map SDL gyro from rad/s to
  DS4 raw as degrees/s scaled by 16, and map SDL accel from m/s² to DS4 raw
  scaled by 512. Use DS4 flat accelerometer default `Z=-5023` when no
  accelerometer is available.
- VIIPER DS4 output maps touchpad click and the first two normalized touch
  contacts into DS4 touchpad fields. Pressure is preserved in canonical state
  and controller pipe frames, but the VIIPER DS4 input report does not use it.
- Do not filter DS4 VIIPER loopback by `054C:05C4` alone because that is also
  a real DS4 identity. Require an app-owned name/path signal when treating a
  DS4-shaped SDL controller as VIIPER-owned.
- After host-side physical resolution, drop unresolved Steam-routed
  `054C:05C4` `PS4 Controller` streams. Steam can echo a VIIPER DS4 output
  back into the client as a generic PS4 stream; a real DS4 should resolve to a
  host-visible physical counterpart before it is allowed to create a route.
- In Steam-launched clients, use the first non-empty controller scan as the
  generic Steam DS4 baseline. If it had no generic Steam `PS4 Controller`, later
  generic Steam `054C:05C4` `PS4 Controller` entries are VIIPER DS4 echoes and
  should not be opened.
- DS4 feedback through VIIPER supports normal small/large motor rumble plus
  lightbar RGB and flash on/off. Do not treat DS4 as DualSense advanced
  haptics/adaptive trigger support.
- Fail on unsupported output ranges rather than silently clamping.
- Route rumble feedback back through the exact controller route that owns the
  virtual output.
- Teensy 4.0 is planned but not implemented; placeholders may throw
  `NotImplementedException`.

## HidHide

- Keep HidHide integration in `SteamInputBridge/HidHide`.
- App settings currently enable HidHide. When it is disabled, the server must
  not mutate HidHide state, hidden devices, or application lists.
- Current HidHide experimentation uses normal mode, not inverse mode: scoped
  physical devices are hidden globally, and this executable is kept on
  HidHide's app list.
- Register this executable with HidHide once at server startup. Do not add and
  remove it on every scope change.
- Normal-mode HidHide scopes are device-based. Do not require receiver
  executable paths to apply a scope; some games do not expose process paths to
  this app.
- Do not add Steam to the temporary HidHide app list just because profiles run
  through Steam. Manual testing showed Steam Input still feeds the client while
  only this executable is listed.
- Restore previous HidHide cloak/inverse state, hidden devices, and app-list
  entries when clearing a scope. Treat the app list as user-owned global state.
- Profiles should select output behavior, not store HidHide device paths.
- HidHide firewall behavior should derive hidden physical devices from running
  client receiver scopes, not foreground focus. Hide the union of physical
  controllers used by clients with controller output enabled from those clients'
  owned receiver executables.
- VIIPER devices should not leak to unrelated processes; expose them only where
  the active profile needs them.

## Steam And Shortcuts

- Product scope is Steam shortcut orchestration and forwarding Steam-visible
  input into games that do not handle it correctly.
- Keep Steam Input configuration forcing as activation orchestration, not
  per-report forwarding logic.
- Keep Steam file parsing in `SteamInputBridge/Steam`; read local Steam files
  defensively and cover parsers with fake Steam directories in tests.
- Use `ValveKeyValue` for Steam VDF parsing.
- Keep the Steam Input control API caller-facing: force config, clear forcing,
  open controller config, list games, and export shortcuts.
- Steam shortcut targets use `SteamInputBridge.exe shortcut <profile>`.
- Keyboard shortcuts are global server settings, not per-game behavior.
- Shortcut `Name` is optional; omit it in normal settings unless diagnostics
  need a display label.
- Shortcut entries use `Targets` as an array, even for one target.
- Shortcuts only set direct gates such as `Motion` and `Pointer` through
  `Enabled`, `Disabled`, `HoldEnabled`, `HoldDisabled`, or `Toggle`.

## CLI And Scripts

- Keep CLI, tray, and shortcut modes in `SteamInputBridge.App/SteamInputBridge.App.csproj`.
- CLI groups are `server`, `client`, `steam`, and `test`.
- Daily forwarding stays under `client run <profile>`.
- Diagnostics such as probes, raw input viewers, nullifiers, and benchmarks stay
  under `test`, not product-facing command groups.
- Do not add alternate CLI aliases before a release.
- CLI output should be concise, aligned, and value-first.
- `Scripts/Build-Solution.ps1`: format and build `SteamInputBridge.slnx`
- `Scripts/Test-Solution.ps1`: run `SteamInputBridge.Tests/SteamInputBridge.Tests.csproj`
- `Scripts/CLI.ps1`: run the SteamInputBridge CLI mode
- `Scripts/Deploy-App.ps1`: publish `SteamInputBridge.App/SteamInputBridge.App.csproj`

## Testing

- Add tests for new behavior as it is added.
- Use tests while developing, not only at the end.
- Do not run multiple `dotnet build` or `dotnet test` commands in parallel for
  the same configuration; they share `bin/obj` outputs and can lock each other.
- Keep tests focused on behavior and mapping, not internal structure.
- Keep all tests in `SteamInputBridge.Tests/SteamInputBridge.Tests.csproj`.
- Keep tests in three tiers:
  - Normal tests are untagged, deterministic, local/fake-output tests suitable for regular development and CI.
  - Dependency tests use MSTest `TestCategory("Dependency")` and require explicit external services or drivers such as VIIPER.
  - Manual tests use MSTest `TestCategory("Manual")` and require prepared user action or machine state such as connected controllers, Steam launch context, or a running receiver process.
- The default test script runs only normal tests. Explicit dependency/manual tests are opt-in and should report `Assert.Inconclusive` when required environment variables or hardware state are missing.

## Logging

- Do not write to console directly from library code.
- Do not create or manage log files from library code.
- Use `ILogger` only for lifecycle events.
- Do not log per-report hot-path traffic.
- File logging belongs in app/settings plumbing, not low-level libraries.

## Style

- Keep things concise.
- Avoid self-referential wording like "minimal" in project-facing text.
- Use explicit `using` directives.
- Prefer clear names over over-general names.
- Prefer a small number of coherent files over many tiny files when types are
  tightly related.
- When splitting code, keep related files under balanced folders. Avoid folders
  with 10+ loose files, and avoid folders with only 1-2 files unless they are
  placeholders.
- Avoid single-model-per-file layouts for small related contracts, options,
  status records, log helpers, or leases.
- Do not leave near-empty migration artifact files.
- Do not add private helpers that only wrap a constructor, null check, simple
  property access, or one obvious call.
- Use comments for non-obvious behavior, external quirks, ownership rules, and
  routing constraints instead of hiding intent behind tiny helper methods.
- Do not place source files under dot-prefixed folders in SDK-style projects.

## Section Markers

Use section markers only when they help structure a source file.

```csharp
// MARK: Section Name
// ========================================================================
```

The full separator line, including the leading `// `, must be exactly 79
characters wide.
