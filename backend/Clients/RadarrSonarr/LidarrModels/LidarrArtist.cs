using System.Text.Json.Serialization;

namespace NzbWebDAV.Clients.RadarrSonarr.LidarrModels;

public class LidarrArtist
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("artistName")]
    public string? ArtistName { get; set; }

    [JsonPropertyName("path")]
    public string? Path { get; set; }
}
