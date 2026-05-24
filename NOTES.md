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
| SDL controller instance ids are lease/runtime-local. | Do not list under one SDL lease and open stale ids under another. Select/open from the fresh list inside `SdlControllerCatalog.OpenControllers`. | `Inputs/Sdl/SdlControllers.cs` | `AGENTS.md` |
| Keep SDL initialized for the client process lifetime after first controller use. | Calling `SDL_QuitSubSystem` during reconnect can lose Steam Input controller visibility inside the already-running client. | `Inputs/Sdl/SdlGamepadRuntime.cs` | `AGENTS.md` |

## Controller Routing

| Quirk | Why It Matters | Owner | Coverage |
| --- | --- | --- | --- |
| Registered controller endpoints create VIIPER outputs immediately with empty state. | Reconnected clients and inactive clients need virtual controllers created before the first input frame. | `Forwarding/Controller/Routing/ControllerBroker.cs` | `ControllerBrokerTests` |
| Physical controller slots own virtual output identity. | Steam stream ids identify Steam's virtual stream, so the server rewrites them to a physical slot only when the host sees one unambiguous physical counterpart. | `Hosting/Server/Orchestration/Lifetime/PhysicalControllerPump.cs` | `HostingForwardingTests`, `SdlControllerRoutePolicyTests` |
| Client path-based physical routes must be host-visible. | A Steam client can report virtual or stale physical-looking paths. The server rejects path routes unless the host physical pump currently sees the same physical controller. | `Hosting/Server/Orchestration/Lifetime/PhysicalControllerPump.cs` | `HostingForwardingTests` |
| Owned VIIPER outputs are filtered by exact observed SDL identity. | VIIPER list output does not expose display names, and Xbox/DS4 VID/PID are real controller ids. VID/PID is used only inside the short just-created-output window to learn the exact SDL path, then filtering is exact-path based. | `Hosting/Server/Orchestration/Lifetime/OwnedVirtualControllerRegistry.cs`, `TrackingControllerOutputFactory.cs` | `SdlControllerRouteFilterTests` |
| Physical-only slots do not create virtual outputs. | The host can read physical controllers globally, but a VIIPER controller should exist only when a client route with controller output attaches to that physical slot. | `Forwarding/Controller/Routing/ControllerBroker.cs` | `ControllerBrokerTests` |
| Active foreground selection gates report data, not output existence. | Alt-tab/profile activation should stop input reports without disconnecting virtual devices. | `Forwarding/Controller/Routing/ControllerBroker.Outputs.cs` | `ControllerBrokerTests` |
| Reconcile controller route changes per endpoint. | Adding/removing one controller must not tear down all virtual controllers for that client. | `Hosting/Server/Pipes/ClientControllerPipe.Controllers.cs` | `ControllerBrokerTests` |
| Prune empty controller slots after endpoint removal. | Stale disconnected routes should not remain in diagnostics or output state. | `Forwarding/Controller/Routing/ControllerBroker.cs` | `ControllerBrokerTests` |
| Rumble feedback is route-local. | Feedback from one virtual output must not be selected through a global controller registry. | `Forwarding/Controller/Routing/ControllerSlot.Feedback.cs` | `ControllerBrokerTests` |

## Host And Client

| Quirk | Why It Matters | Owner | Coverage |
| --- | --- | --- | --- |
| The host owns VIIPER outputs. | Multiple Steam-launched clients can see Steam Input differently; one host owns virtual device lifecycle and active-run gating. | `Hosting/Server`, `Outputs/Viiper` | `HostingForwardingTests`, dependency tests |
| Client reconnect gets a new server client id. | After server restart, the client must restore the run, reconnect controller pipe state, and re-register routes. | `Hosting/Client/Run/GameClient.cs` | Manual/log validated |
| Receiver processes are the game lifetime signal. | Launchers may exit immediately; the launched root process is not enough to decide active run lifetime. | `Runtime`, `Hosting/Client/Run` | `ManualProfileRoutingTests` |
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
| HidHide app-list state is user-owned global state. | Do not remove app entries at startup or during profile scopes. The only normal mutation is adding this executable and HidHideCLI.exe if either is missing. | `HidHide/HidHideService.cs` | `HidHideServiceTests` |

## Raw Input Mouse

| Quirk | Why It Matters | Owner | Coverage |
| --- | --- | --- | --- |
| Steam Input mouse events use empty Raw Input device metadata. | Forwarding only metadata-empty Raw Input events is how the Steam mouse stream is separated from normal physical mouse input. | `Inputs/RawInput` | Source comment, `REVIEW.md` |
| Raw Input follows the high-performance buffered path. | Hot-path mouse input should avoid per-report allocations and extra native calls. | `Inputs/RawInput` | Source comment |
