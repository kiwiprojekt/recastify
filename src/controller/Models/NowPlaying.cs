namespace Recastify.Models;

public class NowPlaying
{
    public string Title { get; set; } = "";
    public string Artist { get; set; } = "";
    public string Album { get; set; } = "";
    public string? AlbumArtist { get; set; }
    public string? Genre { get; set; }
    public string? ArtworkUrl { get; set; }
    public long ElapsedMs { get; set; }
    public long DurationMs { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    [System.Text.Json.Serialization.JsonIgnore]
    public string? DacpId { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    public string? ActiveRemote { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    public string? DacpHost { get; set; }
    public bool HasRemoteControl => DacpId != null && ActiveRemote != null && DacpHost != null;
}
