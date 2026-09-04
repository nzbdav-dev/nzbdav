using System.Text.Json.Serialization;

namespace NzbWebDAV.Clients.RadarrSonarr.LidarrModels;

public class LidarrTrackFile
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("artistId")]
    public int ArtistId { get; set; }

    [JsonPropertyName("path")]
    public string? Path { get; set; }
}
