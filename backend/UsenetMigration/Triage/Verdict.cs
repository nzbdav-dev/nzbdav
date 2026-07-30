namespace NzbWebDAV.UsenetMigration.Triage;

/// <summary>Triage severity for a release. Ordered Green &lt; Amber &lt; Red.</summary>
public enum Verdict
{
    Green = 0,
    Amber = 1,
    Red = 2,
}

/// <summary>
/// Machine-readable verdict reason codes. A release can carry multiple reasons;
/// its verdict is the maximum severity across them. Human-readable detail is
/// stored separately.
/// </summary>
public static class VerdictReason
{
    /// <summary>Every expected source file already has a unique live NzbDAV match.</summary>
    public const string AlreadyMigrated = "already_migrated";

    // --- Red: excluded by default ------------------------------------------

    /// <summary>v1 Altmount metadata — no shared store, so it cannot be losslessly migrated.</summary>
    public const string NoStoreRef = "no_store_ref";

    /// <summary><c>store_ref</c> set but the <c>.nzbz</c> was not found.</summary>
    public const string StoreMissing = "store_missing";

    /// <summary>zstd or protobuf decode failure on the <c>.nzbz</c>.</summary>
    public const string StoreCorrupt = "store_corrupt";

    /// <summary>Store decoded but has no files, or no file has segments.</summary>
    public const string StoreEmpty = "store_empty";

    /// <summary>Every file is <c>CORRUPTED</c> — Altmount already knows it's dead.</summary>
    public const string StatusCorrupted = "status_corrupted";

    /// <summary>Store-path category has no mapping row, or store is uncategorised. Blocks Review.</summary>
    public const string CategoryUnmapped = "category_unmapped";

    /// <summary>≥2 included stores share <c>(TargetCategory, QueueFileName)</c>, so submitting would silently evict one. Blocks Review.</summary>
    public const string QueueKeyCollision = "queue_key_collision";

    /// <summary>Would evict a live, non-migration queue item. Blocks Review.</summary>
    public const string CollidesWithExistingQueueItem = "collides_with_existing_queue_item";

    // --- Amber: will attempt; outcome may differ from Altmount --------------

    /// <summary>
    /// Some (but not all) files are <c>CORRUPTED</c>. Altmount has no separate
    /// DEGRADED status, so partial corruption is derived from the file counts.
    /// </summary>
    public const string SomeFilesCorrupted = "some_files_corrupted";

    /// <summary>Inner RAR within outer RAR — NzbDAV's multipart model is single-level.</summary>
    public const string NestedSources = "nested_sources";

    /// <summary>Multi-clip Blu-ray PTS rebasing — NzbDAV has no clip timeline.</summary>
    public const string ClipBoundaries = "clip_boundaries";

    /// <summary><c>encryption != NONE</c> — depends on the head injection surviving.</summary>
    public const string Encrypted = "encrypted";

    /// <summary>Archive password present.</summary>
    public const string Password = "password";

    /// <summary><c>JobName != SubmitFileName</c> — mount folder will not match the release name.</summary>
    public const string JobNameDiverges = "job_name_diverges";

    /// <summary>Distinct <c>FileName</c>, same <c>JobName</c> — <c>GetDuplicateNzbBehavior()</c> decides.</summary>
    public const string MountFolderCollision = "mount_folder_collision";

    /// <summary><c>/content/{cat}/{JobName}</c> already exists in live NzbDAV.</summary>
    public const string MountFolderExists = "mount_folder_exists";

    /// <summary><c>{nzbBasename}</c> matches <c>PasswordRegex</c> — two password channels with unknown precedence.</summary>
    public const string FilenamePasswordMarker = "filename_password_marker";

    private static readonly IReadOnlyDictionary<string, Verdict> Severity = new Dictionary<string, Verdict>
    {
        [NoStoreRef] = Verdict.Red,
        [StoreMissing] = Verdict.Red,
        [StoreCorrupt] = Verdict.Red,
        [StoreEmpty] = Verdict.Red,
        [StatusCorrupted] = Verdict.Red,
        [CategoryUnmapped] = Verdict.Red,
        [QueueKeyCollision] = Verdict.Red,
        [CollidesWithExistingQueueItem] = Verdict.Red,
        [SomeFilesCorrupted] = Verdict.Amber,
        [NestedSources] = Verdict.Amber,
        [ClipBoundaries] = Verdict.Amber,
        [Encrypted] = Verdict.Amber,
        [Password] = Verdict.Amber,
        [JobNameDiverges] = Verdict.Amber,
        [MountFolderCollision] = Verdict.Amber,
        [MountFolderExists] = Verdict.Amber,
        [FilenamePasswordMarker] = Verdict.Amber,
    };

    /// <summary>Severity of a single reason code (Green if unknown).</summary>
    public static Verdict SeverityOf(string reason) =>
        Severity.TryGetValue(reason, out var v) ? v : Verdict.Green;

    /// <summary>Max severity across a set of reasons (Green when empty).</summary>
    public static Verdict VerdictFor(IEnumerable<string> reasons)
    {
        var worst = Verdict.Green;
        foreach (var r in reasons)
        {
            var s = SeverityOf(r);
            if (s > worst) worst = s;
        }

        return worst;
    }

    /// <summary>The Red reasons that block Review.</summary>
    public static bool BlocksReview(string reason) =>
        reason is CategoryUnmapped or QueueKeyCollision or CollidesWithExistingQueueItem;
}
