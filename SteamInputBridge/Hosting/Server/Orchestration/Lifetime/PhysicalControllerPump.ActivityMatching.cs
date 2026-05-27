using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SteamInputBridge.Forwarding.Controller;
using SteamInputBridge.Inputs.Sdl;

namespace SteamInputBridge.Hosting.Server.Orchestration.Lifetime;

internal sealed partial class PhysicalControllerPump
{
    private static readonly TimeSpan PassiveMatchWindow = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan VirtualEchoProbeDuration = TimeSpan.FromMilliseconds(120);
    private static readonly TimeSpan VirtualEchoProbeResponseWindow = TimeSpan.FromMilliseconds(350);
    // Existing VIIPER outputs can show up as Steam-visible SDL streams for a
    // second client. Probe only already-connected outputs, then keep unresolved
    // streams as candidates until physical/client activity proves a match.
    private static readonly ControllerState VirtualEchoProbeState = new(
        new ControllerStandardState(
            ControllerButtons.DPadUp |
            ControllerButtons.DPadDown |
            ControllerButtons.DPadLeft |
            ControllerButtons.DPadRight,
            0,
            0,
            0,
            0,
            0,
            0),
        null,
        null);
    private static readonly long VirtualEchoProbeSignature = GetActivitySignature(VirtualEchoProbeState);

    private readonly Dictionary<ControllerMatchKey, PendingMatch> _pendingMatches = [];
    private readonly Dictionary<ControllerMatchKey, MatchedController> _matches = [];
    private readonly Dictionary<ControllerMatchKey, ControllerMatchIdentity> _virtualEchoes = [];
    private readonly Dictionary<ControllerMatchKey, ControllerMatchIdentity> _virtualEchoProbeAttempts = [];
    private readonly Dictionary<ControllerMatchKey, ActivityTracker> _clientActivity = [];
    private readonly Dictionary<string, ActivityTracker> _physicalActivity =
        new(StringComparer.OrdinalIgnoreCase);
    private VirtualEchoProbe? _virtualEchoProbe;
    private Task? _virtualEchoProbeTask;

    private bool TryResolveActivityMatch(
        Guid clientId,
        ClientControllerInfo controller,
        out ClientControllerInfo resolved)
    {
        ControllerMatchKey key = new(clientId, controller.ControllerIndex);
        lock (_gate)
        {
            EnsurePendingMatchNoLock(key, controller);
            _ = ApplyAutomaticMatchesNoLock();

            if (!_matches.TryGetValue(key, out MatchedController match))
            {
                resolved = null!;
                return false;
            }

            if (!IsSameIdentity(match.Identity, CreateMatchIdentity(controller)) ||
                FindPhysicalControllerByIdNoLock(match.PhysicalControllerId) is not { } physical)
            {
                _ = _matches.Remove(key);
                resolved = null!;
                return false;
            }

            resolved = ResolveToPhysicalController(controller, physical);
            return true;
        }
    }

    private bool CanUseClientOnlyOutput(Guid clientId, ClientControllerInfo controller)
    {
        ControllerMatchKey key = new(clientId, controller.ControllerIndex);
        lock (_gate)
        {
            return SdlControllerRoutePolicy.CanOwnOutputWithoutPhysical(controller) &&
                !IsKnownVirtualEchoNoLock(key, controller) &&
                !HasPossiblePhysicalCounterpartNoLock(controller);
        }
    }

    private void TrackPendingMatch(Guid clientId, ClientControllerInfo controller)
    {
        bool scheduleEchoProbe;
        lock (_gate)
        {
            ControllerMatchKey key = new(clientId, controller.ControllerIndex);
            if (IsKnownVirtualEchoNoLock(key, controller))
            {
                return;
            }

            EnsurePendingMatchNoLock(key, controller);
            _ = ApplyAutomaticMatchesNoLock();
            scheduleEchoProbe = !HasCurrentEchoProbeAttemptNoLock(key, controller);
        }

        if (scheduleEchoProbe)
        {
            ScheduleVirtualEchoProbe();
        }
    }

