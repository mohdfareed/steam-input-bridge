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
- `tests/PhysicalMouse.Tests`: tests
- `tools/PhysicalMouse.Cli`: CLI harness
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

## Current API Direction

- `IPhysicalMouse` is the base connected-session interface.
- Connection entrypoints should live on concrete transport types.
- Use transport-specific options types when config becomes large enough to justify them.
- For tiny related contracts, prefer one file over many small files.

## VIIPER Notes

- Treat VIIPER as a direct handoff target.
- Map `MouseReport` directly to VIIPER mouse input.
- Fail on unsupported ranges rather than clamping silently.
- Keep implementation thin and explicit.
- Use one VIIPER session model: create one mouse device on connect and remove it on dispose.
- Do not add caller-controlled VIIPER device reuse or cleanup policy unless explicitly requested.

## Teensy Notes

- Teensy 4.0 is planned but not implemented yet.
- Placeholders may throw `NotImplementedException` until the transport is designed.

## Testing Rules

- Add tests for new behavior as you add it.
- Use the tests to verify work while developing, not only at the end.
- Keep tests in the solution.
- Keep tests focused on behavior and mapping, not internal structure.
- For retained CLI tools, prefer `System.CommandLine` over a hand-rolled parser.

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
- `scripts/pack.ps1`: pack the solution

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
- Preserve the existing repo tone and conventions once established.
