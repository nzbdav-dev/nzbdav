using System.Text.Json.Serialization;

namespace NzbWebDAV.Clients.Rclone.Models;

/// <summary>
/// Response from vfs/stats endpoint.
/// </summary>
public class VfsStatsResponse : RcloneResponse
{
    [JsonPropertyName("diskCache")]
    public VfsDiskCache? DiskCache { get; set; }

    [JsonPropertyName("metadataCache")]
    public VfsMetadataCache? MetadataCache { get; set; }

    [JsonPropertyName("opt")]
    public VfsOptions? Options { get; set; }
}

public class VfsDiskCache
{
    [JsonPropertyName("bytesUsed")]
    public long BytesUsed { get; set; }

    [JsonPropertyName("erroredFiles")]
    public int ErroredFiles { get; set; }

    [JsonPropertyName("hashType")]
    public int HashType { get; set; }

    [JsonPropertyName("outOfSpace")]
    public bool OutOfSpace { get; set; }

    [JsonPropertyName("uploadsInProgress")]
    public int UploadsInProgress { get; set; }

    [JsonPropertyName("uploadsQueued")]
    public int UploadsQueued { get; set; }
}

public class VfsMetadataCache
{
    [JsonPropertyName("dirs")]
    public int Dirs { get; set; }

    [JsonPropertyName("files")]
    public int Files { get; set; }
}

public class VfsOptions
{
    [JsonPropertyName("CacheMode")]
    public string? CacheMode { get; set; }

    [JsonPropertyName("CacheMaxAge")]
    public long CacheMaxAge { get; set; }

    [JsonPropertyName("CachePollInterval")]
    public long CachePollInterval { get; set; }

    [JsonPropertyName("ChunkSize")]
    public long ChunkSize { get; set; }

    [JsonPropertyName("ChunkSizeLimit")]
    public long ChunkSizeLimit { get; set; }

    [JsonPropertyName("DirCacheTime")]
    public long DirCacheTime { get; set; }

    [JsonPropertyName("ReadAhead")]
    public long ReadAhead { get; set; }

    [JsonPropertyName("ReadOnly")]
    public bool ReadOnly { get; set; }
}
