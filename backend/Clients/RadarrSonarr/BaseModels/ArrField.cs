using System.Text.Json;
using System.Text.Json.Serialization;

namespace NzbWebDAV.Clients.RadarrSonarr.BaseModels;

public class ArrField
{
    [JsonPropertyName("name")]
    public string Name { get; set; }

    [JsonPropertyName("label")]
    public string Label { get; set; }

    [JsonPropertyName("value")]
    public JsonElement? ValueJson { get; set; }

    public object? Value => ValueJson?.ValueKind == JsonValueKind.Null ? null
        : ValueJson?.ValueKind == JsonValueKind.String ? ValueJson?.ToString()
        : ValueJson?.ValueKind == JsonValueKind.Number ? ValueJson?.GetInt64()
        : ValueJson?.ValueKind == JsonValueKind.True ? true
        : ValueJson?.ValueKind == JsonValueKind.False ? false
        : ValueJson?.GetRawText();
}