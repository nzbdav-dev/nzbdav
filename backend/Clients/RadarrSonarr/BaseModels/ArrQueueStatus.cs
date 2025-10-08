using System.Text.Json.Serialization;

namespace NzbWebDAV.Clients.RadarrSonarr.BaseModels;

public class ArrQueueStatus
{
    [JsonPropertyName("totalCount")]
    public int TotalCount { get; set; }

    [JsonPropertyName("count")]
    public int Count { get; set; }

    [JsonPropertyName("unknownCount")]
    public int UnknownCount { get; set; }

    [JsonPropertyName("errors")]
    public bool Errors { get; set; }

    [JsonPropertyName("warnings")]
    public bool Warnings { get; set; }

    [JsonPropertyName("unknownErrors")]
    public bool UnknownErrors { get; set; }

    [JsonPropertyName("unknownWarnings")]
    public bool UnknownWarnings { get; set; }
}