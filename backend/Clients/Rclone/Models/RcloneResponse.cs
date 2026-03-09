using System.Text.Json.Serialization;

namespace NzbWebDAV.Clients.Rclone.Models;

/// <summary>
/// Base response class for all rclone RC API responses.
/// </summary>
public class RcloneResponse
{
    /// <summary>
    /// Indicates if the request was successful.
    /// This is set by the client based on HTTP status, not returned by rclone.
    /// </summary>
    [JsonIgnore]
    public bool Success { get; set; }

    /// <summary>
    /// Error message if the request failed.
    /// </summary>
    [JsonIgnore]
    public string? Error { get; set; }
}
