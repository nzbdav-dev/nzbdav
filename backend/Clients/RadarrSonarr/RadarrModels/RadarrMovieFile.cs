using System.Text.Json.Serialization;

namespace NzbWebDAV.Clients.RadarrSonarr.RadarrModels;

public class RadarrMovieFile
{
    [JsonPropertyName("path")]
    public string? Path { get; set; }

    [JsonPropertyName("mediaInfo")]
    public RadarrMediaInfo? MediaInfo { get; set; }
}