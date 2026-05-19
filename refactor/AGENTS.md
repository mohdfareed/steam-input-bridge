# Refactor Spike

## Maintenance Rule

- Treat this file as living project memory for the refactor spike.
- Add durable instructions here when the user gives guidance that should apply throughout future maintenance of this spike.
- Keep temporary debugging notes, one-off task state, and stale implementation details out of this file.

## Current Direction

- Call the long-lived process the server, not the host.
- Keep this foundation small: CLI commands, appsettings, logging, and server/client request-response communication only.
- Organize the spike by responsibility: `Runtime`, `Hosting`, `Settings`,
  `Forwarding`, `Steam`, `Inputs`, `Outputs`, and `cli`.
- Keep active-client and receiver-process ownership in the separate `Runtime`
  project. Do not fold it into Hosting.
- Keep client, server, request/response protocol, and named-pipe lifecycle management in `Hosting`.
- Organize internal server code by server responsibility:
  `Inputs` for server-owned input pumps, `Outputs` for server output
  composition/fallbacks, `Pipes` for named-pipe sessions and per-client pipe
  loops, `Runtime` for active-client orchestration, `Sessions` for connected
  client/profile state, and `Status` for server status models.
- Organize Hosting around product concepts, not implementation leftovers:
  `Server` for the long-lived process and server-side orchestration, `Client`
  for the Steam-launched/profile client lifecycle, and `Transport` or `Shared`
  only for pipe/RPC plumbing. Avoid generic names such as `Runner`, `Tools`,
  `Manager`, or `Helper` unless the file is genuinely tiny glue.
- Keep app-facing hosting classes readable first; move per-connection plumbing into internal helper types when it starts hiding the public contract.
- Keep `HostServer` focused on server lifetime and pipe acceptance.
  Connected-client state, profile run state, status, and runtime updates belong
  in `ServerSessions`; pipe RPC target code should talk to `ServerSessions`, not
  to the app-facing server object.
- In `Hosting`, keep user-facing public types at the project root. Put internal protocol, pipe, connection, and registry plumbing under `Shared`.
- Keep `ClientConnection` focused on connection lifecycle. Server API calls such as status belong on `VirtualMouseClient` and use the connection pipe internally.
- Keep server-owned status construction on server state/root server code. `ServerConnection` should dispatch status requests, not decide what status contains.
- Endpoint request mapping belongs on `HostServer` until it is large enough to deserve a dedicated router. `ServerConnection` only owns pipe connection lifecycle and delegates post-connect requests.
- Before adding code, inspect the full pipeline and place behavior where that responsibility already belongs; do not add code to a nearby file just because it is convenient.
- When moving or flattening files, update project names, assembly names,
  namespaces, scripts, solution entries, and friend assembly names so no stale
  historical responsibility name survives the move.
- Before adding new behavior, first name the product concept that owns it, then
  place the code under that concept. Do not create generic runner/helper/tool
  files as a parking lot for workflow logic.
- Treat foundation code as permanent. Do not defer organization with "clean it
  later"; if the current organization cannot absorb a feature cleanly, fix the
  organization before adding the feature.
- If a file named for a responsibility exists, put that responsibility there. For example, client connection/open/reconnect/clear/dispose logic belongs in `ClientConnection`, not `VirtualMouseClient`.
- Do not make plumbing public to satisfy constructor or test convenience. Keep `ClientConnection` and connection liveness calls internal; expose only the app-facing `VirtualMouseClient` surface.
- Document public interfaces with concise XML docs and do not suppress missing XML documentation warnings for refactor projects.
- Section marker separator comments must be exactly 79 characters long, ending on column 79.
- Treat `VirtualMouseClient` as an object callers instantiate, connect, call, wait, and dispose.
- Keep profiles as server-side configuration loaded from the shared appsettings file; do not expose profile list/get IPC until a server workflow needs it.
- Keep app-wide settings in the `Settings` project. Profiles are a `Settings.Profiles` folder/namespace that owns only game profile options and profile lookup/reload behavior.
- Settings expose a `VirtualMouseSettings` root object. CLI composition should call Settings registration instead of binding random settings types directly.
- Keep settings models, settings service, and dependency injection registration in separate files.
- Keep settings validation minimal and centralized in `Settings`; feature services should not duplicate root validation.
- Settings reload notifications belong to the root `ApplicationSettingsService`; feature services such as profiles should observe that root service and project their own slice without exposing duplicate change events.
- Keep settings file path ownership in the root `Settings` layer. `ProfilesService` should not accept, expose, or log the settings file path.
- Profiles choose output explicitly with separate `ControllerOutput` and `MouseOutput` values. Do not reintroduce a vague combined output mode.
- Resolve raw game profile settings through a simple helper, not a DI service. The resolver owns runtime defaults such as title, working directory, receiver process names, and environment-expanded paths.
- Put app-owned settings under the `VirtualMouse` root in appsettings. Do not use top-level `Logging` for app settings; it is owned by Microsoft.Extensions.Logging provider configuration.
- Keep `refactor/cli/appsettings.json` as a practical starter config: small,
  usable, and not just empty defaults. Keep `refactor/cli/appsettings.example.json`
  as the compact complete reference with every supported field assigned a
  meaningful value that demonstrates the setting.
