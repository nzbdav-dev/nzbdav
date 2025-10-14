using System.Text.Json.Serialization;

namespace NzbWebDAV.Clients.RadarrSonarr.BaseModels;

public class ArrRootFolder
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("path")]
    public string? Path { get; set; }

    [JsonPropertyName("accessible")]
    public bool Accessible { get; set; }

    [JsonPropertyName("freeSpace")]
    public long FreeSpace { get; set; }
}