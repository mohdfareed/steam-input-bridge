# Quirks

This file tracks non-obvious behavior that exists because Steam Input, SDL,
Windows input visibility, VIIPER, or the routing model behave in surprising
ways.

Use this as the first place to check before deleting, simplifying, or moving
controller routing code.

## Rules

- Add an entry when a fix depends on observed external behavior, not obvious
  local code.
- Each entry should say what happens, why the code exists, where it lives, and
  how it is covered.
- Keep implementation details concise. This is an index, not a second codebase.

## Steam Input And SDL

| Quirk | Why It Matters | Owner | Coverage |
| --- | --- | --- | --- |
| Steam-launched clients can only see controllers Steam exposes to that process. | Do not assume the client can read a Steam-hidden physical controller. Physical fallback needs a separate confirmed reader path. | `Hosting/Client/Run/Controllers` | `AGENTS.md` |
| Do not clear Steam-provided SDL hiding/filter flags in the default forwarding client. | It destabilized controller identity and exposed Steam virtual gamepads rather than real physical companions. | `Inputs/Sdl`, `Hosting/SdlControllerRoutePolicy.cs` | `AGENTS.md`, `REVIEW.md` |
| SDL Steam-routed `XInput#N` paths are not physical identity. | `XInput#N` changes during Steam Input rebuilds and cannot be trusted as a route id. | `Hosting/SdlControllerRoutePolicy.cs` | `SdlControllerRoutePolicyTests` |
| SDL `Physical` devices reported as Valve `28de:11ff` with `XInput#N` paths are Steam virtual XInput fallback devices. | They must not create physical routes or VIIPER slots. | `Hosting/SdlControllerRoutePolicy.cs` | `SdlControllerRoutePolicyTests` |
| SDL can report duplicate Steam entries with the same Steam handle. | Example: `28de:11ff` XInput fallback plus the real Steam Controller. Treat duplicate stable ids as one route and prefer the non-fallback entry. | `Hosting/Client/Run/Controllers/ClientControllerRoutePlanner.cs` | `SdlControllerRoutePolicyTests` |
| Steam Input can temporarily report zero SDL controllers after VIIPER outputs appear. | Do not unregister client controller routes just because a rescan is empty or partial; actual removals should come from SDL remove events. | `Hosting/Client/Run/Controllers/ClientControllerSourceRegistry.cs` | Manual/log validated |
| SDL touchpad topology differs by controller. | DS4/DualSense expose one touchpad with two fingers; newer SDL exposes Steam Controller as two touchpads with one finger each. Flatten the first two active contacts before forwarding. | `Inputs/Sdl/SdlGamepadStateReader.cs` | Manual SDL test/logs |
| Steam Controller advanced features depend on upstream SDL support. | SDL init enables `SDL_JOYSTICK_HIDAPI_STEAM`, but this repo no longer vendors newer SDL binaries. Wait for the NuGet native package to expose Steam Controller touchpads. | `Inputs/Sdl/SdlGamepadRuntime.cs` | Manual SDL test/logs |
| SDL controller instance ids are lease/runtime-local. | Do not list under one SDL lease and open stale ids under another. Select/open from the fresh list inside `SdlControllerCatalog.OpenControllers`. | `Inputs/Sdl/SdlControllers.cs` | `AGENTS.md` |
| Keep SDL initialized for the client process lifetime after first controller use. | Calling `SDL_QuitSubSystem` during reconnect can lose Steam Input controller visibility inside the already-running client. | `Inputs/Sdl/SdlGamepadRuntime.cs` | `AGENTS.md` |

## Controller Routing

