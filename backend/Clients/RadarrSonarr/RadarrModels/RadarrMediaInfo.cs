using System.Text.Json.Serialization;

namespace NzbWebDAV.Clients.RadarrSonarr.RadarrModels;

public class RadarrMediaInfo
{
    [JsonPropertyName("resolution")]
    public string? Resolution { get; set; }

    [JsonPropertyName("runTime")]
    public string? Runtime { get; set; }
}