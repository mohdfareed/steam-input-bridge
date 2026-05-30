using System;
using System.Collections.Generic;
using SteamInputBridge.Forwarding.Controller;
using SteamInputBridge.Inputs.Sdl;

namespace SteamInputBridge.Hosting.Server.Orchestration.Lifetime;

internal sealed partial class PhysicalControllerPump
{
    private static readonly TimeSpan PassiveMatchWindow = TimeSpan.FromMilliseconds(250);

    private readonly Dictionary<ControllerMatchKey, ClientMatchState> _clientStates = [];
    private readonly Dictionary<string, ActivityTracker> _physicalActivity =
        new(StringComparer.OrdinalIgnoreCase);

    private bool TryResolveActivityMatch(
        Guid clientId,
        ClientControllerInfo controller,
        out ClientControllerInfo resolved)
    {
        ControllerMatchKey key = new(clientId, controller.ControllerIndex);
        lock (_gate)
        {
            EnsurePendingMatchNoLock(key, controller);
            ApplyAutomaticMatchesNoLock();

            ClientMatchState state = GetClientStateNoLock(key);
            if (!state.Match.HasValue)
            {
                resolved = null!;
                return false;
            }

            MatchedController match = state.Match.Value;
            if (!IsSameIdentity(match.Identity, CreateMatchIdentity(controller)) ||
                FindPhysicalControllerByIdNoLock(match.PhysicalControllerId) is not { } physical)
            {
                state.Match = null;
                resolved = null!;
                return false;
            }

            resolved = ResolveToPhysicalController(controller, physical);
            return true;
        }
    }

    private bool CanUseClientOnlyOutput(ClientControllerInfo controller)
    {
        lock (_gate)
        {
            return SdlControllerRoutePolicy.CanOwnOutputWithoutPhysical(controller) &&
                !HasPossiblePhysicalCounterpartNoLock(controller);
        }
    }

    private void TrackPendingMatch(Guid clientId, ClientControllerInfo controller)
    {
        lock (_gate)
        {
            ControllerMatchKey key = new(clientId, controller.ControllerIndex);
            EnsurePendingMatchNoLock(key, controller);
            ApplyAutomaticMatchesNoLock();
        }
    }

    private bool ObserveClientMatchInput(Guid clientId, ushort controllerIndex, ControllerState state)
    {
        ControllerMatchKey key = new(clientId, controllerIndex);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        bool matched;
        lock (_gate)
        {
            ActivityTracker tracker = GetClientStateNoLock(key).Activity;
            _ = tracker.Update(state, now);

            matched = TryApplyPassiveActivityMatchNoLock(now);
        }

        return matched;
    }

    private bool ObservePhysicalMatchInput(SdlControllerInfo controller, ControllerState state)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        bool matched;
        lock (_gate)
        {
            string physicalId = SdlControllerRoutePolicy.GetPhysicalControllerId(controller);
            ActivityTracker tracker = GetPhysicalActivityNoLock(physicalId);
            _ = tracker.Update(state, now);
            matched = TryApplyPassiveActivityMatchNoLock(now);
        }