| Quirk | Why It Matters | Owner | Coverage |
| --- | --- | --- | --- |
| Host-visible physical slots own VIIPER outputs only after a client stream resolves to them. | Unmatched Steam/client routes are candidates, not output routes. This prevents VIIPER outputs from being created before echo filtering and physical matching finish. The exact real Steam Controller stream remains the client-only exception when no host-visible physical counterpart exists. | `Forwarding/Controller/Routing/ControllerBroker.cs`, `Hosting/Server/Orchestration/Lifetime/PhysicalControllerPump.cs` | `ControllerBrokerTests`, `HostingForwardingTests` |
| Steam SDL streams do not expose exact physical controller identity. | Matching first narrows possible pairs by VID/PID/controller family for auto-match only. Remaining ambiguity is resolved passively by correlating fresh standard-control activity timing from the host-visible physical controller and the client-visible Steam stream. | `Hosting/Server/Orchestration/Lifetime/PhysicalControllerPump.ActivityMatching.cs` | `SdlControllerRoutePolicyTests`; real validation still belongs in dependency/manual tests |
| Physical controller slots own virtual output identity. | Steam stream ids identify Steam's virtual stream. The server rewrites them to a physical slot only through exact host-visible path, automatic identity match, or passive activity correlation; unresolved Steam routes do not create physical companion output slots. | `Hosting/Server/Orchestration/Lifetime/PhysicalControllerPump.cs` | `HostingForwardingTests`, `SdlControllerRoutePolicyTests` |
| Steam can report a generic DualSense id while host SDL sees a DualSense Edge id. | Only pair this family mismatch when exactly one host-visible DualSense exists; otherwise refuse to guess. | `Hosting/SdlControllerRoutePolicy.cs` | `SdlControllerRoutePolicyTests` |
| Client path-based physical routes must be host-visible. | A Steam client can report virtual or stale physical-looking paths. The server rejects path routes unless the host physical pump currently sees the same physical controller. | `Hosting/Server/Orchestration/Lifetime/PhysicalControllerPump.cs` | `HostingForwardingTests` |
| Owned VIIPER outputs are filtered by exact observed SDL identity. | VIIPER list output does not expose display names, and Xbox/DS4 VID/PID are real controller ids. VID/PID is used only inside the short just-created-output window to learn the exact SDL path, then filtering is exact-path based. | `Hosting/Server/Orchestration/Lifetime/OwnedVirtualControllerRegistry.cs`, `TrackingControllerOutputFactory.cs` | `SdlControllerRouteFilterTests` |
| VIIPER echo streams are rejected behaviorally. | Steam can re-expose a VIIPER output as a client SDL stream after creation-window identity tracking misses it. Before passive matching trusts a candidate, the host sends a short impossible D-pad probe through already-connected outputs and ignores pending streams that mirror it. | `Hosting/Server/Orchestration/Lifetime/PhysicalControllerPump.ActivityMatching.cs`, `Forwarding/Controller/Routing/ControllerBroker.Outputs.cs` | Manual/dependency validation needed |
| Physical-only slots do not create virtual outputs. | A controller connected after the client starts stays a host-visible physical slot until a matching client Steam stream resolves to it. | `Forwarding/Controller/Routing/ControllerBroker.cs` | `ControllerBrokerTests` |
| Unmatched physical slots must not send reports through an active client route. | Raw physical input may bypass Steam Input mapping and look like held input. Physical state is only fallback data after the active client stream is matched to the same slot. | `Forwarding/Controller/Routing/ControllerSlot.cs` | `ControllerBrokerTests` |
| Active foreground selection gates report data, not output existence. | Alt-tab/profile activation should stop input reports without disconnecting virtual devices. | `Forwarding/Controller/Routing/ControllerBroker.Outputs.cs` | `ControllerBrokerTests` |
| Physical companion triggers can fill client trigger gaps. | Steam Input may expose buttons/sticks while omitting analog trigger axes. The standard merge keeps Steam's buttons/sticks and fills only zero trigger axes from the matched physical slot. | `Forwarding/Controller/Routing/ControllerSlot.cs` | `ControllerBrokerTests` |
| Physical companion touch contacts can fill client touchpad gaps. | DS4/DualSense support two touch contacts, but Steam/client streams may expose only one. The touchpad merge keeps client contacts and fills missing contact slots from the matched physical controller. | `Forwarding/Controller/Routing/ControllerSlot.cs` | `ControllerBrokerTests` |
| Reconcile controller route changes per endpoint. | Adding/removing one controller must not tear down all virtual controllers for that client. | `Hosting/Server/Pipes/ClientControllerPipe.Controllers.cs` | `ControllerBrokerTests` |
| Prune empty controller slots after endpoint removal. | Stale disconnected routes should not remain in diagnostics or output state. | `Forwarding/Controller/Routing/ControllerBroker.cs` | `ControllerBrokerTests` |
| Rumble feedback is route-local. | Feedback from one virtual output must not be selected through a global controller registry. | `Forwarding/Controller/Routing/ControllerSlot.Feedback.cs` | `ControllerBrokerTests` |

