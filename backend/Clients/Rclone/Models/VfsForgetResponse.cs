using System.Text.Json.Serialization;

namespace NzbWebDAV.Clients.Rclone.Models;

/// <summary>
/// Response from vfs/forget endpoint.
/// </summary>
public class VfsForgetResponse : RcloneResponse
{
    [JsonPropertyName("forgotten")]
    public List<string>? Forgotten { get; set; }
}
