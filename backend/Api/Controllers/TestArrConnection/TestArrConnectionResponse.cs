using System.Text.Json.Serialization;

namespace NzbWebDAV.Api.Controllers.TestArrConnection;

public class TestArrConnectionResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("configuredInstances")]
    public List<string> ConfiguredInstances { get; set; } = new();
}
