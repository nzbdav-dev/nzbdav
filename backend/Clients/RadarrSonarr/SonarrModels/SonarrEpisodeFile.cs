using System.Text.Json.Serialization;

namespace NzbWebDAV.Clients.RadarrSonarr.SonarrModels;

public class SonarrEpisodeFile
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("seriesId")]
    public int SeriesId { get; set; }

    [JsonPropertyName("path")]
    public string? Path { get; set; }
}