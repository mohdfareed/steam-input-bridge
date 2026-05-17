# AGENTS.md

## Purpose

This repository is for forwarding local input to physical or virtual output transports.
Keep discussion and code scoped to this repository only.
Do not mention other applications, launch flows, or the larger system context.

## Maintenance Rule

Treat this file as living project memory.

- Keep `AGENTS.md` updated when you learn stable preferences, conventions, design decisions, workflow expectations, or repo-specific rules that should carry forward to future tasks.
- Add only durable guidance that is likely to matter again.
- Prefer updating this file during the same task where the preference or rule becomes clear.
- Do not add temporary notes, one-off debugging details, or stale implementation trivia.
- Use `TODO.md` for durable refactoring backlog items; keep this file focused on conventions, design decisions, and workflow rules.

## Current Stack

- .NET 10
- C# 14 via the SDK default
- VS Code with solution support via `virtual-mouse.slnx`

Do not set `LangVersion=latest`.

## Project Shape

- `src/Inputs`: input source contracts and canonical input models
- `src/Inputs/RawInput`: Windows Raw Input mouse source
- `src/Inputs/Sdl`: SDL gamepad source
- `src/Outputs`: output contracts and output-format report models
- `src/Outputs/Viiper`: VIIPER output transports
- `src/Outputs/Teensy`: Teensy output transports
- `src/Hosting`: local forwarding host/control process primitives
- `src/SteamInput`: Steam Input control helpers
- `tests/Inputs.Tests`: input contract and source tests
- `tests/Outputs.Tests`: output contract and transport tests
- `tests/Hosting.Tests`: host/control tests
- `tests/SteamInput.Tests`: Steam Input helper tests
- `tests/Cli.Tests`: CLI command tests
- `cli`: CLI harness
- `cli/Tools`: CLI-only diagnostics and benchmark helpers
- `firmware`: microcontroller-side code
- `scripts`: repo scripts

## Design Rules

