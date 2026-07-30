namespace NzbWebDAV.Database.Models.UsenetMigration;

/// <summary>
/// Singleton wizard session row (Id is pinned to 1). Captures the connection
/// inputs, tunables, and global settings sampled during Scan that Run must
/// re-check before proceeding.
/// </summary>
public sealed class MigrationSessionState
{
    public int Id { get; set; } = 1;

    /// <summary>
    /// See MigrationSessionStatus for the canonical state values and
    /// MigrationSessionStateMachine for legal transitions.
    /// </summary>
    public string Status { get; set; } = "idle";

    public string? AltmountMetadataRoot { get; set; }
    public string? AltmountConfigPath { get; set; }
    public string? AltmountStoreRoot { get; set; }

    // Step 6 symlink-continuity inputs are opt-in and disabled by default.
    // Only populated once the user explicitly enters the symlink phase against a
    // completed migration.

    /// <summary>Root of the arr/Plex library whose symlinks point at Altmount.</summary>
    public string? SymlinkLibraryRoot { get; set; }

    /// <summary>Where the wizard writes the restore tarball before rewriting.</summary>
    public string? SymlinkBackupDir { get; set; }

    public int MaxQueueDepth { get; set; } = 20;

    /// <summary>
    /// Defaults to 1 because concurrent submissions sharing an NzbDAV queue key
    /// can evict one another mid-download.
    /// </summary>
    public int SubmitWorkers { get; set; } = 1;

    // Globals captured at Scan; Run returns to Review if either changes.
    public bool? ScanLazyRarEnabled { get; set; }
    public bool? ScanWindowsSafePaths { get; set; }

