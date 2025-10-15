using System.Text.Json.Serialization;

namespace NzbWebDAV.Clients.RadarrSonarr.SonarrModels;

public class SonarrSeries
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("path")]
    public string? Path { get; set; }
}