- Keep the API small.
- Build only the MVP for this repository.
- Prefer maintained first-party or popular libraries over hand-rolled infrastructure when they reduce repo complexity. Drop a dependency when it adds build, IDE, or project-structure friction that outweighs its value.
- Prefer direct pass-through over abstraction layers.
- Organize new non-mouse work around input sources, output devices, and hosting/orchestration.
- Group input projects under `src/Inputs` and output projects under `src/Outputs`, including their side-specific contracts.
- Avoid project-name sprawl. Prefer folders inside a coherent project when a split would create many thin projects.
- Keep shared input contracts and canonical input models in `src/Inputs`; keep output contracts and output-format reports in `src/Outputs`.
- Do not let `src/Inputs` depend on `src/Outputs`.
- Put source-to-output bridge helpers and output-specific mapping routes in `src/Hosting`, not in `src/Inputs`.
- Do not put shared gamepad, keyboard, or VIIPER code under a mouse-specific namespace.
- Organize Hosting by responsibility: `Forwarding`, `Routes`, `Host`, `Control`, and `Runtime`.
- Keep Hosting public API types in the `Hosting` namespace even when files are organized into responsibility folders.
- Do not split Hosting into one type or one tiny model per file. Keep tightly related route, host, runtime, and control types together in coherent files.
- Keep `src` limited to code that belongs in the final app backend. CLI-only diagnostics, benchmarks, probes, and manual testing helpers belong under `cli` or `tests`.
- CLI testing tools may use `InternalsVisibleTo("Cli")` for measurement hooks; do not move the tool implementation back into `src` just to access internals.
- Do not add buffering, smoothing, batching, retries, or background pipelines unless explicitly requested.
- Do not introduce latency or side effects in library code beyond what the underlying transport already requires.
- Keep transport connection APIs transport-specific.
- Keep mouse source-to-output forwarding on the shared `IMouseInputSource`/`IMouseOutput` path in `src/Hosting`; transports should provide source-name filtering through `IMouseOutput.FilterInput`.
- For future controller and keyboard paths, keep source and output contracts explicit instead of forcing mouse, keyboard, and gamepad state through one generic interface.
- Use SDL as the controller input source and map its standard gamepad state through hosting routes before sending to a concrete output such as Xbox 360 or DS4.
- Treat physical SDL gamepad input and Steam-routed SDL gamepad input as different discovered devices, not separate source modes in the public API. Mixed sources may combine Steam-routed buttons/axes with a strict physical motion counterpart.
- Do not hardcode controller model/type VID/PID mappings in `src`. Automatic counterpart matching must be generic, such as exact VID/PID matching. Model-specific cases belong in tests, explicit user configuration, or a future data-driven mapping layer if needed.
- SDL gamepad input should be event-driven through SDL events such as gamepad update-complete and sensor-update events, not an input polling loop.
- Do not add a shared factory or transport manager unless explicitly requested.
- Do not add a shared cross-transport options type.
- `IMouseOutput` represents a usable mouse session, not a transport factory.
- `IMouseInputSource` represents a usable mouse input session.
- Use direct callbacks for virtual mouse input hot paths; do not add event queues or buffering unless explicitly requested.
- Push back when a requested change leaks caller responsibility into this repository, expands scope, or adds avoidable hot-path overhead.
- Treat 1000 Hz mouse-rate input and sub-2-5 ms added latency as hot-path design targets.
- Keep hot-path performance and benchmark-able boundaries as top priorities when shaping shared interfaces and transport code.
- Prefer one local host process for production forwarding. The host owns Raw Input and the physical output transport; other processes should use control IPC instead of running their own forwarding loops.
- Keep host IPC control-only. Do not forward per-report mouse traffic over IPC unless explicitly revisited.
- Client control sessions may enable and disable route leases without disconnecting. Disconnecting a client must release any route leases it still holds.
- Host-owned global forwarding state, such as emulation enabled and physical motion enabled, may be mutated by short-lived clients without taking a forwarding lease.
- Expose Hosting through normal app-facing `ForwardingServer` and `ForwardingClient` APIs. Keep named-pipe control details behind those types.
- `ForwardingServer` should remain usable as a Microsoft `IHostedService` so CLI, tray, and WPF app hosts can compose it through Generic Host patterns.
- Host routes are explicit peers. Do not describe mouse route names as defaults when they are really route-specific names.
- Host single-instance ownership must be safe across async continuations; do not use a thread-affine lock that has to be released on the acquiring thread.
- Use `StreamJsonRpc` for host control IPC instead of maintaining a custom text protocol.
- Compose app-facing hosts with Microsoft Generic Host, configuration, options, and logging primitives instead of custom equivalents.
- Put durable Steam Input configuration forcing code in `src` as reusable library code; CLI tools should only expose or orchestrate it.
- Keep Steam file parsing in `src/SteamInput`; read local Steam files defensively and cover parsers with tests using fake Steam directories.
- Use `ValveKeyValue` for Steam VDF parsing instead of maintaining a custom parser.
- Expose Steam game discovery through static `SteamInputClient.ListGames`; keep library-folder, manifest, shortcut-path, install discovery, and parser helpers internal.
- Keep the Steam Input control API to caller-facing actions: force a config, clear forcing, and open controller config. Do not expose URI builders, duplicate static/instance variants, detected app state, or activation lifetime state without a real workflow.
- Keep `SteamGame` small for the current CLI: app id, name, entry kind, and one local path. Do not expose Steam shortcut icons, tags, launch options, or raw metadata until a real workflow needs them.
- Keep VIIPER server probing/startup logic in `src/Outputs/Viiper`; CLI code may orchestrate auto-start but should not own the probing or process-start rules.
- Keep durable responsibility, profile, configuration, and state ownership rules in `ARCHITECTURE.md`; update it when those decisions change.
- For context-sensitive 1000 Hz input, prefer client-local hot-path forwarding over per-report manager IPC.
- Keep manager responsibilities focused on configuration, profile resolution, leases, system policy, heartbeat, and cleanup.
- Keep client responsibilities focused on contextual input reads, route-local gates, direct output writes when latency requires it, heartbeat, and normal lease release.
- Separate durable configuration from runtime state. Profiles, games, controllers, and global settings are configuration; active leases, process ids, heartbeat, and created device ids are state.

## Current API Direction

- `IMouseOutput` is the base connected-session interface.
- `IMouseOutput.FilterInput` is the transport-owned loopback/source filter used by shared forwarding.
- `Hosting` owns enable/disable/status control for a local forwarding host.
- Connection entrypoints should live on concrete transport types.
- Input connection entrypoints should live on concrete source types.
- Use transport-specific options types when config becomes large enough to justify them.
- For tiny related contracts, prefer one file over many small files.

## VIIPER Notes

