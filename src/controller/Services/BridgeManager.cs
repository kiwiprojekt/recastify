using System.Collections.Concurrent;
using Recastify.Models;

namespace Recastify.Services;

public class BridgeManager
{
    private readonly ConcurrentDictionary<string, Bridge> _bridges = new();
    private readonly string _icecastHost;
    private readonly int _icecastPort;

    public BridgeManager(string icecastHost, int icecastPort)
    {
        _icecastHost = icecastHost;
        _icecastPort = icecastPort;
    }

    public Bridge GetOrCreate(string id)
    {
        return _bridges.GetOrAdd(id, key => new Bridge
        {
            Id = key,
            Name = key,
            Mount = "/" + key,
            StreamUrl = $"http://{_icecastHost}:{_icecastPort}/{key}",
            State = "offline"
        });
    }

    public Bridge? Get(string id)
    {
        _bridges.TryGetValue(id, out var bridge);
        return bridge;
    }

    public List<Bridge> GetAll()
    {
        return _bridges.Values.ToList();
    }

    public void UpdateState(string bridgeId, string state)
    {
        var bridge = GetOrCreate(bridgeId);
        bridge.State = state;
        bridge.LastStateChange = DateTime.UtcNow;
    }

    public bool Remove(string id)
    {
        return _bridges.TryRemove(id, out _);
    }

    public void EnsureDefault(string name, string mount, string bitrate)
    {
        var bridge = GetOrCreate("default");
        bridge.Name = name;
        bridge.Mount = mount;
        bridge.StreamUrl = $"http://{_icecastHost}:{_icecastPort}{mount}";
        bridge.Bitrate = bitrate;
    }
}
