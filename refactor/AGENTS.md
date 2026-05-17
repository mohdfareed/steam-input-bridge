# Refactor Spike

## Maintenance Rule

- Treat this file as living project memory for the refactor spike.
- Add durable instructions here when the user gives guidance that should apply throughout future maintenance of this spike.
- Keep temporary debugging notes, one-off task state, and stale implementation details out of this file.

## Current Direction

- Call the long-lived process the server, not the host.
- Keep this foundation small: CLI commands, appsettings, logging, and server/client request-response communication only.
- Organize the spike by responsibility: `Hosting`, `Settings`, and `cli`.
- Keep client, server, request/response protocol, and named-pipe lifecycle management in `Hosting`.
- Keep app-facing hosting classes readable first; move per-connection plumbing into internal helper types when it starts hiding the public contract.
- In `Hosting`, keep user-facing public types at the project root. Put internal protocol, pipe, connection, and registry plumbing under `Shared`.
- Before adding code, inspect the full pipeline and place behavior where that responsibility already belongs; do not add code to a nearby file just because it is convenient.
- If a file named for a responsibility exists, put that responsibility there. For example, client connection/open/reconnect/clear/dispose logic belongs in `ClientConnection`, not `VirtualMouseClient`.
- Do not make plumbing public to satisfy constructor or test convenience. Keep `ClientConnection` and connection liveness calls internal; expose only the app-facing `VirtualMouseClient` surface.
- Document public interfaces with concise XML docs and do not suppress missing XML documentation warnings for refactor projects.
- Section marker separator comments must be exactly 79 characters long, ending on column 79.
- Treat `VirtualMouseClient` as an object callers instantiate, connect, call, wait, and dispose.
- Keep profiles as server-side configuration loaded from the shared appsettings file; do not expose profile list/get IPC until a server workflow needs it.
- Keep app-wide settings in the `Settings` project. Profiles are a `Settings.Profiles` folder/namespace that owns only game profile options and profile lookup/reload behavior.
- Settings expose a `VirtualMouseSettings` root object. CLI composition should call Settings registration instead of binding random settings types directly.
- Keep settings models, settings service, and dependency injection registration in separate files.
- Settings reload notifications belong to the root `ApplicationSettingsService`; feature services such as profiles should observe that root service and project their own slice without exposing duplicate change events.
- Keep settings file path ownership in the root `Settings` layer. `ProfilesService` should not accept, expose, or log the settings file path.
- Profiles choose output explicitly with separate `ControllerOutput` and `MouseOutput` values. Do not reintroduce a vague combined output mode.
- Put app-owned settings under the `VirtualMouse` root in appsettings. Do not use top-level `Logging` for app settings; it is owned by Microsoft.Extensions.Logging provider configuration.
- File logging is configured from `VirtualMouse:Logging:LogFile` at startup. Do not add reloadable logging until a real workflow needs it.
- Keep useful smoke checks as repeatable tests under `tests`, and expose them through `script/test.ps1`.
- Do not add profiles, routes, input devices, output devices, or session orchestration until the communication foundation is stable.
