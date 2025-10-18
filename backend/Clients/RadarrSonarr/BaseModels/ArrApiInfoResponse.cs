using System.Text.Json.Serialization;

namespace NzbWebDAV.Clients.RadarrSonarr.BaseModels;

public class ArrApiInfoResponse
{
    [JsonPropertyName("current")]
    public string? Current { get; set; }
}