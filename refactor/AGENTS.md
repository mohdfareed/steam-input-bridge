# Refactor Spike

## Maintenance Rule

- Treat this file as living project memory for the refactor spike.
- Add durable instructions here when the user gives guidance that should apply throughout future maintenance of this spike.
- Keep temporary debugging notes, one-off task state, and stale implementation details out of this file.

## Current Direction

- Call the long-lived process the server, not the host.
- Keep this foundation small: CLI commands, appsettings, logging, and server/client request-response communication only.
- Organize the spike by responsibility: `Protocol`, `Client`, `Server`, and `cli`.
- Keep the server/client request interface visible in `Protocol`, separate from named-pipe plumbing.
- Keep only shared wire/request/response models in `Protocol`; keep local client state models in `Client` and server registry models in `Server`.
- Keep app-facing client/server classes readable first; move per-connection plumbing into internal helper types when it starts hiding the public contract.
- Treat `VirtualMouseClient` as an object callers instantiate, connect, call, wait, and dispose.
- Keep useful smoke checks as repeatable tests under `tests`, and expose them through `script/test.ps1`.
- Do not add profiles, routes, input devices, output devices, or session orchestration until the communication foundation is stable.
