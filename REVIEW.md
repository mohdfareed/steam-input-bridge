# Bundled Feature Review

Global cleanup:
- Fix namespaces and rename files/folders/types so the codebase consistently matches the intended project structure.

Scope: `SteamInputBridge` and `SteamInputBridge.App`; tests excluded.

Order:
1. `SteamInputBridge/Forwarding/Controller`
2. `SteamInputBridge/Forwarding/Mouse`
3. `SteamInputBridge/Forwarding`
4. `SteamInputBridge/Hosting`
5. `SteamInputBridge/Inputs`
6. `SteamInputBridge/Outputs`
7. `SteamInputBridge/Runtime`
8. `SteamInputBridge/Settings`
9. `SteamInputBridge/Shortcuts`
10. `SteamInputBridge/Steam`
11. `SteamInputBridge.App`

Current: `SteamInputBridge/Hosting`

Decisions only. Open questions stay in chat until settled.

Review rules:
- When splitting files, group related files under balanced folders. Avoid folders with 10+ loose files and avoid folders with only 1-2 files unless they are placeholders.

Forwarding findings:
- Stock HidHide cannot express the target visibility model because its app list applies to all hidden devices together, not per device.
- The useful model needs per-device per-process visibility: hide VIIPER from Steam, hide physical and Steam Input controllers from the game, and show route-specific VIIPER devices only to the intended receiver.
- Without that visibility routing, forwarding remains leak-prone: Steam may consume VIIPER devices, games may see duplicate physical/Steam/VIIPER inputs, and simultaneous clients stay fragile.
- A Steam-launched client normal SDL scan sees Steam-routed controllers only. Clearing SDL/Steam filters in that same forwarding client did not expose real physical controllers; it exposed Steam virtual gamepads and destabilized SDL route identity, so physical companion discovery needs a separate approach.
- GameInput is not a simple same-client physical companion path: local non-Steam diagnostics can see a physical HID gamepad, but a Steam-launched diagnostic saw `devices=0`.

Big-picture cleanup direction:
- Keep server/client architecture for the Steam-visible client to VIIPER-owned server output path.
- Make the active path small: `client run <profile>` -> client-visible SDL controllers -> controller pipe -> server-owned VIIPER output.
- Keep foreground active-run selection, Steam config forcing, and shortcuts because they are part of the useful daily route.
- Keep useful but unstable features only if they are isolated and unwired from the default route. Do not leave commented-out integration code.
- Delete abandoned ideas. Keep future-use code only when it has one obvious integration point and a clear owner.
- Physical companion reading, HidHide routing, and physical controller matching stay outside the active path until controller identity/routing is stable.
- Cleanup goal is not a production architecture. It is a small personal tool with stable core routing and explicit opt-in extension points.

Near-term cleanup plan:
1. Stabilize the controller route identity path first.
2. Isolate client-side SDL scan/open/route planning from pipe streaming.
3. Make server-side controller pipe handling only translate registered client routes into broker updates.
4. Unwire physical controller pump, HidHide policy, and physical companion matching from the default run path behind explicit integration points.
5. Then clean up `ServerService`, `ServerActiveClientLoop`, and status/diagnostics around the smaller active route.