- Treat VIIPER as a direct handoff target.
- Map `MouseReport` directly to VIIPER mouse input.
- Map `Xbox360Report` directly to VIIPER Xbox 360 input.
- Route Xbox rumble feedback from the virtual output back to the SDL gamepad source when both sides support it.
- Treat Xbox rumble feedback as a held state, not a timed effect. SDL requires a duration, so the SDL adapter may use an effectively-held duration internally but must stop rumble explicitly on zero state and disposal.
- For virtual controller outputs, use the real controller USB identity expected by games and drivers. Xbox 360 output uses Microsoft `045E:028E`; DS4 should use Sony `054C:05C4` for the original DS4 unless a newer CUH-ZCT2 profile specifically needs `054C:09CC`.
- Fail on unsupported ranges rather than clamping silently.
- Keep implementation thin and explicit.
- Organize VIIPER by responsibility: device-specific outputs under `Mouse` and `Xbox360`, server startup under `Server`, and shared ownership/session/create-connect-reclaim/logging code under `Shared`.
- Keep VIIPER public API types in the `Outputs.Viiper` namespace even when files are organized into responsibility folders.
- Steam nullifier commands should ignore the owned VIIPER output device by VID/PID so the Steam path does not feed back on itself.
- Use one VIIPER session model: create one route-specific output device on connect and remove it on dispose.
- For VIIPER-created virtual devices, remove the device and bus before waiting on the connected stream to dispose. The generated client can block while an output read loop waits on the stream; stream shutdown must not block virtual-device removal.
- Mark created VIIPER devices with fixed route-specific VID/PID pairs and reclaim only those owned devices on startup.
- Enforce one active VIIPER owner with a named ownership primitive; concurrent instances should fail fast instead of competing, and ownership must be safe across async continuations.

## Teensy Notes

- Teensy 4.0 is planned but not implemented yet.
- Placeholders may throw `NotImplementedException` until the transport is designed.

## Raw Input Notes

- Treat Raw Input as the only virtual mouse input implementation until explicitly revisited.
- In the host model, Raw Input runs inside the local host process.
- Follow Microsoft's documented Raw Input model first.
- Keep Raw Input Win32 interop as one coherent manual boundary. Do not use CsWin32 or generator input files for this project.
- Prefer the performance-oriented documented path over simplifying code by adding per-report allocations or extra native calls.
- In a `WM_INPUT` handler, read the current event from `lParam` with `GetRawInputData`, then use `GetRawInputBuffer` only to drain additional queued events.
- Keep raw input filtering caller-driven; do not bake Steam-specific assumptions into `Inputs.RawInput`.
- Put durable source/transport interop logic in `src`; CLI tools should orchestrate and display results, not own reusable forwarding rules.
- Do not use SDL gamepad list indices as controller identity. SDL gamepad commands and host options should select controllers by stable `SdlGamepadId` or a unique device name, and reconnect should resolve the selected id again instead of reusing a stale list position.
- Keep SDL connected-source metadata grouped as `SdlGamepadInfo` objects such as `Device` and `MotionDevice`; do not re-expose flattened duplicate properties on `SdlGamepadSource`.
- The controller path is Steam-launched. First discover and read Steam Input controllers exactly as Steam exposes them, without clearing Steam-provided SDL hiding/filter flags.
- Clear Steam-provided SDL hiding/filter flags only as a fallback feature-discovery step after the primary controller is selected and only when the primary lacks a feature the app needs, such as motion. Match the backend physical controller strictly, then use it only for the missing feature. Do not replace primary buttons/axes with fallback physical input.
- For SDL fallback motion, choose the primary controller from the normal Steam-visible scan first, then reinitialize SDL with controller filters cleared before opening handles. Keep those process-local SDL filters cleared for the lifetime of the source if a backend motion device is open, then restore them on source disposal.
- Do not expose SDL diagnostic/filter-clearing switches as public source options. Filter clearing is an internal fallback detail, not caller policy.
- Use SDL Steam handles for Steam-routed controller selectors. Reconnect logic may fall back to matching the previous logical Steam controller by source type and VID/PID if the handle changes during a Steam Input device rebuild.
- Motion enable/disable is a source-level runtime toggle. Future feature toggles should follow the same source-level model when they select whether fallback feature data is emitted.
- Each open `SdlGamepadSource` must own its own SDL runtime lease. Do not share one disposable SDL lease across multiple sources; disposing one source must not shut SDL down under other open controllers.

## Gamepad Hosting Notes

- The Steam-launched client owns Steam Input visibility and forwards Steam-routed controller state to the host.
- A Steam-launched client must forward only Steam Input SDL controllers with Steam handles. Do not attach ordinary physical controllers or VIIPER virtual outputs discovered in the Steam process.
- The host owns physical SDL readers, VIIPER outputs, and merging Steam state with physical-only features such as motion.
- Use one host slot per physical controller id. A slot owns one physical SDL reader loop, one VIIPER output, and zero or more attached Steam client sessions.
- Client close or pipe break must detach only that client session. When the last session detaches from a slot, dispose that slot and its VIIPER output.
- Physical SDL disconnect should not destroy the VIIPER output while clients remain attached. Mark physical input disconnected and retry opening the physical source.
- Do not use a global physical gamepad pump that reloads all slots. Each slot owns its own physical reader loop and reconnect behavior.
- Do not block VIIPER output cleanup on SDL reader shutdown. Cancel the reader, remove the virtual output immediately, and observe reader completion separately.

