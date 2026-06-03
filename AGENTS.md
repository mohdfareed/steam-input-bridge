# AGENTS.md

## Purpose

This repository is Steam Input Bridge (`steam-input-bridge`). Keep work scoped
to this repository.

This file is the maintainer operating contract: how to work, communicate, and
avoid repeating past mistakes.

- User-facing usage belongs in `README.md`.
- Do not create extra ledger/review/map docs unless the user explicitly asks.

## Communication

- Be concise. Answer the actual question before giving context.
- Do not present architecture reasoning, memory, or prior assumptions as facts
  about the current codebase.
- If saying "the code does X", first read the current owning file, log, or test.
- If a remembered convention affects implementation and is not in this file or
  explicitly stated in the current thread, verify it before enforcing it.
- Ask when a decision changes architecture, ownership, lifecycle, global state,
  or long-term maintenance cost.
- If a fix would change ownership, architecture, presentation model, lifecycle,
  or add a nontrivial implementation, stop and ask before editing.

## Architecture Decisions

- Do not make architecture-scale calls implicitly.
- Code under `SteamInputBridge*` is final-product code, not scaffolding. Do not
  add placeholder architecture, temporary ownership, fake implementations, or
  "good enough for now" structure there. If a requested behavior touches
  runtime ownership, process lifetime, background tasks, settings, IPC, tray
  mode, shortcut mode, client management, server management, or command
  dispatch, implement the final ownership shape for that behavior or stop and
  ask.
- Do not add nested placeholder classes for runtime owners, command handlers
  that re-invoke the app like scripts, temporary state buckets, hidden
  environment-variable control paths, or "clean this later" architecture.
- Confirm before changing which process owns an input source, output device,
  route lifecycle, Steam visibility, controller identity, IPC path, hardware
  access, or process ownership.
- ***The app must remain anti-cheat compliant and usable in competitive games.***
  Do not propose or implement process injection, API hooking, game memory
  access, anti-cheat bypasses, or anything that could look like tampering with
  Riot/Blizzard/competitive game processes.
- Confirm before adding a new service, registry, background loop, retry loop,
  polling timer, lifecycle owner, global-state mutation, or cross-process data
  path.
- For nontrivial features, first state the intended behavior, owner, affected
  files, and tradeoff. Wait for confirmation if the change crosses major
  boundaries.
- Treat "already there, just inactive" as unproven until the live reader,
  writer, route, and integration point are found in code.

## Working Method

- Runtime edit preflight must state the files read fully, ownership boundary
  affected or not, expected deletion/simplification, and tests that protect the
  behavior.
- Before changing runtime code, understand the current path in words: input,
  output, owner, lifecycle, state transitions, side effects, cleanup, and tests.
- Before proposing or applying a bug fix, read the owner files from current
  code. Lifecycle claims require lifecycle files; routing claims require
  routing files; VIIPER claims require VIIPER files; hot-path claims require
  the report-processing path.
- For live route bugs, inspect the latest deployed settings and logs before
  editing. Name the failing owner/layer from evidence before changing code.
- Do not claim a live bug is fixed unless the changed code was built, tested,
  and deployed when the user is testing `bin`.
- If a bug report crosses responsibilities, map the current responsibility
  boundary before editing. Do not guess which layer owns the fix.
- Read the full file before editing it. In fragile areas, also read direct
  callers and callee's.
- Fragile areas include controller routing, SDL, VIIPER, client
  reconnect, process lifetime, settings/runtime boundaries, foreground
  activation, status/diagnostics, and polling loops.
- Prefer deleting or simplifying existing layers over adding new abstractions.
- Default to direct pass-through over abstraction layers unless the abstraction
  removes real complexity or matches an existing local pattern.
- Use dependency-backed Windows plumbing by default: Vanara for Win32/CoreAudio
  and NHotkey for global hotkey registration.
  Keep manual interop only for hot paths or behavior the library does not cover
  cleanly.
- Do not add broad infrastructure to make a feature possible unless that design
  has been agreed.
- Cleanup/refactor changes should reduce production complexity. If production
  LOC grows, or a new loop/state owner/registry is added, get explicit approval
  unless the user already approved that exact tradeoff.
- If a new loop, timer, registry, route owner, or global side effect is needed,
  get explicit confirmation and keep the implementation locally obvious.

## Runtime Rules

- For launched profiles, report and lifetime-track only receiver processes that
  appeared after the pre-launch receiver baseline. Pre-existing matching
  processes must not keep the client alive or claim active focus. Attach-only
  profiles report all observed receivers.
- Do not poll SDL controller identity/route snapshots on a timer. Refresh
  client controller routes from SDL add/remove/disconnect events, and send
  server route registration only when the controller set actually changes.
