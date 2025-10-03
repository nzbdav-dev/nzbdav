using System.Text.Json.Serialization;
using NzbWebDAV.Clients;

namespace NzbWebDAV.Api.Controllers.TestArrConnection;

public class TestSingleConnectionRequest
{
    [JsonPropertyName("appType")]
    public ArrAppType AppType { get; set; }

    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("apiKey")]
    public string ApiKey { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string? Name { get; set; }
}