- File logging is configured from `VirtualMouse:Logging:LogFile` at startup. Do not add reloadable logging until a real workflow needs it.
- Keep useful smoke checks as repeatable tests under `tests`, and expose them through `script/test.ps1`.
- Do not add profiles, routes, input devices, output devices, or session orchestration until the communication foundation is stable.
- Status commands should use the normal client-to-running-server pipe path, not direct access to an in-process server object.
- Use `StreamJsonRpc` for Hosting server/client communication. Do not
  reintroduce manual request envelopes, method-name dispatch helpers, or
  custom request/response pipe protocols.
- Public wording should say active client, not active run. Do not add run ids
  until one connected client can actually manage multiple games.
- Receiver process lists are ANY-match lists. The client reports all matching
  receiver pids; Runtime claims pids first-client-wins and may let one client
  own multiple pids.
- Keep OS process observation and cleanup outside `Runtime`; Runtime is only
  active-client and receiver-pid ownership state.
- Keep input/output routing in `Forwarding`, not `Hosting` or `Runtime`.
  `Forwarding` owns logical controller slots, active-client output gates, output
  device lifetime, Steam-preferred feature merge, and feedback fallback.
- Keep canonical hot-path report models and source-to-output mappers in
  `Forwarding`. Adapter projects such as `Inputs/Sdl` and `Outputs/Viiper`
  should translate between device libraries and those canonical models only.
- Model each controller as one logical slot with a Steam endpoint and a physical
  endpoint. Prefer Steam Input for every readable/writable feature, and use the
  physical endpoint only when the Steam endpoint is unavailable or does not
  support that feature.
- Keep future controller features such as LEDs, touchpads, adaptive triggers,
  and similar device-specific behavior as feature groups on the logical
  controller slot. Do not create per-feature route/session abstractions unless a
  real workflow proves the single-slot model insufficient.
- Runtime controller toggles should gate feature groups, not attach or detach
  the whole physical controller endpoint. Physical motion is the first such
  gate; the physical endpoint remains available for matching, feedback fallback,
  and other supported feature groups.
- Controller output devices stay connected while any attached client endpoint
  wants that output kind. Active-client state gates report forwarding, not
  virtual-device lifetime. Dispose the output only when no attached clients need
  it or output is explicitly disabled.
- SDL gamepad indices are transport-local and can change between discovery
  calls. Use them only for the current client controller pipe snapshot; clear
  stale client controller endpoints whenever the client re-registers
  controllers.
- Client-launched game processes should be attached to a Windows kill-on-close
  job when possible. Keep explicit receiver cleanup too, because launchers may
  not keep every receiver in the process tree.
- Keep per-report controller traffic on the `Forwarding` fixed binary pipe
  model. Do not send hot-path controller reports through JSON-RPC.
- `Hosting` owns controller pipe lifetime for connected clients, but pipe frames
  must flow into `Forwarding.ControllerBroker`; do not put merge, output, or
  feature fallback policy in Hosting.
- Output feedback sent back to client controller streams must not block broker
  output callbacks. Use pipe-owned asynchronous delivery or an explicit queue.
- Server-side active-client orchestration lives in `Hosting/Server`. It observes
  the foreground process id, updates `ActiveClientRegistry`, and dispatches
  active-client changes through simple callbacks. Do not add tiny reaction
  wrapper types until there are multiple real reactions with different behavior.
- Steam Input forcing is a server-side active-client reaction. On active-client
  changes, clear the forced app id first, then apply the new active client's
  Steam app id when present.
- Keep refactor Steam integration in `refactor/src/Steam`. Do not reference
  legacy `src/SteamInput` from refactor projects.
- Keep refactor input/output adapters in `refactor/src/Inputs` and
  `refactor/src/Outputs`. Do not reference legacy adapter projects from
  refactor projects.
- Keep Teensy wired as an output adapter placeholder until the transport is
  designed. It may expose selectable output options, but send operations should
  fail explicitly with `NotImplementedException`.
- Keep adapter-specific loopback policy out of `Inputs/Sdl`. SDL discovery may
  accept a caller filter, but Hosting owns the app-specific decision to ignore
  VIIPER-created virtual controllers.
- Server-owned input pumps and client controller streams should survive SDL
  disconnects by disposing stale sources, retrying discovery, and updating
  status instead of ending the whole server or client run.
- Keep Steam ROM Manager export in the refactor Steam project; CLI commands only
  orchestrate it from appsettings and print the result.
- Keep `SteamInputClient`'s public API narrow: `DesktopConfigAppId`,
  `ListGames`, `ResolveAppIdFromEnvironment`, `ForceConfigAsync(uint?)`, and
  `OpenControllerConfigAsync(uint)`. CLI commands expose desktop and clearing
  by passing `DesktopConfigAppId` or null rather than adding more SteamInput
  methods.
