using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SteamInputBridge.Inputs.Sdl;

namespace SteamInputBridge.Hosting.Client.Run.Controllers;

internal sealed class ClientControllerSourceRegistry : IAsyncDisposable
{
    private readonly Lock _gate = new();
    private readonly Dictionary<SdlControllerId, ushort> _controllerIndices = [];
    private List<ClientControllerRouteSource> _sources = [];
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
                entries.Add(new ClientControllerRouteSource(GetOrAddControllerIndex(source.Controller.Id), source));
            }

            _sources = entries;
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
        IReadOnlyList<ClientControllerRouteSource> sources = GetSourcesSnapshot();
        SdlGamepadSource[] gamepads = new SdlGamepadSource[sources.Count];
        for (int i = 0; i < sources.Count; i++)
        {
            gamepads[i] = sources[i].Source;
        }

        return gamepads;
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
            foreach (ClientControllerRouteSource source in _sources)
            {
                if (IsCurrent(source.Source.Controller, currentControllers))
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

    private ushort GetOrAddControllerIndex(SdlControllerId controllerId)
    {
        if (_controllerIndices.TryGetValue(controllerId, out ushort index))
        {
            return index;
        }

        index = _nextControllerIndex++;
        _controllerIndices[controllerId] = index;
        return index;
    }

    private static bool IsCurrent(
        SdlControllerInfo source,
        Dictionary<SdlControllerId, SdlControllerInfo> currentControllers)
    {
        return currentControllers.TryGetValue(source.Id, out SdlControllerInfo? current) &&
            current.InstanceId == source.InstanceId &&
            current.Source == source.Source &&
            current.SteamHandle == source.SteamHandle &&
            string.Equals(current.Path, source.Path, StringComparison.OrdinalIgnoreCase);
    }
}
