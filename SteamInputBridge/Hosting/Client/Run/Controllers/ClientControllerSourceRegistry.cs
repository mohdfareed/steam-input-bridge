using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SteamInputBridge.Inputs.Sdl;

namespace SteamInputBridge.Hosting.Client.Run.Controllers;

internal sealed class ClientControllerSourceRegistry : IAsyncDisposable
{
    private readonly Lock _gate = new();
    private List<ClientControllerRouteSource> _sources = [];
    private SdlGamepadSource[] _gamepadSources = [];
    private Dictionary<SdlGamepadSource, ushort> _sourceIndices = [];
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
                entries.Add(new ClientControllerRouteSource(_nextControllerIndex++, source));
            }

            _sources = entries;
            _gamepadSources = CreateGamepadSnapshot(entries);
            _sourceIndices = CreateSourceIndexSnapshot(entries);
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
            _sourceIndices = CreateSourceIndexSnapshot(sources);
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
        return Volatile.Read(ref _gamepadSources);
    }

    public IReadOnlyList<ClientControllerRouteSource> GetSourcesSnapshot()
    {
        return Volatile.Read(ref _sources);
    }

    public bool TryFindSourceIndex(SdlGamepadSource source, out ushort controllerIndex)
    {
        return Volatile.Read(ref _sourceIndices).TryGetValue(source, out controllerIndex);
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
                if (ClientControllerSourceStaleness.ShouldRemoveOpenedSource(
                    source.Source.Controller,
                    currentControllers))
                {
                    removed.Add(source);
                }
                else
                {
                    retained.Add(source);
                }
            }

            if (removed.Count == 0)
            {
                return [];
            }

            _sources = retained;
            _gamepadSources = CreateGamepadSnapshot(retained);
            _sourceIndices = CreateSourceIndexSnapshot(retained);
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
            _sourceIndices = [];
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

    private static Dictionary<SdlGamepadSource, ushort> CreateSourceIndexSnapshot(
        List<ClientControllerRouteSource> sources)
    {
        if (sources.Count == 0)
        {
            return [];
        }

        Dictionary<SdlGamepadSource, ushort> indices = new(capacity: sources.Count);
        foreach (ClientControllerRouteSource source in sources)
        {
            indices[source.Source] = source.ControllerIndex;
        }

        return indices;
    }
}

internal static class ClientControllerSourceStaleness
{
    public static bool ShouldRemoveOpenedSource(
        SdlControllerInfo opened,
        IReadOnlyDictionary<SdlControllerId, SdlControllerInfo> currentControllers)
    {
        // Steam can temporarily omit already-open streams while rebuilding
        // virtual devices. Missing from a scan is not a removal signal; the
        // SDL event loop owns actual disconnects. The only scan-time stale
        // case we trust is SDL reusing the same stable id for a different
        // controller identity.
        return currentControllers.TryGetValue(opened.Id, out SdlControllerInfo? current) &&
            !SdlControllerRoutePolicy.IsSameConnectedController(opened, current);
    }
}