## Host And Client

| Quirk | Why It Matters | Owner | Coverage |
| --- | --- | --- | --- |
| The host owns VIIPER outputs. | Multiple Steam-launched clients can see Steam Input differently; one host owns virtual device lifecycle and active-run gating. | `Hosting/Server`, `Outputs/Viiper` | `HostingForwardingTests`, dependency tests |
| Client reconnect gets a new server client id. | After server restart, the client must restore the run, reconnect controller pipe state, and re-register routes. | `Hosting/Client/Run/GameClient.cs` | Manual/log validated |
| Client lifecycle RPCs are bounded. | Receiver-exit updates, end-run cleanup, keepalive ACKs, and reconnect probes must not wait forever on a dead JSON-RPC pipe during server restart; the game/client lifecycle must continue. | `Hosting/Client/Connection`, `Hosting/Client/Run/GameClient.cs` | `ServerClientTests`, manual/log validated |
| Receiver processes are the game lifetime signal. | Launchers may exit immediately; the launched root process is not enough to decide active run lifetime. | `Runtime`, `Hosting/Client/Run` | `ManualProfileRoutingTests` |
| Launched-profile receiver pids are lifecycle-owned. | Some games escape the launched root process/job. Capture receiver pids before launch, then stop only newly observed receiver pids on client shutdown. | `Hosting/Client/Run`, `Runtime/ActiveClientRegistry.cs` | `ActiveClientRegistryTests` |
| Only stop processes explicitly launched or owned by this repository. | Receiver process claims are routing state, not kill permission. | `Runtime/GameProcessKiller.cs` | `AGENTS.md` |

## VIIPER

| Quirk | Why It Matters | Owner | Coverage |
| --- | --- | --- | --- |
| VIIPER devices use route-specific VID/PID markers. | Startup reclaim must remove only devices owned by this repository. | `Outputs/Viiper` | `ViiperDependencyTests` |
| VIIPER `DevId` is bus-local. | Device identity must include `BusID + DevId`. | `Outputs/Viiper/Shared` | `AGENTS.md` |
| Remove the VIIPER device and bus before waiting on connected streams to dispose. | Generated client streams can block while the output read loop waits. Device removal must not wait on stream shutdown. | `Outputs/Viiper/Shared` | `AGENTS.md` |
| Rapid VIIPER recreation can transiently fail. | Retry the whole bus/device create attempt narrowly; do not add more devices to a failed bus. | `Outputs/Viiper/Shared` | `AGENTS.md` |
| VIIPER DS4 d-pad is a bitfield, not a USB HID hat value. | Sending HID neutral value `8` means a held right direction in VIIPER's protocol. Neutral must be `0`; diagonals are OR'ed bits. | `Forwarding/Controller/ControllerReports.cs`, `Outputs/Viiper/ViiperDs4Output.cs` | `ControllerOutputMappingTests` |

## HidHide

