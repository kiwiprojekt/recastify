using System.Text.Json;
using System.Text.Json.Serialization;
using Recastify.Models;

namespace Recastify.Services;

public class IcecastPoller : BackgroundService
{
    private readonly BridgeManager _bridges;
    private readonly string _icecastUrl;
    private readonly HttpClient _http;
    private readonly TimeSpan _interval = TimeSpan.FromSeconds(5);

    public IcecastPoller(BridgeManager bridges, string icecastHost, int icecastPort)
    {
        _bridges = bridges;
        _icecastUrl = $"http://{icecastHost}:{icecastPort}/status-json.xsl";
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PollIcecastAsync(stoppingToken);
            }
            catch
            {
                // Icecast unreachable — ignore, will retry next interval
            }

            await Task.Delay(_interval, stoppingToken);
        }
    }

    private async Task PollIcecastAsync(CancellationToken ct)
    {
        var response = await _http.GetAsync(_icecastUrl, ct);
        if (!response.IsSuccessStatusCode)
            return;

        var json = await response.Content.ReadAsStringAsync(ct);
        var status = JsonSerializer.Deserialize(json, IcecastJsonContext.Default.IcecastStatus);
        if (status?.Icestats == null)
            return;

        var activeMounts = new HashSet<string>();

        // Icecast returns "source" as an object (1 source) or array (multiple).
        // Use JsonElement to handle both cases.
        if (status.Icestats.Source is { } sourceElement)
        {
            var sources = new List<IcecastSource>();
            if (sourceElement.ValueKind == System.Text.Json.JsonValueKind.Array)
                sources = sourceElement.Deserialize(IcecastJsonContext.Default.ListIcecastSource) ?? sources;
            else if (sourceElement.ValueKind == System.Text.Json.JsonValueKind.Object)
            {
                var s = sourceElement.Deserialize(IcecastJsonContext.Default.IcecastSource);
                if (s != null) sources.Add(s);
            }

            foreach (var source in sources)
            {
                // listenurl is the actual mount URL e.g. "http://localhost:8000/stream"
                // server_url is ICY metadata set by the source — often empty
                if (string.IsNullOrEmpty(source.ListenUrl))
                    continue;

                if (!Uri.TryCreate(source.ListenUrl, UriKind.Absolute, out var uri))
                    continue;

                var mountPath = uri.AbsolutePath;
                activeMounts.Add(mountPath);

                foreach (var bridge in _bridges.GetAll())
                {
                    if (bridge.Mount == mountPath)
                        bridge.Listeners = source.Listeners;
                }
            }
        }

        // Cross-reference: if hook says "playing" but mount doesn't exist, mark as starting.
        // If hook says "paused" and mount is gone, that's correct.
        // If no hook and no mount, offline.
        foreach (var bridge in _bridges.GetAll())
        {
            var mountExists = activeMounts.Contains(bridge.Mount);

            if (bridge.State == "playing" && !mountExists)
            {
                // Hook says playing but ffmpeg hasn't connected yet
                bridge.State = "starting";
            }
            else if (bridge.State == "starting" && mountExists)
            {
                bridge.State = "playing";
            }
            else if (bridge.State == "paused" && mountExists)
            {
                // Mount still exists after pause — could be source timeout lag
                // Keep as paused, it will drop soon
            }
            else if (!mountExists && bridge.State != "paused" && bridge.State != "playing" && bridge.State != "starting")
            {
                bridge.State = "offline";
                bridge.Listeners = 0;
            }
        }
    }
}

// Icecast status JSON models
public class IcecastStatus
{
    [JsonPropertyName("icestats")]
    public IcecastIcestats? Icestats { get; set; }
}

public class IcecastIcestats
{
    [JsonPropertyName("source")]
    public System.Text.Json.JsonElement? Source { get; set; }
}

public class IcecastSource
{
    // listenurl = actual stream URL (e.g. "http://hostname:8000/stream")
    [JsonPropertyName("listenurl")]
    public string? ListenUrl { get; set; }

    [JsonPropertyName("listeners")]
    public int Listeners { get; set; }

    [JsonPropertyName("server_name")]
    public string? ServerName { get; set; }
}

[JsonSerializable(typeof(IcecastStatus))]
[JsonSerializable(typeof(IcecastSource))]
[JsonSerializable(typeof(List<IcecastSource>))]
public partial class IcecastJsonContext : JsonSerializerContext { }
