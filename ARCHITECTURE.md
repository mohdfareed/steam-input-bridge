# Architecture

This project has two distinct responsibilities:

- **Hot path:** read input and write output with as little latency and allocation as possible.
- **Coordination:** choose profiles, guard ownership, apply system policy, and clean up owned devices.

Do not move hot-path work into coordination code. Control RPC, profile loading, logging, and discovery must stay out of per-report forwarding.

## Process Roles

### Manager

The manager is the long-lived coordinator. It owns durable configuration, active leases, system-wide policy, and cleanup.

Responsibilities:

- Load and validate configuration.
- Compute the effective profile for a client.
- Grant exclusive output leases.
- Track client heartbeat and status.
- Reclaim orphaned owned virtual devices.
- Apply system policy such as device hiding.
- Store runtime state that must survive manager restart.

The manager should not be required on the hot path for 1000 Hz input.

### Client

The client is the route runner for context-sensitive input. It owns the hot path when input is only available in that process context.

Responsibilities:

- Read contextual input sources such as Steam-routed SDL input.
- Pair Steam-routed buttons and axes with physical motion when configured.
- Apply route-local gates that must affect the next report immediately.
- Write directly to the leased output when latency requires it.
- Report heartbeat and route status to the manager.
- Release its lease and dispose its output on normal exit.

For a 1000 Hz gyro route, prefer client-local input-to-output forwarding over per-report manager IPC.

### Host Routes

Host routes are still valid for inputs that the long-lived process can read directly, such as Raw Input mouse and physical SDL gamepads. Host routes should use the same canonical input/output contracts as client routes.

### Output Transports

Output transports own connection and device lifetime. They must expose enough identity for cleanup and loopback prevention.

Rules:

- Created virtual devices must be identifiable as owned by this project.
- Startup should reclaim stale owned devices before creating new ones.
- Active output ownership must be guarded by a lease or named ownership primitive.
- Input discovery must be able to ignore owned virtual outputs.

## Configuration

Configuration is durable user intent. Runtime state is what is currently happening. Keep them separate.

### Global Settings

Global settings are defaults and infrastructure:

- VIIPER or Teensy connection settings.
- Manager IPC settings.
- Default output transport.
- Default device hiding policy.
- Default global gates such as emulation and physical motion.
- Logging and diagnostics settings.

Global settings should not contain active client leases, process ids, or temporary device ids.

### Controller Settings

Controller settings are keyed by a stable physical identity when possible:

- VID/PID.
- SDL path or GUID when stable.
- Reported name as a fallback.
- User-defined alias.

Controller settings are defaults for a physical controller:

- Whether this controller should be hidden while emulated.
- Preferred motion source.
- Preferred output type when not overridden.
- Future calibration or sensor policy, only if explicit non-transparent behavior is needed.

Do not put game-specific mappings here.

### Profiles

A profile is a reusable route recipe:

- Input mode: physical, Steam-routed, or Steam-routed with physical motion.
- Output type: mouse, Xbox 360, DS4, keyboard, or future transport.
- Output transport: VIIPER or Teensy.
- Device selection policy.
- Motion policy.
- Rumble policy.
- Device hiding policy.
- Optional Steam Input config to force while active.

Profiles are not runtime state. A profile says what should happen when activated.

### Games

A game entry binds a launch target to a profile:

- Steam app id or local executable path.
- Display name.
- Default profile id.
- Optional per-game overrides.
- Optional process or window match rules.

Game entries should not duplicate profile settings unless they intentionally override them.

### Client Instance State

Client instance state is temporary:

- Client id.
- Active profile id.
- Game process id.
- Focus state.
- Lease id.
- Created output device ids.
- Heartbeat timestamp.

This belongs in manager runtime state, not the durable profile file.

## Precedence

Effective route configuration should be computed in this order:

1. Built-in defaults.
2. Global settings.
3. Controller settings.
4. Profile settings.
5. Game overrides.
6. Client launch arguments.
7. Runtime toggles.

Runtime toggles such as emulation enabled and physical motion enabled are gates over the effective route. They should not rewrite profiles.

## Storage

Use Microsoft configuration/options APIs for durable settings. The physical storage can grow from one JSON file into multiple files without changing the app model.

Recommended layout:

- `settings.json`: global settings.
- `controllers.json`: known controller defaults.
- `profiles/*.json`: route profiles.
- `games/*.json`: game bindings.
- `state.json`: runtime state that must survive manager restart.

`state.json` is not configuration. It should be safe to delete, aside from losing cached leases/status.

## Steam Input Configs

Steam Input config forcing is profile lifecycle work, not hot-path forwarding.

Rules:

- Steam Input file parsing and control helpers stay in `src/SteamInput`.
- A profile may reference the Steam Input config it needs.
- The process that owns profile activation applies and clears forcing.
- Forcing and clearing must be paired with lifecycle cleanup.

## Adding Features

Use this placement rule:

- If it reads contextual input, put it in the client/input side.
- If it writes a device, put it in the output transport side.
- If it arbitrates ownership or policy, put it in the manager side.
- If it describes user intent, put it in configuration.
- If it describes what is currently active, put it in state.
- If it runs per report, keep it out of JSON, logging, allocation-heavy code, and general RPC.
