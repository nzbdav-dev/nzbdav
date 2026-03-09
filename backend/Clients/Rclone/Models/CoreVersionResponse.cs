using System.Text.Json.Serialization;

namespace NzbWebDAV.Clients.Rclone.Models;

/// <summary>
/// Response from core/version endpoint.
/// </summary>
public class CoreVersionResponse : RcloneResponse
{
    [JsonPropertyName("version")]
    public string? Version { get; set; }

    [JsonPropertyName("decomposed")]
    public List<int>? Decomposed { get; set; }

    [JsonPropertyName("isGit")]
    public bool IsGit { get; set; }

    [JsonPropertyName("isBeta")]
    public bool IsBeta { get; set; }

    [JsonPropertyName("os")]
    public string? Os { get; set; }

    [JsonPropertyName("arch")]
    public string? Arch { get; set; }

    [JsonPropertyName("goVersion")]
    public string? GoVersion { get; set; }

    [JsonPropertyName("linking")]
    public string? Linking { get; set; }

    [JsonPropertyName("goTags")]
    public string? GoTags { get; set; }
}
