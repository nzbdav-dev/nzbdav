using System.Text.Json.Serialization;

namespace NzbWebDAV.Clients.RadarrSonarr.BaseModels;

public class ArrDownloadClient
{
    [JsonPropertyName("enable")]
    public bool Enable { get; set; }

    [JsonPropertyName("protocol")]
    public string Protocol { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; }

    [JsonPropertyName("fields")]
    public List<ArrField> Fields { get; set; }

    public string? Category => (string?)Fields
        .FirstOrDefault(x => x.Name is "movieCategory" or "tvCategory")
        ?.Value;
}