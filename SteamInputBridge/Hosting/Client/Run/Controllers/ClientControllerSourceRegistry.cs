using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SteamInputBridge.Inputs.Sdl;

namespace SteamInputBridge.Hosting.Client.Run.Controllers;

internal sealed class ClientControllerSourceRegistry : IAsyncDisposable
{
    private readonly Lock _gate = new();
    private readonly Dictionary<ControllerIndexKey, ushort> _controllerIndices = [];
    private List<ClientControllerRouteSource> _sources = [];
    private SdlGamepadSource[] _gamepadSources = [];
    private ushort _nextControllerIndex;

    public IReadOnlyList<ClientControllerRouteSource> Add(IReadOnlyList<SdlGamepadSource> sources)
    {
        if (sources.Count == 0)
        {
            return GetSourcesSnapshot();
        }

        lock (_gate)
        {
            List<ClientControllerRouteSource> entries = [.. _sources];
            foreach (SdlGamepadSource source in sources)
            {
                entries.Add(new ClientControllerRouteSource(GetOrAddControllerIndex(source.Controller), source));
            }

            _sources = entries;
            _gamepadSources = CreateGamepadSnapshot(entries);
            return _sources;
        }
    }

    public bool Remove(SdlGamepadSource source, out ClientControllerRouteSource removed)
    {
        removed = default;
        lock (_gate)
        {
            List<ClientControllerRouteSource> sources = [.. _sources];
            int index = sources.FindIndex(entry => ReferenceEquals(entry.Source, source));
            if (index < 0)
            {
                return false;
            }

            removed = sources[index];
            sources.RemoveAt(index);
            _sources = sources;
            _gamepadSources = CreateGamepadSnapshot(sources);
            return true;
        }
    }

    public HashSet<SdlControllerId> GetOpenSourceIds()
    {
        IReadOnlyList<ClientControllerRouteSource> sources = GetSourcesSnapshot();
        HashSet<SdlControllerId> ids = [];
        foreach (ClientControllerRouteSource source in sources)
        {
            _ = ids.Add(source.Source.Controller.Id);
        }

        return ids;
    }

    public IReadOnlyList<SdlGamepadSource> GetGamepadSourcesSnapshot()
    {
        lock (_gate)
        {
            return _gamepadSources;
        }
    }

    public IReadOnlyList<ClientControllerRouteSource> GetSourcesSnapshot()
    {
        lock (_gate)
        {
            return _sources;
        }
    }

    public bool TryFindSourceIndex(SdlGamepadSource source, out ushort controllerIndex)
    {
        IReadOnlyList<ClientControllerRouteSource> sources = GetSourcesSnapshot();
        for (int i = 0; i < sources.Count; i++)
        {
            if (ReferenceEquals(sources[i].Source, source))
            {
                controllerIndex = sources[i].ControllerIndex;
                return true;
            }
        }

        controllerIndex = 0;
        return false;
    }

    public bool TryGetSource(ushort controllerIndex, out SdlGamepadSource source)
    {
        foreach (ClientControllerRouteSource entry in GetSourcesSnapshot())
        {
            if (entry.ControllerIndex == controllerIndex)
            {
                source = entry.Source;
                return true;
            }
        }

        source = null!;
        return false;
    }

    public IReadOnlyList<ClientControllerRouteSource> RemoveStale(
        IReadOnlyList<SdlControllerInfo> controllers)
    {
        Dictionary<SdlControllerId, SdlControllerInfo> currentControllers = [];
        foreach (SdlControllerInfo controller in controllers)
        {
            currentControllers[controller.Id] = controller;
        }

        lock (_gate)
        {
            if (_sources.Count == 0)
            {
                return [];
            }

            List<ClientControllerRouteSource> retained = [];
            List<ClientControllerRouteSource> removed = [];
            bool hasCurrentControllers = currentControllers.Count != 0;
            foreach (ClientControllerRouteSource source in _sources)
            {
                // Keep routes through empty Steam scans, but do not preserve a
                // stale source when Steam returns another non-empty set. Steam
                // can reshuffle handles while rebuilding virtual devices; stale
                // routes are worse than a brief unregister/reopen.
                if (!currentControllers.TryGetValue(source.Source.Controller.Id, out SdlControllerInfo? current))
                {
                    if (hasCurrentControllers)
                    {
                        removed.Add(source);
                    }
                    else
                    {
                        retained.Add(source);
                    }

                    continue;
                }

                if (SdlControllerRoutePolicy.IsSameConnectedController(source.Source.Controller, current))
                {
                    retained.Add(source);
                }
                else
                {
                    removed.Add(source);
                }
            }

            if (removed.Count == 0)
            {
                return [];
            }

            _sources = retained;
            _gamepadSources = CreateGamepadSnapshot(retained);
            return removed;
        }
    }

    public async Task ClearAsync()
    {
        IReadOnlyList<ClientControllerRouteSource> sources;
        lock (_gate)
        {
            sources = _sources;
            _sources = [];
            _gamepadSources = [];
        }

        foreach (ClientControllerRouteSource source in sources)
        {
            await source.Source.DisposeAsync().ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await ClearAsync().ConfigureAwait(false);
    }

    private ushort GetOrAddControllerIndex(SdlControllerInfo controller)
    {
        ControllerIndexKey key = ControllerIndexKey.Create(controller);
        if (_controllerIndices.TryGetValue(key, out ushort index))
        {
            return index;
        }

        index = _nextControllerIndex++;
        _controllerIndices[key] = index;
        return index;
    }

    private static SdlGamepadSource[] CreateGamepadSnapshot(List<ClientControllerRouteSource> sources)
    {
        if (sources.Count == 0)
        {
            return [];
        }

        SdlGamepadSource[] gamepads = new SdlGamepadSource[sources.Count];
        for (int i = 0; i < sources.Count; i++)
        {
            gamepads[i] = sources[i].Source;
        }

        return gamepads;
    }

    private readonly record struct ControllerIndexKey(
        SdlControllerId Id,
        uint InstanceId,
        SdlControllerSource Source,
        ulong SteamHandle,
        ushort VendorId,
        ushort ProductId,
        string Name,
        string? Path)
    {
        public static ControllerIndexKey Create(SdlControllerInfo controller)
        {
            return new ControllerIndexKey(
                controller.Id,
                controller.InstanceId,
                controller.Source,
                controller.SteamHandle,
                controller.VendorId,
                controller.ProductId,
                controller.Name,
                controller.Path);
        }
    }
}
