# AGENTS.md

## Purpose

This repository is for forwarding mouse input to a physical mouse transport.
Keep discussion and code scoped to this repository only.
Do not mention other applications, launch flows, or the larger system context.

## Maintenance Rule

Treat this file as living project memory.

- Keep `AGENTS.md` updated when you learn stable preferences, conventions, design decisions, workflow expectations, or repo-specific rules that should carry forward to future tasks.
- Add only durable guidance that is likely to matter again.
- Prefer updating this file during the same task where the preference or rule becomes clear.
- Do not add temporary notes, one-off debugging details, or stale implementation trivia.

## Current Stack

- .NET 10
- C# 14 via the SDK default
- VS Code with solution support via `virtual-mouse.slnx`

Do not set `LangVersion=latest`.

## Project Shape

- `src/PhysicalMouse`: shared contracts
- `src/PhysicalMouse.Viiper`: VIIPER transport
- `src/PhysicalMouse.Teensy`: Teensy 4.0 transport
- `src/VirtualMouse`: shared input contracts
- `src/VirtualMouse.RawInput`: Windows Raw Input source
- `src/VirtualMouse.SteamInput`: Steam Input source placeholder
- `tests/PhysicalMouse.Tests`: tests
- `tests/VirtualMouse.Tests`: virtual mouse tests
- `tools/PhysicalMouse.Cli`: CLI harness
- `tools/SteamInput.TestBench`: interactive Steam Input testbench
- `tools/SteamInput.TestBench.Launcher`: publishes and launches the latest Steam Input testbench for Steam shortcuts
- `firmware`: microcontroller-side code
- `scripts`: repo scripts

## Design Rules

- Keep the API small.
- Build only the MVP for this repository.
- Prefer direct pass-through over abstraction layers.
- Do not add buffering, smoothing, batching, retries, or background pipelines unless explicitly requested.
- Do not introduce latency or side effects in library code beyond what the underlying transport already requires.
- Keep transport connection APIs transport-specific.
- Do not add a shared factory or transport manager unless explicitly requested.
- Do not add a shared cross-transport options type.
- `IPhysicalMouse` represents a usable mouse session, not a transport factory.
- `IVirtualMouse` represents a usable mouse input session.
- Use direct callbacks for virtual mouse input hot paths; do not add event queues or buffering unless explicitly requested.
- Push back when a requested change leaks caller responsibility into this repository, expands scope, or adds avoidable hot-path overhead.
- Treat 1000 Hz mouse-rate input and sub-2-5 ms added latency as hot-path design targets.

## Current API Direction

- `IPhysicalMouse` is the base connected-session interface.
- Connection entrypoints should live on concrete transport types.
- Input connection entrypoints should live on concrete source types.
- Use transport-specific options types when config becomes large enough to justify them.
- For tiny related contracts, prefer one file over many small files.

## VIIPER Notes

- Treat VIIPER as a direct handoff target.
- Map `MouseReport` directly to VIIPER mouse input.
- Fail on unsupported ranges rather than clamping silently.
- Keep implementation thin and explicit.
- Steam nullifier commands should ignore the owned VIIPER output device by VID/PID so the Steam path does not feed back on itself.
- Use one VIIPER session model: create one mouse device on connect and remove it on dispose.
- Mark created VIIPER devices with a fixed VID/PID pair and reclaim only those on startup.
- Enforce one active VIIPER owner with a named mutex; concurrent instances should fail fast instead of competing.

## Teensy Notes

- Teensy 4.0 is planned but not implemented yet.
- Placeholders may throw `NotImplementedException` until the transport is designed.

## Steam Input Notes

- Use Steamworks.NET as the C# binding unless there is a concrete reason to switch.
- Keep Steam API lifecycle ownership explicit; do not silently shut down Steam API if the caller owns it.
- Follow Valve's action-based Steam Input model; do not build around controller-specific assumptions.
- Keep the Steam Input testbench interactive and use the launcher executable for Steam non-Steam shortcuts so each run refreshes the testbench build.

## Raw Input Notes

- Follow Microsoft's documented Raw Input model first.
- Prefer the performance-oriented documented path over simplifying code by adding per-report allocations or extra native calls.
- In a `WM_INPUT` handler, read the current event from `lParam` with `GetRawInputData`, then use `GetRawInputBuffer` only to drain additional queued events.
- Keep raw input filtering caller-driven; do not bake Steam-specific assumptions into `VirtualMouse.RawInput`.

## Testing Rules

- Add tests for new behavior as you add it.
- Use the tests to verify work while developing, not only at the end.
- Keep tests in the solution.
- Keep tests focused on behavior and mapping, not internal structure.
- For retained CLI tools, prefer `System.CommandLine` over a hand-rolled parser.
- CLI output should be concise, aligned, and value-first; avoid prose verdicts and unexplained benchmark jargon.

## Logging Rules

- Do not write to console directly from library code.
- Do not create or manage log files from library code.
- Use `ILogger` only when logging is needed.
- Keep logging at the transport level, not the shared base interface.
- Log lifecycle events only.
- Do not log per-report hot-path traffic.

## Scripts

- `scripts/build.ps1`: build the solution
- `scripts/test.ps1`: run tests
- `scripts/steam.ps1`: publish the Steam Input testbench launcher executable
- `scripts/builld-testbench.ps1`: publish the Steam Input testbench executable

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

- Use `PhysicalMouse` as the root library name.
- Use `PhysicalMouse.<Transport>` for transport projects.
- Prefer clear names over over-general names.

## Style Preferences

- Keep things concise.
- Avoid self-referential wording like "minimal" in project-facing text.
- Use explicit `using` directives.
- Prefer a small number of coherent files over many tiny files when the types are tightly related.
- For CLI code, prefer a few coherent files grouped by command family or shared concerns.
- Do not collapse the CLI into one large file, and do not split it into many tiny files with barely any logic.
- Do not place source files under dot-prefixed folders in SDK-style projects; the default compile glob will skip them.
- Preserve the existing repo tone and conventions once established.
