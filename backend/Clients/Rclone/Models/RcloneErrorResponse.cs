using System.Text.Json.Serialization;

namespace NzbWebDAV.Clients.Rclone.Models;

/// <summary>
/// Error response returned by rclone when a request fails.
/// </summary>
public class RcloneErrorResponse
{
    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("input")]
    public object? Input { get; set; }

    [JsonPropertyName("status")]
    public int Status { get; set; }

    [JsonPropertyName("path")]
    public string? Path { get; set; }
}