| Area                            | File                            | Bundled responsibilities                                                                                                                | Decision      | Action                                                                                         | Notes                                                                                                                                                         |
| ------------------------------- | ------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------- | ------------- | ---------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `Forwarding.Controller`         | `ControllerContracts.cs`        | Controller public interfaces and canonical controller models                                                                            | Keep          | None                                                                                           | Acceptable contracts/model bundle                                                                                                                             |
| `Forwarding.Controller`         | `ControllerReports.cs`          | Xbox 360 report and rumble models                                                                                                       | Keep          | None                                                                                           | One output report shape                                                                                                                                       |
| `Forwarding.Controller`         | `ControllerOutputMapping.cs`    | Xbox 360 mapping to/from canonical controller model                                                                                     | Keep          | None for now                                                                                   | Split by output type only when DS4 mapping lands                                                                                                              |
| `Forwarding.Controller`         | `ControllerBroker.cs`           | Client registry, active-client gate, output lifecycle, dispatch, status                                                                 | Review deeper | Decide split after broker/slot review                                                          | Main unstable controller orchestrator                                                                                                                         |
| `Forwarding.Controller`         | `ControllerSlot.cs`             | Endpoint state, merge policy, output connection, feedback, cleanup                                                                      | Split         | Extract feedback and/or output-lifetime logic                                                  | Too much behavior in one route-slot file                                                                                                                      |
| `Forwarding.Mouse`              | `MouseReports.cs`               | Mouse reports/input/output contracts, but broker-adjacent models live elsewhere                                                         | Split         | Move `MouseOutput`, `IMouseOutputFactory`, and `MouseBrokerStatus` here or rename to contracts | Keep mouse definitions in one place                                                                                                                           |
| `Forwarding.Mouse`              | `MouseBroker.cs`                | Client registry, active-client gate, output lifecycle, pointer gate, dispatch, status                                                   | Review deeper | Review after model move                                                                        | Smaller mirror of controller broker                                                                                                                           |
| `Forwarding`                    | `ControllerInputPipe.cs`        | Bidirectional controller pipe message models and reader/writer                                                                          | Keep          | Rename to `ControllerPipe.cs`                                                                  | Current name hides feedback direction                                                                                                                         |
| `Forwarding`                    | `ControllerPipeFrame.cs`        | Fixed-size controller pipe binary protocol codec                                                                                        | Keep          | Review size/readability later                                                                  | Coherent but gigantic                                                                                                                                         |
| `HidHide`                       | folder                          | CLI runner, allowlist, device catalog, scope, snapshot, firewall, status, logging spread across many files                              | Review deeper | Collapse toward one simple service plus models                                                 | Current design is over-engineered for disabled-by-default HidHide support                                                                                     |
| `Inputs.RawInput`               | folder                          | Window/message loop, high-frequency raw input buffering, report conversion, device metadata, Steam mouse filtering                      | Review deeper | Simplify boundaries and namespace shape                                                        | Metadata-less Raw Input events are the Steam Input mouse stream and should be filtered here unless a dedicated Steam mouse source exists                      |
| `Inputs.Sdl`                    | `SdlControllers.cs`             | SDL controller models, discovery, open helpers, source filters, route identity helpers                                                  | Split         | Keep discovery/models here; move host/route identity helpers out                               | `GetPhysicalControllerId`, `OpenClientControllers`, and `OpenPhysicalControllers` leak routing policy into SDL                                                |
| `Inputs.Sdl`                    | `SdlControllerMatcher.cs`       | Steam-routed to physical-controller matching policy                                                                                     | Move          | Move toward Hosting route/companion policy                                                     | Keep temporary Steam Controller fallback, but label it as explicit FIXME because multiple Steam Controllers cannot be paired safely with current SDL identity |
| `Inputs.Sdl`                    | `SdlGamepadSource.cs`           | One connected SDL controller handle, sensors, rumble feedback, disconnect state, motion toggle                                          | Keep          | Minor cleanup only                                                                             | Concrete SDL source object is acceptable; do not add a controller input interface until a second real controller source exists                                |
| `Inputs.Sdl`                    | `SdlGamepadEventLoop.cs`        | SDL event loop, add/disconnect dispatch, SDL runtime lease, disconnect exception                                                        | Split         | Move runtime lease out                                                                         | Event-loop behavior is fine; runtime lease does not belong in the event-loop file                                                                             |
| `Inputs.Sdl`                    | `SdlGamepadStateReader.cs`      | SDL state to controller-state mapping                                                                                                   | Keep          | None                                                                                           | Straight mapping file                                                                                                                                         |
| `Outputs.Viiper`                | `ViiperOutput.cs`               | Options, owned-device detection, output factory, reclaim-all                                                                            | Split         | Move related pieces under a balanced folder, not loose root files                              | Repo-wide rule: avoid folders with 10 loose files, but also avoid one-file folders except placeholders                                                        |
| `Outputs.Viiper`                | `ViiperMouseOutput.cs`          | Mouse output definition, loopback filter, mouse report mapping, connect/reclaim                                                         | Keep          | None                                                                                           | Coherent                                                                                                                                                      |
| `Outputs.Viiper`                | `ViiperXbox360Output.cs`        | Xbox 360 output definition, report mapping, rumble read, connect/reclaim                                                                | Keep          | None for now                                                                                   | Split by output type remains correct when DS4 lands                                                                                                           |
| `Outputs.Viiper.Shared`         | `ViiperOutputConnector.cs`      | VIIPER bus/device creation, stream connect, reclaim, failed-create cleanup                                                              | Keep          | Rename/place better and review lifecycle stability                                             | Important lifecycle code, dense but not feature bloat                                                                                                         |
| `Outputs.Viiper.Shared`         | `ViiperOutputDevice.cs`         | Created device ownership, stream, feedback subscription, disconnect hook, removal/disposal                                              | Keep          | Rename/place better and review lifecycle stability                                             | Main connect/disconnect stability surface                                                                                                                     |
| `Outputs.Viiper.Shared`         | `ViiperOutputShared.cs`         | Device definition model and VIIPER lifecycle logging                                                                                    | Split         | Rename to clearer model/log ownership                                                          | `Shared` is too vague                                                                                                                                         |
| `Runtime`                       | `ActiveClientRegistry.cs`       | Client registration, receiver-process claims, foreground active-client selection, status projection                                     | Review deeper | Decide whether status projection/claim priority belong in the registry                         | Core state shape is useful; review is about boundaries, not deleting it                                                                                       |
| `Runtime`                       | `RuntimeModels.cs`              | Runtime status records and internal client state model                                                                                  | Keep          | None                                                                                           | Coherent small model file                                                                                                                                     |
| `Runtime`                       | `GameProcessHost.cs`            | Process launch, process-tree ownership, receiver discovery, executable path lookup, kill helpers                                        | Split         | Separate launch/discovery/ownership from process-kill policy                                   | Current file mixes safe utilities with destructive behavior                                                                                                   |
| `Runtime`                       | `WindowsProcessJob.cs`          | Windows Job Object wrapper for owned launched process trees                                                                             | Keep          | None                                                                                           | Coherent platform boundary                                                                                                                                    |
| `Settings`                      | `ApplicationSettingsService.cs` | Settings DI registration and reloadable settings snapshot service                                                                       | Keep          | None                                                                                           | Coherent                                                                                                                                                      |
| `Settings`                      | `SteamInputBridgeSettings.cs`   | Root settings plus feature settings models for logging, VIIPER, Steam, HidHide, shortcuts, and profiles                                 | Keep          | None for now                                                                                   | Central settings model file is easier to read                                                                                                                 |
| `Settings`                      | `SettingsValidation.cs`         | Validation for VIIPER, HidHide, shortcuts, and profiles                                                                                 | Keep          | None                                                                                           | One validation entrypoint is useful                                                                                                                           |
| `Settings`                      | `FileLogging.cs`                | File logger provider, logger, scope, and DI extension                                                                                   | Move          | Move to project root                                                                           | Logging plumbing is not really settings                                                                                                                       |
| `Settings.Profiles`             | `GameProfiles.cs`               | Profile models and internal profile snapshot                                                                                            | Keep          | None                                                                                           | Acceptable bundle                                                                                                                                             |
| `Settings.Profiles`             | `ProfileResolver.cs`            | Raw profile to runtime-ready profile resolver                                                                                           | Keep          | None                                                                                           | Coherent                                                                                                                                                      |
| `Settings.Profiles`             | `ProfilesService.cs`            | Profile lookup/cache, reload handling, and DI registration                                                                              | Keep          | Rename `ProfilesServices`                                                                      | Service name is awkward                                                                                                                                       |
| `Shortcuts`                     | `KeyboardShortcuts.cs`          | Public shortcut models/contracts, shortcut string parser, Windows global hotkey listener/session, Win32 interop                         | Split         | Split into `ShortcutModels.cs`, `ShortcutParser.cs`, and `GlobalKeyboardShortcutListener.cs`   | Functionally useful, but too packed into one file                                                                                                             |
| `Steam`                         | `SteamInputClient.cs`           | Steam game model, app-id env detection, game listing entrypoint, force config URL, open controller config URL                           | Split         | Move public Steam game models out                                                              | Behavior is coherent; model split improves readability                                                                                                        |
| `Steam`                         | `SteamRomManagerExport.cs`      | Export configured profiles to Steam ROM Manager JSON                                                                                    | Keep          | None                                                                                           | Separate feature, already isolated                                                                                                                            |
| `Steam.GameCatalog`             | `SteamGameCatalog.cs`           | Steam library folder discovery, Steam app manifests, non-Steam shortcuts, shortcut app-id parsing                                       | Keep          | Rename local variables like `_id`, `_dir`, `_exe` during cleanup                               | Coherent enough                                                                                                                                               |
| `Steam.GameCatalog`             | `SteamKeyValue.cs`              | Tiny Steam key-value wrapper model and `ValveKeyValue` adapter                                                                          | Keep          | None                                                                                           | Fine                                                                                                                                                          |
| `Steam.GameCatalog`             | `SteamLocator.cs`               | Steam install path and active user lookup                                                                                               | Keep          | None                                                                                           | Coherent                                                                                                                                                      |
| `SteamInputBridge.App.Cli`      | `AppSetup.cs`                   | Host, config, settings, service registration, and logging setup                                                                         | Move          | Move shared app setup to app root; leave CLI/tray-specific logging choices at mode layer       | Duplicates tray setup                                                                                                                                         |
| `SteamInputBridge.App.Cli`      | `CliMode.cs`                    | CLI root command composition                                                                                                            | Keep          | None                                                                                           | Coherent                                                                                                                                                      |
| `SteamInputBridge.App.Cli`      | `Commands.cs`                   | Client/server command wiring and handlers                                                                                               | Keep          | None                                                                                           | Folder context makes the generic file name acceptable                                                                                                         |
| `SteamInputBridge.App.Cli`      | `ServerStatusCommand.cs`        | Server status command plus large text/JSON status formatting                                                                            | Review deeper | Simplify output or split formatter if detail stays                                             | Useful, but drifting into diagnostic dump                                                                                                                     |
| `SteamInputBridge.App.Cli`      | `SteamCommands.cs`              | Steam list, force, clear, open-config, status, and SRM export commands                                                                  | Keep          | None for now                                                                                   | Split SRM export only if the file grows                                                                                                                       |
| `SteamInputBridge.App.Shortcut` | `ShortcutMode.cs`               | Steam shortcut argument parsing, client app setup, profile run                                                                          | Merge         | Fold into CLI/client-run path and remove separate mode/folder                                  | Same behavior as `client run`; separate setup/parser is unnecessary                                                                                           |
| `SteamInputBridge.App.Tray`     | `AppContext.cs`                 | Server lifetime, tray icon/window, refresh timer, status cache, menu callbacks, restart, stop client, export SRM, log path/icon loading | Split         | Split main tray orchestration into clearer responsibilities                                    | Main tray file does too much                                                                                                                                  |
| `SteamInputBridge.App.Tray`     | `AppMenu.cs`                    | Full tray menu tree, diagnostics menus, client menu, HidHide display, open settings/logs                                                | Split         | Split menu sections inside `Tray` when cleanup starts                                          | Useful, but too much menu-building in one file                                                                                                                |
| `SteamInputBridge.App.Tray`     | `AppSetup.cs`                   | Tray host, config, settings, service registration, and logging setup                                                                    | Move          | Merge with shared app setup                                                                    | Duplicates CLI/shortcut setup                                                                                                                                 |
| `SteamInputBridge.App.Tray`     | `AppText.cs`                    | Tray display text and value formatting                                                                                                  | Keep          | None                                                                                           | Coherent                                                                                                                                                      |
| `SteamInputBridge.App.Tray`     | `NativeMenu.cs`                 | Native menu item model and Win32 menu rendering                                                                                         | Keep          | None                                                                                           | Related enough                                                                                                                                                |
| `SteamInputBridge.App.Tray`     | `SrmExportAction.cs`            | Tray SRM export action and result model                                                                                                 | Review deeper | Share export path/content writing with CLI                                                     | Duplicates CLI export logic                                                                                                                                   |
| `SteamInputBridge.App.Tray`     | `StartupRegistration.cs`        | Windows startup registry helper                                                                                                         | Keep          | None                                                                                           | Coherent                                                                                                                                                      |
| `SteamInputBridge.App.Tray`     | `TrayMode.cs`                   | WPF app bootstrap and tray context lifetime                                                                                             | Keep          | None                                                                                           | Coherent                                                                                                                                                      |
| `SteamInputBridge.App.Tray`     | `WindowsThemeSupport.cs`        | Windows dark-menu/theme support                                                                                                         | Keep          | None                                                                                           | Coherent platform boundary                                                                                                                                    |