    public DateTime? ScanStartedAt { get; set; }
    public DateTime? ScanCompletedAt { get; set; }
    public DateTime? RunStartedAt { get; set; }
    public DateTime? RunCompletedAt { get; set; }
    public long? CurrentRunId { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// Singleton user preferences row (Id is pinned to 1). Unlike session state,
/// these values survive wizard and provenance resets so future runs can reuse
/// the paths and queue settings entered in Steps 1 and 6.
/// </summary>
public sealed class MigrationPreferences
{
    public int Id { get; set; } = 1;

    public string? AltmountMetadataRoot { get; set; }
    public string? AltmountConfigPath { get; set; }
    public string? AltmountStoreRoot { get; set; }
    public int MaxQueueDepth { get; set; } = 20;
    public int SubmitWorkers { get; set; } = 1;
    public string? SymlinkLibraryRoot { get; set; }
    public string? SymlinkBackupDir { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>Maps an Altmount category to a NzbDAV target category.</summary>
public sealed class MigrationCategoryMap
{
    /// <summary>config.yaml Name; "" for uncategorised stores.</summary>
    public string AltmountCategory { get; set; } = "";

    public string? AltmountDir { get; set; }
    public string? AltmountSanitizedDir { get; set; }

    /// <summary>sonarr|radarr|""</summary>
    public string? AltmountType { get; set; }

    /// <summary>NULL = unmapped.</summary>
    public string? TargetCategory { get; set; }

    /// <summary>migrate|exclude</summary>
    public string Action { get; set; } = "migrate";

    /// <summary>config|scan, where scan means the category was missing from config.</summary>
    public string DiscoveredBy { get; set; } = "config";

    public DateTime UpdatedAt { get; set; }
}

/// <summary>One release equals one <c>.nzbz</c> store. StoreRef is the identity key.</summary>
public sealed class MigrationRelease
{
    /// <summary>Absolute .nzbz path == release identity. For v1 files with no store, a synthetic "v1:{metaPath}" key.</summary>
    public string StoreRef { get; set; } = "";

    public string StoreBasename { get; set; } = "";

    /// <summary>Original queue id used to diagnose collisions; null when no prefix exists.</summary>
    public long? QueueId { get; set; }

    /// <summary>{nzbBasename}; goes on the wire as nzbname.</summary>
    public string SubmitFileName { get; set; } = "";

    /// <summary>ResolveFileName(SubmitFileName); the filename half of the unique queue key.</summary>
    public string QueueFileName { get; set; } = "";

    /// <summary>GetJobName(QueueFileName); predicted mount folder.</summary>
    public string JobName { get; set; } = "";

    public bool JobNameDiverges { get; set; }

    public string? AltmountCategory { get; set; }
    public string? TargetCategory { get; set; }

    /// <summary>green|amber|red</summary>
    public string Verdict { get; set; } = "green";

    /// <summary>JSON array of machine-readable verdict reason codes.</summary>
    public string VerdictReasons { get; set; } = "[]";

    public string? VerdictDetail { get; set; }

    /// <summary><c>{TargetCategory}\0{JobName}</c> groups releases that share a predicted mount folder.</summary>
    public string? CollisionGroupKey { get; set; }

    public int MetaFileCount { get; set; }
    public long? TotalBytes { get; set; }
    public int NzbFileCount { get; set; }
    public int SegmentCount { get; set; }

    /// <summary>Σ file.Segments[0].Bytes; the near-exact lazy-fetch estimate.</summary>
    public long EstFetchBytesLazy { get; set; }

    /// <summary>Lazy estimate + Σ rar file.Segments[^1].Bytes; a lower bound.</summary>
    public long EstFetchBytesEager { get; set; }

    public bool IsRarRelease { get; set; }
    public long EstStatCommands { get; set; }

    public DateTime? ReleaseDate { get; set; }
    public string? Encryption { get; set; }
    public bool HasPassword { get; set; }

    /// <summary>Whether PasswordRegex matches the submit filename.</summary>
    public bool HasFilenamePassword { get; set; }

    public string? WorstFileStatus { get; set; }
    public bool HasNestedSources { get; set; }
    public bool HasClipBoundaries { get; set; }

    // NOTE: no HasKnownHoles column — Altmount has no known_holes field.

    /// <summary>Source-side NzbDAV id when available; null because sidecar .id files are not read.</summary>
    public string? SourceNzbdavId { get; set; }

    public bool Included { get; set; } = true;
    public DateTime ScannedAt { get; set; }
}

/// <summary>One virtual file within a release, persisted for reporting and symlink matching.</summary>
public sealed class MigrationReleaseFile
{
    public long Id { get; set; }
    public string StoreRef { get; set; } = "";
    public string MetaPath { get; set; } = "";

    /// <summary>Altmount mount path == old symlink target (Step 6 input).</summary>
    public string VirtualPath { get; set; } = "";

    public string FileName { get; set; } = "";
    public string NormalisedName { get; set; } = "";
    public long? FileSize { get; set; }
    public string? FileStatus { get; set; }
    public string? NzbdavId { get; set; }

    /// <summary>Populated only when symlink planning matches this file to a live DavItem.</summary>
    public string? NewDavItemId { get; set; }

    /// <summary>json: nested|clips|encrypted</summary>
    public string? Flags { get; set; }
}

/// <summary>Submission and import lifecycle state for a release.</summary>
public sealed class MigrationSubmission
{
    public string StoreRef { get; set; } = "";
    public string? NzoId { get; set; }

    /// <summary>
    /// pending|submitting|submitted|processing|completed|history_cleared|failed|skipped|evicted.
    /// New successful runs remain completed; history_cleared is retained for
    /// compatibility with older migration sessions. 'evicted' is terminal.
    /// </summary>
    public string State { get; set; } = "pending";

    public int Attempt { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? HistoryClearedAt { get; set; }

    /// <summary>Resulting /content/{cat}/{JobName}.</summary>
    public string? MountPath { get; set; }

    public int? DavItemCount { get; set; }
    public string? Error { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>One execution of a Usenet-source migration.</summary>
public sealed class MigrationRun
{
    public long Id { get; set; }
    public string SourceType { get; set; } = "altmount";
    public string Status { get; set; } = "running";
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}

/// <summary>
/// Durable identity and outcome of a successfully migrated source release.
/// Unlike <see cref="MigrationRelease"/>, this row survives wizard resets and
/// subsequent scans.
/// </summary>
public sealed class MigratedRelease
{
    public long Id { get; set; }
    public string SourceType { get; set; } = "altmount";
    public string SourceReleaseId { get; set; } = "";
    public long FirstRunId { get; set; }
    public long LastRunId { get; set; }
    public string? NzoId { get; set; }
    public string? TargetCategory { get; set; }
    public string? JobName { get; set; }
    public string? MountPath { get; set; }
    public int ExpectedFileCount { get; set; }
    public int MappedFileCount { get; set; }
    public DateTime MigratedAt { get; set; }
    public DateTime LastVerifiedAt { get; set; }
}

/// <summary>
/// Durable mapping from an original source virtual path to the mounted NZBDav
/// item created for it.
/// </summary>
public sealed class MigratedFile
{
    public long Id { get; set; }
    public long MigratedReleaseId { get; set; }
    public string VirtualPath { get; set; } = "";
    public string NormalisedRelativePath { get; set; } = "";
    public string NormalisedName { get; set; } = "";
    public long? FileSize { get; set; }
    public Guid DavItemId { get; set; }
    public Guid? NzbBlobId { get; set; }
    public string MatchMethod { get; set; } = "";
    public DateTime LastVerifiedAt { get; set; }
}

/// <summary>
/// One planned or applied symlink rewrite for Step 6.
/// The plan is built dry-run (Status in rewrite|already-nzbdav|not-altmount|orphan),
/// then apply flips matched rows to applied|failed. The wizard only ever retargets;
/// it never deletes links or their targets.
/// </summary>
public sealed class MigrationSymlinkRewrite
{
    public long Id { get; set; }

    /// <summary>Absolute path of the symlink in the arr/Plex library.</summary>
    public string SymlinkPath { get; set; } = "";

    /// <summary>Observed link target; empty when the target could not be read.</summary>
    public string OldTarget { get; set; } = "";

    /// <summary>Computed .ids/…/&lt;guid&gt; target; null until matched.</summary>
    public string? NewTarget { get; set; }

    /// <summary>rewrite|already-nzbdav|not-altmount|orphan|unreadable|applied|failed.</summary>
    public string Status { get; set; } = "";

    /// <summary>relative-path|exact|unique-size|single-leaf-fallback; null when unmatched.</summary>
    public string? MatchMethod { get; set; }

    /// <summary>The release this symlink resolved to, when matched. Forensics only.</summary>
    public string? StoreRef { get; set; }

    public string? Error { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>A non-fatal error encountered during the scan.</summary>
public sealed class MigrationScanError
{
    public long Id { get; set; }
    public string? Path { get; set; }
    public string? Kind { get; set; }
    public string? Message { get; set; }
    public DateTime OccurredAt { get; set; }
}