## Testing Rules

- Add tests for new behavior as you add it.
- Use the tests to verify work while developing, not only at the end.
- Keep tests in the solution.
- Keep tests focused on behavior and mapping, not internal structure.
- For retained CLI tools, prefer `System.CommandLine` over a hand-rolled parser.
- Keep the CLI project at `cli/Cli.csproj`; do not put it under `tools`.
- Keep the CLI split into `host` for host lifecycle, `client` for normal host control, `steam` for Steam product features, and `test` for diagnostics and manual tools.
- Keep daily forwarding under `client run [--route <route>]`, not under top-level device command groups.
- Keep global host-state controls under direct client command groups, for example `client emulation enable|disable|toggle` and `client physical-motion enable|disable|toggle`.
- Keep one host process that owns all supported routes. Host startup config chooses route-specific setup such as the SDL gamepad device index, while each route connects lazily only when at least one client enables it.
- Keep diagnostics such as probes, raw input viewers, nullifiers, synthetic button presses, and benchmark commands under `test`, not under product-facing command groups.
- Do not add alternate CLI aliases before the project has had a release. Keep only the current intended command shape.
- Keep Steam Input controls under `steam` commands such as `list`, `force`, `clear`, and `open-config`; do not reuse `steam` as a mouse-forwarding command.
- CLI output should be concise, aligned, and value-first; avoid prose verdicts and unexplained benchmark jargon.
- CLI device stream output should use shared formatting helpers and one device label per line; avoid duplicating labels like both a leading name and `device="..."`.
- Keep benchmark entrypoints under `test mouse bench <output>` and `test xpad bench <output>`. They may print multiple measured repository boundaries for that pair, but do not reintroduce separate raw/bridge/all or xpad bench command trees.
- Keep benchmark mechanics under `cli/Tools/Benchmarks`; they are CLI testing tools, not app backend library code.
- Keep experimental standalone hot-path testbenches under `testing/tools`; do not promote them into `src` until their measured path is selected for the app.
- Do not fold external VIIPER client/device latency into repository-code benchmark claims.
- Put shared CLI option and validation helpers in `cli/Support/CliOptions.cs`; command files should reuse them instead of duplicating validators.

## Logging Rules

- Do not write to console directly from library code.
- Do not create or manage log files from library code.
- Use `ILogger` only when logging is needed.
- Keep logging at the transport level, not the shared base interface.
- Log lifecycle events only.
- Do not log per-report hot-path traffic.
- The local host may use `ILogger` for lifecycle events. The CLI should inject a console logger only; do not add file logging unless explicitly requested.

## Scripts

- `scripts/build.ps1`: build the solution
- `scripts/test.ps1`: run tests
- `scripts/cli.ps1`: run the CLI harness
- Prefer `scripts/build.ps1` over `scripts/build.ps1 -SkipFormat` for normal verification so formatting runs by default. Use `-SkipFormat` only when there is a specific reason to avoid formatting churn while iterating.

## Documentation Style

- Keep XML docs short.
- Use XML docs only where needed for the public API and warning baseline.
- Do not write fluffy or repetitive documentation.
- Implementation comments should complement the code, not narrate obvious lines.

Preferred style:

```csharp
// create a folder
step1
step2
step3
```

Avoid:

```csharp
// increment x
x++;
```

## Section Marker Style

Use section markers in source files when they help structure the file.

Format:

```csharp
// MARK: Section Name
// ========================================================================
```

The separator line is always 79 characters wide.

## Naming

- Use `Inputs`, `Outputs`, `Hosting`, and `SteamInput` as the top-level project buckets for new work.
- Keep namespaces aligned with project buckets: `Inputs`, `Inputs.RawInput`, `Inputs.Sdl`, `Outputs`, `Outputs.Viiper`, `Outputs.Teensy`, `Hosting`, and `SteamInput`.
- Prefer clear names over over-general names.

## Style Preferences

- Keep things concise.
- Avoid self-referential wording like "minimal" in project-facing text.
- Use explicit `using` directives.
- Prefer a small number of coherent files over many tiny files when the types are tightly related.
- Split logic-heavy code by responsibility; avoid large multi-responsibility files unless they are mostly repetitive pattern implementation.
- Avoid single-model-per-file layouts for small related contracts, options, status records, log helpers, or leases.
- Do not leave near-empty migration artifact files. Fold small constants and helpers into the file that owns the behavior.
- For CLI code, prefer a few coherent files grouped by command family or shared concerns.
- Do not collapse the CLI into one large file, and do not split it into many tiny files with barely any logic.
- Do not place source files under dot-prefixed folders in SDK-style projects; the default compile glob will skip them.
- Preserve the existing repo tone and conventions once established.
