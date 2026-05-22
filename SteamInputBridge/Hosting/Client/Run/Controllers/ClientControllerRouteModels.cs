using System.Collections.Generic;
using SteamInputBridge.Inputs.Sdl;

namespace SteamInputBridge.Hosting.Client.Run.Controllers;

internal sealed record ClientControllerRoutePlan(
    IReadOnlyList<ClientControllerInfo> Controllers,
    IReadOnlyDictionary<SdlGamepadSource, SdlControllerRouteIdentity> Identities);

internal readonly record struct ClientControllerRouteSource(
    ushort ControllerIndex,
    SdlGamepadSource Source);