## Real Work Tasks

Filtered to actual work only; keep/no-op rows are intentionally omitted.

- Fix namespaces and rename files/folders/types so the codebase consistently matches the intended project structure.
- `Forwarding.Controller/ControllerBroker.cs`: review/split the main controller orchestrator.
- `Forwarding.Controller/ControllerSlot.cs`: split endpoint state, output lifetime, feedback, cleanup.
- `Forwarding.Mouse/MouseReports.cs`: move mouse broker-adjacent models here or rename the file to match contracts.
- `Forwarding.Mouse/MouseBroker.cs`: review/split after mouse model cleanup.
- `Forwarding/ControllerInputPipe.cs`: rename to `ControllerPipe.cs`.
- `Forwarding/ControllerPipeFrame.cs`: review size/readability.
- `HidHide`: collapse toward one simple service plus models.
- `Inputs.RawInput`: simplify boundaries and namespace shape.
- `Inputs.Sdl/SdlControllers.cs`: keep discovery/models; move host/route identity helpers out.
- `Inputs.Sdl/SdlControllerMatcher.cs`: move toward Hosting route/companion policy.
- `Inputs.Sdl/SdlGamepadEventLoop.cs`: move SDL runtime lease out.
- `Outputs.Viiper/ViiperOutput.cs`: split options, owned-device detection, output factory, reclaim-all.
- `Outputs.Viiper.Shared/ViiperOutputConnector.cs`: rename/place better and review lifecycle stability.
- `Outputs.Viiper.Shared/ViiperOutputDevice.cs`: rename/place better and review lifecycle stability.
- `Outputs.Viiper.Shared/ViiperOutputShared.cs`: split/rename device definition and logging pieces.
- `Runtime/ActiveClientRegistry.cs`: review status projection and claim-priority boundaries.
- `Runtime/GameProcessHost.cs`: split launch/discovery/ownership from process-kill policy.
- `Settings/FileLogging.cs`: move to project root.
- `Settings.Profiles/ProfilesService.cs`: rename `ProfilesServices`.
- `Shortcuts/KeyboardShortcuts.cs`: split models, parser, and global hotkey listener.
- `Steam/SteamInputClient.cs`: move public Steam game models out.
- `Steam.GameCatalog/SteamGameCatalog.cs`: rename unclear locals such as `_id`, `_dir`, `_exe`.
- `SteamInputBridge.App.Cli/AppSetup.cs`: move shared app setup to app root.
- `SteamInputBridge.App.Cli/ServerStatusCommand.cs`: simplify output or split formatter.
- `SteamInputBridge.App.Shortcut/ShortcutMode.cs`: merge into CLI/client-run path and remove separate mode/folder.
- `SteamInputBridge.App.Tray/AppContext.cs`: split tray orchestration.
- `SteamInputBridge.App.Tray/AppMenu.cs`: split menu sections inside `Tray`.
- `SteamInputBridge.App.Tray/AppSetup.cs`: merge with shared app setup.
- `SteamInputBridge.App.Tray/SrmExportAction.cs`: share export path/content writing with CLI.
- `Hosting/HostingLog.cs`: review size/scannability.
- `Hosting/SdlControllerFilters.cs`: move/rename as route/filter policy.
- `Hosting/Client/GameClient.cs`: split profile run, launch/attach, receiver polling, reconnect restore, process cleanup, controller streams.
- `Hosting/Client/ClientControllerStreams.cs`: split SDL scan/open, route registration, controller pipe writes, feedback reads, reconnect/retry.
- `Hosting/Client/ClientControllerRoutePlanner.cs`: review identity policy.
- `Hosting/Server/ServerService.cs`: split server lifetime, pumps, pipes, HidHide access, labels, cleanup, forwarding disposal.
- `Hosting/Server/ServerActiveClientLoop.cs`: split foreground polling, active client changes, Steam forcing, HidHide application, statuses.
- `Hosting/Server/ServerSessions.cs`: review client registry, run lifecycle, broker registration, pipe registration, status assembly, stop-client behavior.
- `Hosting/Server/ServerShortcutService.cs`: split or move shortcut behavior closer to shortcuts.
- `Hosting/Server/ServerNoopOutput.cs`: rename/move to clearer defaults/testing file.
- `Hosting/Server/Inputs/PhysicalControllerPump.cs`: review after physical companion route-local cleanup.
- `Hosting/Server/Pipes/ClientControllerPipe.cs`: review with controller routing cleanup.

