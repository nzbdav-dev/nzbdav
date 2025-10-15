using System.Text.Json.Serialization;

namespace NzbWebDAV.Clients.RadarrSonarr.SonarrModels;

public class SonarrEpisode
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("seriesId")]
    public int SeriesId { get; set; }

    [JsonPropertyName("seasonNumber")]
    public int SeasonNumber { get; set; }

    [JsonPropertyName("episodeNumber")]
    public int EpisodeNumber { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }
}