- Controller forwarding is Steam Controller focused. Ignore non-Steam
  controller identities at the SDL/route boundary instead of adding generic
  physical-controller support.
- Inactive client controller frames must not update stored route state. They
  may arrive while another game is focused, but must be ignored before they can
  overwrite the next active output state.
- Keep status and diagnostics reads side-effect free. Repair, reconciliation,
  Steam Input mutation, route mutation, and device lifecycle changes must happen
  through explicit lifecycle paths.
- Backend diagnostic helpers may exist when they are long-term reusable code and
  are organized under a clear responsibility. One-off probes and app-facing
  diagnostics belong in the app or tests.
- Keep controller and mouse hot paths well-optimized. Avoid unnecessary
  allocations, logging, JSON, RPC, batching, smoothing, or extra queues unless
  the user confirms the tradeoff.

## Cleanup Discipline

- Every bug fix includes cleanup over the touched responsibility, not only the
  changed lines.
- Remove dead workarounds, stale diagnostics, unused state, obsolete concept
  names, and one-off debugging code before finalizing.
- Temporary diagnostics must be deleted before final; do not leave hot-path log
  probes, route probes, or status fields as cleanup debt.
- Final responses for runtime fixes must state files deleted or simplified,
  residue searched, tests run, and known remaining issues.
- After a mistaken implementation, search the repository for leftover concept
  names and behavior. Removing the obvious call site is not enough.
- When asked for a cleanup pass, do not narrow the work to the current diff
  unless the user explicitly scopes it that way.
- Do not create cleanup/review ledger files as a substitute for cleanup. Handle
  one agreed area at a time: read the whole area, list the concrete behaviors
  its code supports, get decisions for questionable behavior, delete/edit the
  code, remove stale docs/residue, then build and test. If an item cannot be
  resolved now, ask instead of parking it in a review file.
- Keep code navigable for a human using an IDE. Prefer a small number of
  coherent files over scattered tiny wrappers or large flat dumping grounds.

## Editing Rules

- Use `apply_patch` for manual edits.
- Do not revert user changes unless explicitly asked.
- Do not edit local user settings such as
  `SteamInputBridge.App/appsettings.json` unless the user asks.
- File logging and console logging are app responsibilities. Library code should
  use `ILogger` for lifecycle/debuggable events and must not own console output
  or log files.
- Use comments for non-obvious behavior, external quirks, ownership rules, and
  routing constraints. Do not hide intent behind tiny helper methods.
- Do not add private helpers that only wrap a constructor, null check, simple
  property access, or one obvious call.
- Do not create custom app error dialogs. The app error dialog must stay on the
  native dialog path unless the user explicitly approves replacing that model.

## Verification

- Add or update tests for changed behavior.
- Do not add tests for every small implementation change. Add or update tests
  only when they protect meaningful behavior or real risk: behavior changes,
  bug-prone ownership/lifecycle paths, protocol/serialization boundaries,
  observed regressions, or production-risk code.
- Avoid duplicate coverage that exercises the same path repeatedly,
  implementation-shape tests, and ceremonial tests. For narrow UI wiring, docs,
  comments, naming, simple argument passing, or obvious plumbing already covered
  by build/compile checks, prefer no new test unless there is a concrete failure
  mode worth preserving.
- Tests that only preserve implementation shape should be deleted, rewritten,
  or moved out of the normal tier.
- Use targeted tests while developing. Run broader suites when the changed code
  crosses shared behavior or when asked.
- Do not run a full test suite for docs-only or comment-only edits unless there
  is a concrete reason.
- Do not run multiple `dotnet build` or `dotnet test` commands in parallel for
  the same configuration.
- Tests should cover behavior and mapping, not private implementation shape.
- Keep tests in useful tiers:
  - Normal tests are deterministic and local/fake-output friendly.
  - Dependency tests are opt-in and require external drivers or services.
  - Manual tests are opt-in and require prepared user action or machine state.
- Dependency/manual tests must assert meaningful behavior or report inconclusive
  when prerequisites are missing. Do not keep ceremonial tests that never help
  validate a real route.

## Style

- Keep explanations concise and concrete.
- Use explicit `using` directives.
- Use clear names instead of over-general names.
- Do not spread method, constructor, or attribute arguments over multiple lines
  unless the line would approach 120 characters or the split clearly improves
  readability.
- Keep files and folders navigable: avoid giant files, giant flat folders, and
  files so small that they only add navigation noise.
- Group tiny related models together. Split models into separate files when
  they become large enough to read better on their own.
- Do not leave near-empty migration artifact files.
- Do not place source files under dot-prefixed folders in SDK-style projects.

## Section Markers

Use section markers consistently in files that need sections.

```csharp
// MARK: Section Name
// ============================================================================
```

The full separator line, including the leading `// ` and tabs/padding, must be
exactly 79 characters wide.