## Immediate Cleanup

This is the smaller bounded cleanup phase. It should not redesign controller
routing, active-run policy, foreground selection, or forwarding behavior.
Every item here maps to one or more `Real Work Tasks` above.

No remaining immediate cleanup items.

Completed status:
- Completed the bounded cleanup list: Steam models, shortcut split, settings/file logging move, shared app setup, shortcut mode merge, SRM export sharing, CLI status formatter split, tray context/menu split, controller pipe rename/readability, mouse model move, Raw Input boundary naming, SDL runtime lease split, VIIPER split/rename, runtime kill split, HidHide collapse, server default output rename, and shortcut-service binding split.
- Settings shape rule: keep small settings records/enums centrally navigable from the root settings model; do not scatter settings entries across feature folders unless there is a single obvious F12 path.
- Visibility pass: implementation-only helpers from the cleanup are internal; public surface remains for settings/status/source/output API types.
- Test isolation added: server/client tests use unique internal pipe names so they do not connect to a running production server.
- Verified with `dotnet build SteamInputBridge.slnx` and `dotnet test SteamInputBridge.slnx`.

## Final Cleanup

This is the major Hosting/forwarding foundation cleanup. Start it only after the
immediate cleanup builds and tests pass. Every item here maps to one or more
remaining `Real Work Tasks` above.

