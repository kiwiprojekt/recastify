using System.Text.Json.Serialization;

namespace Recastify.Models;

public class Bridge
{
    public string Id { get; set; } = "default";
    public string Name { get; set; } = "Recastify";
    public string Mount { get; set; } = "/stream";
    public string? StreamUrl { get; set; }
    public string State { get; set; } = "offline";
    public int Listeners { get; set; }
    public string? Ip { get; set; }
    public string Bitrate { get; set; } = "320k";
    public bool Enabled { get; set; } = true;
    public DateTime? LastStateChange { get; set; }
    public NowPlaying NowPlaying { get; set; } = new();

    [JsonIgnore]
    public byte[]? CoverArt { get; set; }
}

[JsonSerializable(typeof(Bridge))]
[JsonSerializable(typeof(List<Bridge>))]
[JsonSerializable(typeof(BridgesResponse))]
[JsonSerializable(typeof(StatusHookRequest))]
[JsonSerializable(typeof(HealthResponse))]
[JsonSerializable(typeof(NowPlaying))]
[JsonSerializable(typeof(BridgeCreateRequest))]
[JsonSerializable(typeof(AppConfigUpdateRequest))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
public partial class AppJsonContext : JsonSerializerContext { }

public class BridgesResponse
{
    public List<Bridge> Bridges { get; set; } = new();
    public bool DisableStreamProxy { get; set; }
}

public class StatusHookRequest
{
    public string Bridge { get; set; } = "";
    public string State { get; set; } = "";
}

public class HealthResponse
{
    public string Status { get; set; } = "ok";
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

public class BridgeCreateRequest
{
    public string Name { get; set; } = "";
    public string Mount { get; set; } = "";
    public string? Ip { get; set; }
    public string? Bitrate { get; set; }
    public bool? Enabled { get; set; }
}

public class AppConfigUpdateRequest
{
    public bool DisableStreamProxy { get; set; }
}
