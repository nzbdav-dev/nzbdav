using System.Text.Json.Serialization;

namespace NzbWebDAV.Clients.RadarrSonarr.RadarrModels;

public class RadarrMovie
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("movieFile")]
    public RadarrMovieFile? MovieFile { get; set; }
}