1. Shrink the default active route to: `client run <profile>` -> client-visible SDL controllers -> controller pipe -> server-owned VIIPER output.
2. `Runtime/ActiveClientRegistry.cs`: review whether status projection and first-observer claim priority belong in the registry.
3. `Inputs.Sdl/SdlControllers.cs`: keep discovery/models there and move host/route identity helpers out.
4. `Inputs.Sdl/SdlControllerMatcher.cs`: move Steam-routed to physical-controller matching toward Hosting route/companion policy.
5. `Hosting/SdlControllerFilters.cs`: move/rename as route/filter policy.
6. Rework controller route identity so one owner decides route id, label, features, and optional physical device id.
7. Isolate physical companion reading, HidHide policy, and physical controller matching behind explicit integration points, unwired from the default route.
8. `Forwarding.Controller/ControllerBroker.cs`: review/split the main controller orchestrator.
9. `Forwarding.Controller/ControllerSlot.cs`: split endpoint state, output lifetime, feedback, and cleanup.
10. `Forwarding.Mouse/MouseBroker.cs`: review/split after mouse model cleanup.
11. `Hosting/Client/GameClient.cs`: split profile run, launch/attach, receiver polling, reconnect restore, process cleanup, and controller streams.
12. `Hosting/Client/ClientControllerStreams.cs`: split SDL scan/open, route registration, controller pipe writes, feedback reads, and reconnect/retry.
13. `Hosting/Client/ClientControllerRoutePlanner.cs`: review identity policy after the route owner is defined.
14. `Hosting/Server/ServerService.cs`: split server lifetime, pumps, pipes, HidHide access, device labels, cleanup, and forwarding disposal.
15. `Hosting/Server/ServerActiveClientLoop.cs`: split foreground polling, active-client changes, Steam forcing, HidHide application, and statuses.
16. `Hosting/Server/ServerSessions.cs`: review client registry, run lifecycle, broker registration, pipe registration, status assembly, and stop-client behavior.
17. `Hosting/Server/Inputs/PhysicalControllerPump.cs`: review after physical companion behavior is route-local and default-unwired.
18. `Hosting/Server/Pipes/ClientControllerPipe.cs`: review with controller routing cleanup.
19. `Hosting/HostingLog.cs`: review size/scannability after the Hosting split so log events follow the new owners.