| Quirk | Why It Matters | Owner | Coverage |
| --- | --- | --- | --- |
| Stock HidHide cannot express per-device per-process visibility. | It cannot simultaneously hide VIIPER from Steam and hide physical controllers from only the game with one global app list. | `HidHide`, `Hosting/Server/Orchestration/Active` | `REVIEW.md` |
| HidHide normal mode uses one global app allowlist for every hidden device. | Register this executable and HidHideCLI.exe once, keep them on the app list, hide scoped physical controllers, force inverse mode off, then restore only device and mode state. Do not prune the user's app list. | `HidHide/HidHideService.cs` | `HidHideServiceTests` |
| Other tools can hide source controllers with HidHide. | Since this executable is allowlisted, it can see controllers hidden by DSX-like tools. Reject hidden devices that are not part of this app's active HidHide scope to avoid duplicating an original and its replacement. | `Hosting/Server/Orchestration/Lifetime/ServerControllerInputFilter.cs` | `HidHideServiceTests` |
| Steam Input still feeds the client when only this executable is on HidHide's app list. | Do not add Steam to the temporary HidHide allowlist unless a failing route proves it is needed; adding it broadens hidden-device visibility. | `HidHide/HidHideService.cs` | Manual validated |
| Some receiver process paths are not readable. | Normal-mode HidHide scopes must be based on resolved device paths and owned receiver presence, not receiver executable paths. | `HidHide/HidHideScope.cs`, `Hosting/Server/Orchestration/Active` | `HidHideServiceTests` |
| HidHide scopes follow live profile clients, not foreground focus. | Physical controllers should stay hidden for the lifetime of the forwarding client so games do not see double input while the run is alive. Client end/disconnect clears the scope. | `Hosting/Server/Orchestration/Active/ServerHidHideCoordinator.cs` | `HidHideServiceTests` |
| HidHide scopes use client-registered physical routes. | Hiding a physical controller before Steam exposes its Steam Input stream can leave the client with zero SDL controllers. Hide after the client route resolves to that physical device. | `Hosting/Server/Orchestration/Lifetime/ServerHidHideDeviceResolver.cs` | `HidHideServiceTests`, manual/log validated |
| HidHide can transiently remove scoped controllers from SDL enumeration. | During scope changes, do not tear down an already-open physical slot just because one SDL scan omits an app-owned hidden device; wait for a real disconnect event from the open source. | `Hosting/Server/Orchestration/Lifetime/PhysicalControllerPump.cs` | `HidHideServiceTests` |
| HidHide app-list state is user-owned global state. | Do not remove app entries at startup or during profile scopes. The only normal mutation is adding this executable and HidHideCLI.exe if either is missing. | `HidHide/HidHideService.cs` | `HidHideServiceTests` |
| Current-scope HidHide devices are app-owned while a profile runs. | Clear must unhide those devices even if they were already hidden when the scope was captured; otherwise stale hides from a prior server lifetime are preserved forever. Foreign hidden devices should be excluded before scope creation. | `HidHide/HidHideService.cs`, `Hosting/Server/Orchestration/Lifetime/ServerControllerInputFilter.cs` | `HidHideServiceTests` |
| Persist the app-owned HidHide scope. | HidHide mutates global driver state, so a server crash/restart can leave physical controllers hidden after every client is gone. Persist only the app-owned scope and restore it at server startup. | `HidHide/HidHideOwnedScopeStore.cs`, `HidHide/HidHideService.cs` | `HidHideServiceTests` |

## Raw Input Mouse

| Quirk | Why It Matters | Owner | Coverage |
| --- | --- | --- | --- |
| Steam Input mouse events use empty Raw Input device metadata. | Forwarding only metadata-empty Raw Input events is how the Steam mouse stream is separated from normal physical mouse input. | `Inputs/RawInput` | Source comment, `REVIEW.md` |
| Raw Input follows the high-performance buffered path. | Hot-path mouse input should avoid per-report allocations and extra native calls. | `Inputs/RawInput` | Source comment |