        return matched;
    }

    private void InitializePhysicalActivityNoLock(SdlControllerInfo controller, ControllerState state)
    {
        string physicalId = SdlControllerRoutePolicy.GetPhysicalControllerId(controller);
        _ = GetPhysicalActivityNoLock(physicalId).Update(state, DateTimeOffset.UtcNow);
    }

    private void RemovePhysicalActivityNoLock(SdlControllerInfo controller)
    {
        _ = _physicalActivity.Remove(SdlControllerRoutePolicy.GetPhysicalControllerId(controller));
    }

    private void RemoveClientMatches(Guid clientId)
    {
        HashSet<ControllerMatchKey> remove = [];
        AddClientKeys(remove, _clientStates.Keys, clientId);

        foreach (ControllerMatchKey key in remove)
        {
            _ = _clientStates.Remove(key);
        }
    }

    private void RemoveClientMatchesExcept(
        Guid clientId,
        HashSet<ControllerMatchKey> current,
        IReadOnlyList<ClientControllerInfo> controllers)
    {
        Dictionary<ControllerMatchKey, ControllerMatchIdentity> routes = [];
        foreach (ClientControllerInfo controller in controllers)
        {
            routes[new ControllerMatchKey(clientId, controller.ControllerIndex)] = CreateMatchIdentity(controller);
        }

        HashSet<ControllerMatchKey> remove = [];
        foreach (KeyValuePair<ControllerMatchKey, ClientMatchState> entry in _clientStates)
        {
            if (entry.Key.ClientId == clientId &&
                (!current.Contains(entry.Key) ||
                !routes.TryGetValue(entry.Key, out ControllerMatchIdentity identity) ||
                !entry.Value.Matches(identity)))
            {
                _ = remove.Add(entry.Key);
            }
        }

        foreach (ControllerMatchKey key in remove)
        {
            _ = _clientStates.Remove(key);
        }
    }

    private void TrackClientControllerBatchNoLock(
        Guid clientId,
        IReadOnlyList<ClientControllerInfo> controllers)
    {
        foreach (ClientControllerInfo controller in controllers)
        {
            EnsurePendingMatchNoLock(new ControllerMatchKey(clientId, controller.ControllerIndex), controller);
        }

        ApplyAutomaticMatchesNoLock();
    }

    private void EnsurePendingMatchNoLock(ControllerMatchKey key, ClientControllerInfo controller)
    {
        ClientMatchState state = GetClientStateNoLock(key);
        ControllerMatchIdentity identity = CreateMatchIdentity(controller);
        state.Identity = identity;
        if (!IsMatchCandidate(controller) ||
            state.Match.HasValue)
        {
            return;
        }

        state.Pending = new PendingMatch(identity);
    }

    private bool TryApplyPassiveActivityMatchNoLock(DateTimeOffset now)
    {
        if (!TryFindPassiveActivityMatchNoLock(now, out PassiveActivityMatch match))
        {
            return false;
        }

        ClientMatchState state = GetClientStateNoLock(match.Key);
        PendingMatch pending = state.Pending!.Value;
        state.Match = new MatchedController(pending.Identity, match.PhysicalControllerId);
        state.Pending = null;
        ApplyAutomaticMatchesNoLock();
        HostingLog.PassiveControllerMatched(
            logger,
            match.Key.ClientId,
            match.Key.ControllerIndex,
            match.PhysicalControllerId);
        return true;
    }

    private void ApplyAutomaticMatchesNoLock()
    {
        while (TryFindAutomaticMatchNoLock(out AutomaticMatch match))
        {
            ClientMatchState state = GetClientStateNoLock(match.Key);
            state.Match = new MatchedController(match.Identity, match.PhysicalControllerId);
            state.Pending = null;
        }
    }

    private bool TryFindAutomaticMatchNoLock(out AutomaticMatch match)
    {
        HashSet<string> matchedPhysicalIds = [];
        Dictionary<ControllerMatchKey, List<PhysicalCandidate>> streamCandidates = [];
        Dictionary<PhysicalClientCandidate, List<ControllerMatchKey>> physicalCandidates = [];

        foreach (KeyValuePair<ControllerMatchKey, ClientMatchState> entry in _clientStates)
        {
            if (!entry.Value.Pending.HasValue)
            {
                continue;
            }

            PendingMatch pending = entry.Value.Pending.Value;
            matchedPhysicalIds.Clear();
            matchedPhysicalIds.UnionWith(GetMatchedPhysicalIdsNoLock(entry.Key.ClientId));
            List<PhysicalCandidate> candidates = [];
            foreach (SdlGamepadSource source in _sources.Values)
            {
                string physicalId = SdlControllerRoutePolicy.GetPhysicalControllerId(source.Controller);
                if (matchedPhysicalIds.Contains(physicalId) ||
                    !CanMatchPendingToPhysicalNoLock(pending, source.Controller))
                {
                    continue;
                }

                candidates.Add(new PhysicalCandidate(physicalId));
                PhysicalClientCandidate candidateKey = new(entry.Key.ClientId, physicalId);
                if (!physicalCandidates.TryGetValue(candidateKey, out List<ControllerMatchKey>? keys))
                {
                    keys = [];
                    physicalCandidates[candidateKey] = keys;
                }

                keys.Add(entry.Key);
            }

            if (candidates.Count != 0)
            {
                streamCandidates[entry.Key] = candidates;
            }
        }

        foreach (KeyValuePair<ControllerMatchKey, List<PhysicalCandidate>> entry in streamCandidates)
        {
            if (entry.Value.Count != 1)
            {
                continue;
            }

            PhysicalCandidate physical = entry.Value[0];
            PhysicalClientCandidate candidateKey = new(entry.Key.ClientId, physical.Id);
            if (!physicalCandidates.TryGetValue(candidateKey, out List<ControllerMatchKey>? keys) ||
                keys.Count != 1)
            {
                continue;
            }

            PendingMatch pending = _clientStates[entry.Key].Pending!.Value;
            match = new AutomaticMatch(entry.Key, pending.Identity, physical.Id);
            return true;
        }

        match = default;
        return false;
    }

    private bool TryFindPassiveActivityMatchNoLock(
        DateTimeOffset now,
        out PassiveActivityMatch match)
    {
        List<PassiveActivityMatch> candidates = [];
        HashSet<string> matchedPhysicalIds = [];

        foreach (KeyValuePair<ControllerMatchKey, ClientMatchState> entry in _clientStates)
        {
            if (!entry.Value.Pending.HasValue ||
                !entry.Value.Activity.HasRecentActivity(now, PassiveMatchWindow))
            {
                continue;
            }

            PendingMatch pending = entry.Value.Pending.Value;
            ActivityTracker clientActivity = entry.Value.Activity;
            matchedPhysicalIds.Clear();
            matchedPhysicalIds.UnionWith(GetMatchedPhysicalIdsNoLock(entry.Key.ClientId));
            foreach (SdlGamepadSource source in _sources.Values)
            {
                string physicalId = SdlControllerRoutePolicy.GetPhysicalControllerId(source.Controller);
                if (matchedPhysicalIds.Contains(physicalId) ||
                    !_physicalActivity.TryGetValue(physicalId, out ActivityTracker? physicalActivity) ||
                    !physicalActivity.HasRecentActivity(now, PassiveMatchWindow) ||
                    !CanMatchPendingToPhysicalNoLock(pending, source.Controller) ||
                    !ActivityTimesMatch(clientActivity, physicalActivity))
                {
                    continue;
                }

                candidates.Add(new PassiveActivityMatch(entry.Key, physicalId));
            }
        }

        if (candidates.Count == 1)
        {
            match = candidates[0];
            return true;
        }

        match = default;
        return false;
    }

    private SdlControllerInfo? FindPhysicalControllerByIdNoLock(string physicalControllerId)
    {
        foreach (SdlGamepadSource source in _sources.Values)
        {
            if (string.Equals(
                    SdlControllerRoutePolicy.GetPhysicalControllerId(source.Controller),
                    physicalControllerId,
                    StringComparison.OrdinalIgnoreCase))
            {
                return source.Controller;
            }
        }

        return null;
    }

    private bool HasPossiblePhysicalCounterpartNoLock(ClientControllerInfo controller)
    {
        foreach (SdlGamepadSource source in _sources.Values)
        {
            if (SdlControllerRoutePolicy.CanBePhysicalCounterpart(
                    controller.VendorId,
                    controller.ProductId,
                    controller.Label,
                    source.Controller))
            {
                return true;
            }
        }

        return false;
    }

    private HashSet<string> GetMatchedPhysicalIdsNoLock(Guid clientId)
    {
        HashSet<string> physicalIds = new(StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<ControllerMatchKey, ClientMatchState> entry in _clientStates)
        {
            if (entry.Key.ClientId == clientId &&
                entry.Value.Match.HasValue &&
                FindPhysicalControllerByIdNoLock(entry.Value.Match.Value.PhysicalControllerId) is not null)
            {
                _ = physicalIds.Add(entry.Value.Match.Value.PhysicalControllerId);
            }
        }

        return physicalIds;
    }

    private ClientMatchState GetClientStateNoLock(ControllerMatchKey key)
    {
        if (!_clientStates.TryGetValue(key, out ClientMatchState? state))
        {
            state = new ClientMatchState();
            _clientStates[key] = state;
        }

        return state;
    }

    private ActivityTracker GetPhysicalActivityNoLock(string physicalId)
    {
        if (!_physicalActivity.TryGetValue(physicalId, out ActivityTracker? tracker))
        {
            tracker = new ActivityTracker();
            _physicalActivity[physicalId] = tracker;
        }

        return tracker;
    }

    private static ClientControllerInfo ResolveToPhysicalController(
        ClientControllerInfo controller,
        SdlControllerInfo physical)
    {
        return controller with
        {
            PhysicalControllerId = SdlControllerRoutePolicy.GetPhysicalControllerId(physical),
            Label = physical.Name,
            PhysicalDeviceId = GetPathControllerId(physical),
        };
    }

    private static bool CanMatchPendingToPhysicalNoLock(
        PendingMatch pending,
        SdlControllerInfo physical)
    {
        return SdlControllerRoutePolicy.CanBePhysicalCounterpart(
            pending.VendorId,
            pending.ProductId,
            pending.Label,
            physical);
    }

    private static bool IsMatchCandidate(ClientControllerInfo controller)
    {
        return IsSteamRoute(controller.PhysicalControllerId) &&
            string.IsNullOrWhiteSpace(controller.PhysicalDeviceId);
    }

    private static void AddClientKeys(
        HashSet<ControllerMatchKey> target,
        IEnumerable<ControllerMatchKey> keys,
        Guid clientId)
    {
        foreach (ControllerMatchKey key in keys)
        {
            if (key.ClientId == clientId)
            {
                _ = target.Add(key);
            }
        }
    }

    private static ControllerMatchIdentity CreateMatchIdentity(ClientControllerInfo controller)
    {
        return new ControllerMatchIdentity(
            controller.PhysicalControllerId,
            controller.Label,
            controller.VendorId,
            controller.ProductId);
    }

    private static bool IsSameIdentity(ControllerMatchIdentity first, ControllerMatchIdentity second)
    {
        return string.Equals(first.SteamRouteId, second.SteamRouteId, StringComparison.Ordinal) &&
            first.VendorId == second.VendorId &&
            first.ProductId == second.ProductId &&
            string.Equals(first.Label, second.Label, StringComparison.OrdinalIgnoreCase);
    }

    private static bool ActivityTimesMatch(ActivityTracker first, ActivityTracker second)
    {
        if (!first.LastChangedAt.HasValue || !second.LastChangedAt.HasValue)
        {
            return false;
        }

        TimeSpan delta = first.LastChangedAt.Value - second.LastChangedAt.Value;
        return delta.Duration() <= PassiveMatchWindow;
    }

    private static long GetActivitySignature(ControllerState state)
    {
        if (state.Standard is not { } standard)
        {
            return 0;
        }

        long signature = (ushort)standard.Buttons;
        signature |= AxisSignature(standard.LeftX, 16);
        signature |= AxisSignature(standard.LeftY, 18);
        signature |= AxisSignature(standard.RightX, 20);
        signature |= AxisSignature(standard.RightY, 22);
        signature |= TriggerSignature(standard.LeftTrigger, 24);
        signature |= TriggerSignature(standard.RightTrigger, 25);
        return signature;
    }

    private static long AxisSignature(short value, int offset)
    {
        const short threshold = 12000;
        return value > threshold
            ? 1L << offset
            : value < -threshold
            ? 1L << (offset + 1)
            : 0;
    }

    private static long TriggerSignature(ushort value, int offset)
    {
        const ushort threshold = 20000;
        return value > threshold ? 1L << offset : 0;
    }

    private readonly record struct ControllerMatchKey(Guid ClientId, ushort ControllerIndex);

    private readonly record struct ControllerMatchIdentity(
        string SteamRouteId,
        string Label,
        ushort VendorId,
        ushort ProductId);

    private readonly record struct PendingMatch(ControllerMatchIdentity Identity)
    {
        public string SteamRouteId => Identity.SteamRouteId;

        public string Label => Identity.Label;

        public ushort VendorId => Identity.VendorId;

        public ushort ProductId => Identity.ProductId;
    }

    private readonly record struct MatchedController(
        ControllerMatchIdentity Identity,
        string PhysicalControllerId);

    private readonly record struct AutomaticMatch(
        ControllerMatchKey Key,
        ControllerMatchIdentity Identity,
        string PhysicalControllerId);

    private readonly record struct PassiveActivityMatch(ControllerMatchKey Key, string PhysicalControllerId);

    private readonly record struct PhysicalCandidate(string Id);

    private readonly record struct PhysicalClientCandidate(Guid ClientId, string PhysicalId);

    private sealed class ClientMatchState
    {
        public ControllerMatchIdentity? Identity { get; set; }

        public PendingMatch? Pending { get; set; }

        public MatchedController? Match { get; set; }

        public ActivityTracker Activity { get; } = new();

        public bool Matches(ControllerMatchIdentity identity)
        {
            return Matches(Identity, identity) &&
                Matches(Pending?.Identity, identity) &&
                Matches(Match?.Identity, identity);
        }

        private static bool Matches(ControllerMatchIdentity? current, ControllerMatchIdentity identity)
        {
            return !current.HasValue || IsSameIdentity(current.Value, identity);
        }
    }

    private sealed class ActivityTracker
    {
        private long _baselineSignature;
        private bool _hasBaseline;

        public long CurrentSignature { get; private set; }

        public DateTimeOffset? LastChangedAt { get; private set; }

        public bool Update(ControllerState state, DateTimeOffset now)
        {
            long signature = GetActivitySignature(state);
            if (!_hasBaseline)
            {
                _baselineSignature = signature;
                CurrentSignature = signature;
                _hasBaseline = true;
                return false;
            }

            if (signature == CurrentSignature)
            {
                return false;
            }

            CurrentSignature = signature;
            LastChangedAt = now;
            return true;
        }

        public bool HasRecentActivity(DateTimeOffset now, TimeSpan window)
        {
            return _hasBaseline &&
                CurrentSignature != _baselineSignature &&
                LastChangedAt.HasValue &&
                now - LastChangedAt.Value <= window;
        }
    }
}