Execution phases:

1. Route boundary and identity
   - Lock the default route to client-visible SDL controllers -> controller pipe -> server-owned VIIPER output.
   - Move SDL route policy out of `Inputs.Sdl`: physical id helpers, client/physical open helpers, `SdlControllerMatcher`, and `SdlControllerFilters`.
   - Create one route-owner boundary that decides route id, label, feature flags, and optional physical device id.
   - Keep physical companion reading, HidHide policy, and physical matching as explicit extension points, unwired from the default run path.
   - Progress: SDL route policy now lives in `Hosting/SdlControllerRoutePolicy.cs`; `Inputs.Sdl` only handles SDL discovery/opening/source models.
   - Progress: route id, label, and physical device id are produced through one policy identity call before client controller registrations.
   - Progress: `ServerService` no longer starts `PhysicalControllerPump` in the default route.

2. Forwarding internals
   - Split `ControllerBroker` and `ControllerSlot` around client/route registry, active-client gating, output lifetime, feedback routing, and cleanup.
   - Keep rumble route-local and avoid any global physical-controller slot registry.
   - Review `MouseBroker` after the controller split and keep only structure that is actually useful for mouse forwarding.
   - Progress: controller output lifetime is isolated in `ControllerOutputConnection`.
   - Progress: controller endpoint/feedback state moved out of the main slot file; route-local feedback is in `ControllerSlot.Feedback.cs`.
   - Progress: broker output/gating/send decisions moved to `ControllerBroker.Outputs.cs`; the main broker file keeps client/slot registration and public control surface.

