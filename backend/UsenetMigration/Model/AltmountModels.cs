namespace NzbWebDAV.UsenetMigration.Model;

/// <summary>
/// Health status of an Altmount virtual file.
/// Mirrors Altmount's <c>FileStatus</c> proto enum. Only these three values are
/// defined; Altmount does not expose a separate DEGRADED status.
/// </summary>
public enum AltmountFileStatus
{
    Unspecified = 0,
    Healthy = 1,
    Corrupted = 3,
}

/// <summary>
/// Encryption type recorded on an Altmount virtual file.
/// Mirrors Altmount's <c>Encryption</c> proto enum.
/// </summary>
public enum AltmountEncryption
{
    None = 0,
    Rclone = 1,
    Headers = 2,
    Aes = 3,
}

/// <summary>
/// The subset of Altmount's <c>FileMetadata</c> proto (one per virtual file,
/// stored in a <c>.meta</c> file) that the migration consumes. Fields the
/// migration does not use are skipped by the decoder.
/// </summary>
public sealed class AltmountFileMetadata
{
    /// <summary>True when the .meta carried the v3 magic prefix.</summary>
    public bool IsV3 { get; init; }

    /// <summary>proto field 1.</summary>
    public long FileSize { get; init; }

    /// <summary>proto field 3.</summary>
    public AltmountFileStatus Status { get; init; }

    /// <summary>proto field 6.</summary>
    public string Password { get; init; } = "";

    /// <summary>proto field 7.</summary>
    public string Salt { get; init; } = "";

    /// <summary>proto field 8.</summary>
    public AltmountEncryption Encryption { get; init; }

    /// <summary>proto field 12 (unix seconds).</summary>
    public long ReleaseDate { get; init; }

    /// <summary>
    /// proto field 18 — path to the release's shared <c>.nzbz</c> store, and the
    /// release identity key. Empty values identify v1 metadata that cannot be migrated.
    /// </summary>
    public string StoreRef { get; init; } = "";

    /// <summary>True when proto field 15 contains at least one nested source.</summary>
    public bool HasNestedSources { get; init; }

    /// <summary>True when proto field 17 contains at least one clip boundary.</summary>
    public bool HasClipBoundaries { get; init; }

    // nzbdav_id (proto field 14) is not read because Altmount clears it before
    // marshalling and stores it in a sidecar `.id` file, leaving the proto field empty.
}

/// <summary>
/// A single &lt;file&gt; entry inside an NZB store, decoded from Altmount's
/// <c>NzbStore</c> proto.
/// </summary>
public sealed class NzbFileEntry
{
    public string Subject { get; init; } = "";
    public string Poster { get; init; } = "";
    public long Date { get; init; }
    public List<string> Groups { get; init; } = new();
    public List<NzbSeg> Segments { get; init; } = new();
}

/// <summary>One NZB segment: message-id, 1-based number, and yEnc wire bytes.</summary>
public sealed class NzbSeg
{
    public string Id { get; init; } = "";
    public int Number { get; init; }

    /// <summary>The NZB <c>bytes</c> attribute — the yEnc-encoded (on-the-wire) size.</summary>
    public long Bytes { get; init; }
}

/// <summary>
/// The complete original NZB for a release, decoded from a <c>.nzbz</c>
/// (zstd(proto)) store. Each store represents one release.
/// </summary>
public sealed class NzbStore
{
    public List<NzbFileEntry> Files { get; init; } = new();
}

/// <summary>
/// One SABnzbd category from Altmount's <c>config.yaml</c> (<c>sabnzbd.categories</c>).
/// </summary>
public sealed class AltmountCategory
{
    public string Name { get; init; } = "";
    public int Order { get; init; }
    public int Priority { get; init; }
    public string Dir { get; init; } = "";

    /// <summary>"sonarr" | "radarr" | "" — semantic intent used for target suggestions.</summary>
    public string Type { get; init; } = "";
}