    private bool ObserveClientMatchInput(Guid clientId, ushort controllerIndex, ControllerState state)
    {
        ControllerMatchKey key = new(clientId, controllerIndex);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        bool matched;
        lock (_gate)
        {
            ActivityTracker tracker = GetClientActivityNoLock(key);
            _ = tracker.Update(state, now);

            if (_virtualEchoProbe is { } echoProbe &&
                _pendingMatches.ContainsKey(key) &&
                echoProbe.Baselines.TryGetValue(key, out long echoBaseline) &&
                tracker.CurrentSignature == VirtualEchoProbeSignature &&
                tracker.CurrentSignature != echoBaseline)
            {
                _ = echoProbe.Responses.Add(key);
            }

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
        AddClientKeys(remove, _pendingMatches.Keys, clientId);
        AddClientKeys(remove, _matches.Keys, clientId);
        AddClientKeys(remove, _virtualEchoes.Keys, clientId);
        AddClientKeys(remove, _virtualEchoProbeAttempts.Keys, clientId);
        AddClientKeys(remove, _clientActivity.Keys, clientId);

        foreach (ControllerMatchKey key in remove)
        {
            _ = _pendingMatches.Remove(key);
            _ = _matches.Remove(key);
            _ = _virtualEchoes.Remove(key);
            _ = _virtualEchoProbeAttempts.Remove(key);
            _ = _clientActivity.Remove(key);
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
        AddStaleKeys(remove, _pendingMatches, clientId, current, routes);
        AddStaleKeys(remove, _matches, clientId, current, routes);
        AddStaleKeys(remove, _virtualEchoes, clientId, current, routes);
        AddStaleKeys(remove, _virtualEchoProbeAttempts, clientId, current, routes);
        foreach (ControllerMatchKey key in _clientActivity.Keys)
        {
            if (key.ClientId == clientId && !current.Contains(key))
            {
                _ = remove.Add(key);
            }
        }

        foreach (ControllerMatchKey key in remove)
        {
            _ = _pendingMatches.Remove(key);
            _ = _matches.Remove(key);
            _ = _virtualEchoes.Remove(key);
            _ = _virtualEchoProbeAttempts.Remove(key);
            _ = _clientActivity.Remove(key);
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

        _ = ApplyAutomaticMatchesNoLock();
    }

    private void EnsurePendingMatchNoLock(ControllerMatchKey key, ClientControllerInfo controller)
    {
        if (!IsMatchCandidate(controller) ||
            IsKnownVirtualEchoNoLock(key, controller) ||
            _matches.ContainsKey(key))
        {
            return;
        }

        _pendingMatches[key] = new PendingMatch(CreateMatchIdentity(controller));
    }

    private bool TryApplyPassiveActivityMatchNoLock(DateTimeOffset now)
    {
        if (!TryFindPassiveActivityMatchNoLock(now, out PassiveActivityMatch match))
        {
            return false;
        }

        PendingMatch pending = _pendingMatches[match.Key];
        _matches[match.Key] = new MatchedController(pending.Identity, match.PhysicalControllerId);
        _ = _pendingMatches.Remove(match.Key);
        _ = ApplyAutomaticMatchesNoLock();
        HostingLog.PassiveControllerMatched(
            logger,
            match.Key.ClientId,
            match.Key.ControllerIndex,
            match.PhysicalControllerId);
        return true;
    }

    private List<AutomaticMatch> ApplyAutomaticMatchesNoLock()
    {
        List<AutomaticMatch> matches = [];
        while (TryFindAutomaticMatchNoLock(out AutomaticMatch match))
        {
            _matches[match.Key] = new MatchedController(match.Identity, match.PhysicalControllerId);
            _ = _pendingMatches.Remove(match.Key);
            matches.Add(match);
        }

        return matches;
    }

    private bool TryFindAutomaticMatchNoLock(out AutomaticMatch match)
    {
        HashSet<string> matchedPhysicalIds = [];
        Dictionary<ControllerMatchKey, List<PhysicalCandidate>> streamCandidates = [];
        Dictionary<PhysicalClientCandidate, List<ControllerMatchKey>> physicalCandidates = [];

        foreach (KeyValuePair<ControllerMatchKey, PendingMatch> pending in _pendingMatches)
        {
            matchedPhysicalIds.Clear();
            matchedPhysicalIds.UnionWith(GetMatchedPhysicalIdsNoLock(pending.Key.ClientId));
            List<PhysicalCandidate> candidates = [];
            foreach (SdlGamepadSource source in _sources.Values)
            {
                string physicalId = SdlControllerRoutePolicy.GetPhysicalControllerId(source.Controller);
                if (matchedPhysicalIds.Contains(physicalId) ||
                    !CanMatchPendingToPhysicalNoLock(pending.Value, source.Controller))
                {
                    continue;
                }

                candidates.Add(new PhysicalCandidate(physicalId));
                PhysicalClientCandidate candidateKey = new(pending.Key.ClientId, physicalId);
                if (!physicalCandidates.TryGetValue(candidateKey, out List<ControllerMatchKey>? keys))
                {
                    keys = [];
                    physicalCandidates[candidateKey] = keys;
                }

                keys.Add(pending.Key);
            }

            if (candidates.Count != 0)
            {
                streamCandidates[pending.Key] = candidates;
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

            PendingMatch pending = _pendingMatches[entry.Key];
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

        foreach (KeyValuePair<ControllerMatchKey, PendingMatch> pending in _pendingMatches)
        {
            if (!_clientActivity.TryGetValue(pending.Key, out ActivityTracker? clientActivity) ||
                !clientActivity.HasRecentActivity(now, PassiveMatchWindow))
            {
                continue;
            }

            matchedPhysicalIds.Clear();
            matchedPhysicalIds.UnionWith(GetMatchedPhysicalIdsNoLock(pending.Key.ClientId));
            foreach (SdlGamepadSource source in _sources.Values)
            {
                string physicalId = SdlControllerRoutePolicy.GetPhysicalControllerId(source.Controller);
                if (matchedPhysicalIds.Contains(physicalId) ||
                    !_physicalActivity.TryGetValue(physicalId, out ActivityTracker? physicalActivity) ||
                    !physicalActivity.HasRecentActivity(now, PassiveMatchWindow) ||
                    !CanMatchPendingToPhysicalNoLock(pending.Value, source.Controller) ||
                    !ActivityTimesMatch(clientActivity, physicalActivity))
                {
                    continue;
                }

                candidates.Add(new PassiveActivityMatch(pending.Key, physicalId));
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

    private void ScheduleVirtualEchoProbe()
    {
        CancellationToken token;
        lock (_gate)
        {
            if (_pendingMatches.Count == 0 ||
                _virtualEchoProbeTask is { IsCompleted: false } ||
                _stop is null)
            {
                return;
            }

            token = _stop.Token;
            _virtualEchoProbeTask = Task.Run(() => RunVirtualEchoProbeAsync(token), CancellationToken.None);
        }
    }

    private async Task RunVirtualEchoProbeAsync(CancellationToken cancellationToken)
    {
        if (!BeginVirtualEchoProbe())
        {
            return;
        }

        int outputCount = 0;
        try
        {
            outputCount = broker.SendOutputProbe(VirtualEchoProbeState);
            if (outputCount != 0)
            {
                await Task.Delay(VirtualEchoProbeDuration, cancellationToken).ConfigureAwait(false);
                ControllerState empty = ControllerState.Empty;
                _ = broker.SendOutputProbe(empty);
                await Task.Delay(VirtualEchoProbeResponseWindow, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        finally
        {
            if (outputCount != 0)
            {
                ControllerState empty = ControllerState.Empty;
                _ = broker.SendOutputProbe(empty);
            }
        }

        List<VirtualEchoMatch> ignored = CompleteVirtualEchoProbe(outputCount);
        foreach (VirtualEchoMatch ignoredMatch in ignored)
        {
            HostingLog.VirtualEchoControllerIgnored(
                logger,
                ignoredMatch.ClientId,
                ignoredMatch.ControllerIndex,
                ignoredMatch.Label,
                ignoredMatch.RouteId);
        }

        if (ignored.Count != 0)
        {
            ControllersChanged?.Invoke();
        }
    }

    private bool BeginVirtualEchoProbe()
    {
        lock (_gate)
        {
            Dictionary<ControllerMatchKey, long> baselines = [];
            foreach (KeyValuePair<ControllerMatchKey, PendingMatch> entry in _pendingMatches)
            {
                if (HasCurrentEchoProbeAttemptNoLock(entry.Key, entry.Value.Identity))
                {
                    continue;
                }

                ActivityTracker tracker = GetClientActivityNoLock(entry.Key);
                baselines[entry.Key] = tracker.CurrentSignature;
            }

            if (baselines.Count == 0)
            {
                return false;
            }

            _virtualEchoProbe = new VirtualEchoProbe(baselines);
            return true;
        }
    }

    private List<VirtualEchoMatch> CompleteVirtualEchoProbe(int outputCount)
    {
        lock (_gate)
        {
            if (_virtualEchoProbe is not { } probe)
            {
                return [];
            }

            _virtualEchoProbe = null;
            List<VirtualEchoMatch> ignored = [];
            foreach (ControllerMatchKey key in probe.Baselines.Keys)
            {
                if (!_pendingMatches.TryGetValue(key, out PendingMatch pending))
                {
                    continue;
                }

                if (outputCount != 0)
                {
                    _virtualEchoProbeAttempts[key] = pending.Identity;
                }

                if (!probe.Responses.Contains(key))
                {
                    continue;
                }

                _virtualEchoes[key] = pending.Identity;
                _ = _pendingMatches.Remove(key);
                ignored.Add(new VirtualEchoMatch(
                    key.ClientId,
                    key.ControllerIndex,
                    pending.Label,
                    pending.SteamRouteId));
            }

            return ignored;
        }
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

    private bool IsKnownVirtualEchoNoLock(ControllerMatchKey key, ClientControllerInfo controller)
    {
        return IsKnownVirtualEchoNoLock(key, CreateMatchIdentity(controller));
    }

    private bool IsKnownVirtualEchoNoLock(ControllerMatchKey key, ControllerMatchIdentity identity)
    {
        return _virtualEchoes.TryGetValue(key, out ControllerMatchIdentity known) &&
            IsSameIdentity(known, identity);
    }

    private bool HasCurrentEchoProbeAttemptNoLock(ControllerMatchKey key, ClientControllerInfo controller)
    {
        return HasCurrentEchoProbeAttemptNoLock(key, CreateMatchIdentity(controller));
    }

    private bool HasCurrentEchoProbeAttemptNoLock(ControllerMatchKey key, ControllerMatchIdentity identity)
    {
        return _virtualEchoProbeAttempts.TryGetValue(key, out ControllerMatchIdentity attempted) &&
            IsSameIdentity(attempted, identity);
    }

    private HashSet<string> GetMatchedPhysicalIdsNoLock(Guid clientId)
    {
        HashSet<string> physicalIds = new(StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<ControllerMatchKey, MatchedController> match in _matches)
        {
            if (match.Key.ClientId == clientId &&
                FindPhysicalControllerByIdNoLock(match.Value.PhysicalControllerId) is not null)
            {
                _ = physicalIds.Add(match.Value.PhysicalControllerId);
            }
        }

        return physicalIds;
    }

    private ActivityTracker GetClientActivityNoLock(ControllerMatchKey key)
    {
        if (!_clientActivity.TryGetValue(key, out ActivityTracker? tracker))
        {
            tracker = new ActivityTracker();
            _clientActivity[key] = tracker;
        }

        return tracker;
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

    private static void AddStaleKeys<TValue>(
        HashSet<ControllerMatchKey> remove,
        Dictionary<ControllerMatchKey, TValue> values,
        Guid clientId,
        HashSet<ControllerMatchKey> current,
        Dictionary<ControllerMatchKey, ControllerMatchIdentity> routes)
        where TValue : IControllerMatchEntry
    {
        foreach (KeyValuePair<ControllerMatchKey, TValue> entry in values)
        {
            if (entry.Key.ClientId == clientId &&
                (!current.Contains(entry.Key) ||
                !routes.TryGetValue(entry.Key, out ControllerMatchIdentity identity) ||
                !IsSameIdentity(identity, entry.Value.Identity)))
            {
                _ = remove.Add(entry.Key);
            }
        }
    }

    private static void AddStaleKeys(
        HashSet<ControllerMatchKey> remove,
        Dictionary<ControllerMatchKey, ControllerMatchIdentity> values,
        Guid clientId,
        HashSet<ControllerMatchKey> current,
        Dictionary<ControllerMatchKey, ControllerMatchIdentity> routes)
    {
        foreach (KeyValuePair<ControllerMatchKey, ControllerMatchIdentity> entry in values)
        {
            if (entry.Key.ClientId == clientId &&
                (!current.Contains(entry.Key) ||
                !routes.TryGetValue(entry.Key, out ControllerMatchIdentity identity) ||
                !IsSameIdentity(identity, entry.Value)))
            {
                _ = remove.Add(entry.Key);
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

    private interface IControllerMatchEntry
    {
        ControllerMatchIdentity Identity { get; }
    }

    private readonly record struct ControllerMatchKey(Guid ClientId, ushort ControllerIndex);

    private readonly record struct ControllerMatchIdentity(
        string SteamRouteId,
        string Label,
        ushort VendorId,
        ushort ProductId);

    private readonly record struct PendingMatch(ControllerMatchIdentity Identity) : IControllerMatchEntry
    {
        public string SteamRouteId => Identity.SteamRouteId;

        public string Label => Identity.Label;

        public ushort VendorId => Identity.VendorId;

        public ushort ProductId => Identity.ProductId;
    }

    private readonly record struct MatchedController(
        ControllerMatchIdentity Identity,
        string PhysicalControllerId) : IControllerMatchEntry;

    private readonly record struct AutomaticMatch(
        ControllerMatchKey Key,
        ControllerMatchIdentity Identity,
        string PhysicalControllerId);

    private readonly record struct PassiveActivityMatch(ControllerMatchKey Key, string PhysicalControllerId);

    private readonly record struct PhysicalCandidate(string Id);

    private readonly record struct PhysicalClientCandidate(Guid ClientId, string PhysicalId);

    private readonly record struct VirtualEchoMatch(
        Guid ClientId,
        ushort ControllerIndex,
        string Label,
        string RouteId);

    private sealed class VirtualEchoProbe(Dictionary<ControllerMatchKey, long> baselines)
    {
        public Dictionary<ControllerMatchKey, long> Baselines { get; } = baselines;

        public HashSet<ControllerMatchKey> Responses { get; } = [];
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