3. Client runtime
   - Split `GameClient`, `ClientControllerStreams`, and `ClientControllerRoutePlanner`.
   - Separate profile run setup, launch/attach, receiver polling, SDL scan/open, route registration, controller pipe writes, feedback reads, reconnect/retry, and cleanup.
   - Make controller stream failures understandable without reading process launch code.
   - Progress: `GameClient` now owns run orchestration only; process launch/stop logic lives in `ClientGameProcessManager`, receiver polling/logging lives in `ClientReceiverProcessMonitor`, and mutable run state lives in `ClientRunState`.
   - Progress: controller streaming now has separate controller-route owners under `Hosting/Client/Run/Controllers`: pipe I/O, source registry, SDL scan/registration, route planning, and route diagnostic text.
   - Progress: `ClientControllerRoutePlanner` no longer formats diagnostics or owns route model declarations.
   - Progress: client runtime folders are balanced: `Run` contains lifecycle files and `Run/Controllers` contains controller-stream files.

4. Server runtime
   - Split `ActiveClientRegistry`, `ServerService`, `ServerActiveClientLoop`, and `ServerSessions`.
   - Separate server lifetime, connected clients, run lifecycle, foreground selection, Steam forcing, HidHide application, pumps, pipes, status, and cleanup.
   - Keep active-run side effects in one loop owner, not in registries or pipe code.
   - Progress: `ServerService` moved under `Hosting/Server/Orchestration/Lifetime`; connection tracking and HidHide device/app-access resolution are separate lifetime helpers.
   - Progress: `ServerActiveClientLoop` moved under `Hosting/Server/Orchestration/Active`; Steam Input forcing, HidHide scope application/status, and foreground-process lookup are separate active-route helpers.
   - Progress: `ServerSessions` is split into client lifecycle, route/profile registration, and status assembly files.
   - Progress: `ActiveClientRegistry` keeps mutation/claim state in the main file and status projection in `ActiveClientRegistry.Status.cs`.
   - Progress: the disabled physical controller pump was deleted from the default server route; diagnostics still report the physical pump as not running.
   - Progress: the host-owned mouse input pump moved into server lifetime orchestration so `Hosting/Server/Inputs` is gone.

5. Optional components and final pass
   - Review `ClientControllerPipe` once route identity is stable; it should only do pipe I/O and route dispatch.
   - Review `PhysicalControllerPump`; delete it if no explicit extension point still needs it.
   - Review `HostingLog` after owners are split so events follow the new structure and stay lifecycle-only.
   - Progress: `ClientControllerPipe` is split into pipe lifecycle/input dispatch, registered controller state, and feedback writing.
   - Progress: `PhysicalControllerPump` was deleted because no explicit extension point wires it into the active route.
   - Progress: `HostingLog` is split by owner: connection, inputs, active-route side effects, client-run lifecycle, and shortcuts.
   - Progress: folder audit passes the repo rule; no source folder has 10+ flat C# files.
