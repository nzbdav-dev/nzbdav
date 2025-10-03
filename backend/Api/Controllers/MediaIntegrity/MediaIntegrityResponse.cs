using System.Text.Json.Serialization;
using NzbWebDAV.Tasks;

namespace NzbWebDAV.Api.Controllers.MediaIntegrity;

public class MediaIntegrityResponse
{
    [JsonPropertyName("message")]
    public string Message { get; set; } = null!;
    
    [JsonPropertyName("started")]
    public bool Started { get; set; }
}

public class MediaIntegrityRunResponse
{
    [JsonPropertyName("message")]
    public string Message { get; set; } = null!;
    
    [JsonPropertyName("started")]
    public bool Started { get; set; }
    
    [JsonPropertyName("runDetails")]
    public IntegrityRunStatus? RunDetails { get; set; }
}
