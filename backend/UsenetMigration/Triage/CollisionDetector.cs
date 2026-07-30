namespace NzbWebDAV.UsenetMigration.Triage;

/// <summary>One included release, reduced to the fields collisions are keyed on.</summary>
public sealed class CollisionCandidate
{
    public required string StoreRef { get; init; }
    public required string TargetCategory { get; init; }

    /// <summary><c>ResolveFileName(basename)</c> — half of the <c>UNIQUE(Category, FileName)</c> key.</summary>
    public required string QueueFileName { get; init; }

    /// <summary>Predicted mount folder <c>GetJobName(QueueFileName)</c>.</summary>
    public required string JobName { get; init; }

    /// <summary>The raw <c>{nzbBasename}</c> submitted as <c>nzbname</c>.</summary>
    public required string SubmitFileName { get; init; }
}

public sealed class CollisionFinding
{
    public required string Reason { get; init; }
    public required IReadOnlyList<string> SiblingStoreRefs { get; init; }
}

public sealed class CollisionScanResult
{
    public required IReadOnlyDictionary<string, List<CollisionFinding>> FindingsByStoreRef { get; init; }
    public required int TotalReleases { get; init; }
    public required int JobNameDivergentReleases { get; init; }

    /// <summary>The measured JobName-divergence rate over the scanned releases.</summary>
    public double JobNameDivergenceRate =>
        TotalReleases == 0 ? 0 : (double)JobNameDivergentReleases / TotalReleases;
}

/// <summary>
/// Detects the two on-disk collision classes among included releases:
/// <list type="bullet">
/// <item><b>Queue-key collision (Red)</b> — ≥2 stores share <c>(cat, QueueFileName)</c>.
///   <c>AddFileController</c> would silently evict one and cancel its download.
///   Both releases remain blocked until the user excludes one.</item>
/// <item><b>Mount-folder collision (Amber)</b> — distinct <c>QueueFileName</c>, same
///   <c>JobName</c>. Both enter the queue; <c>GetDuplicateNzbBehavior()</c> decides.</item>
/// </list>
/// Collisions are a property of the SET, so this runs as a second pass and must
/// re-run whenever the category map or included set changes.
/// </summary>
public static class CollisionDetector
{
    /// <summary>Passes 1, 2 and 4 — all local, over the included candidate set.</summary>
    public static CollisionScanResult Detect(IReadOnlyList<CollisionCandidate> candidates)
    {
        var findings = new Dictionary<string, List<CollisionFinding>>(StringComparer.Ordinal);

        List<CollisionFinding> FindingsFor(string storeRef)
        {
            if (!findings.TryGetValue(storeRef, out var list))
            {
                list = new List<CollisionFinding>();
                findings[storeRef] = list;
            }

            return list;
        }

        // Group by (TargetCategory, JobName), then sub-group by QueueFileName.
        var groups = candidates
            .GroupBy(c => (c.TargetCategory, c.JobName));

        foreach (var group in groups)
        {
            var members = group.ToList();
            var redStoreRefs = new HashSet<string>(StringComparer.Ordinal);

            // Pass 1 (Red): a QueueFileName shared by ≥2 stores evicts silently.
            var byQueueKey = members.GroupBy(m => m.QueueFileName, StringComparer.Ordinal);
            var distinctQueueKeys = 0;
            foreach (var qk in byQueueKey)
            {
                distinctQueueKeys++;
                var siblings = qk.ToList();
                if (siblings.Count <= 1) continue;

                foreach (var member in siblings)
                {
                    redStoreRefs.Add(member.StoreRef);
                    FindingsFor(member.StoreRef).Add(new CollisionFinding
                    {
                        Reason = VerdictReason.QueueKeyCollision,
                        SiblingStoreRefs = siblings
                            .Where(s => !ReferenceEquals(s, member) && s.StoreRef != member.StoreRef)
                            .Select(s => s.StoreRef)
                            .ToList(),
                    });
                }
            }

            // Pass 2 (Amber): multiple distinct QueueFileNames sharing one JobName.
            if (distinctQueueKeys > 1)
            {
                foreach (var member in members)
                {
                    if (redStoreRefs.Contains(member.StoreRef)) continue;
                    FindingsFor(member.StoreRef).Add(new CollisionFinding
                    {
                        Reason = VerdictReason.MountFolderCollision,
                        SiblingStoreRefs = members
                            .Where(m => m.StoreRef != member.StoreRef)
                            .Select(m => m.StoreRef)
                            .ToList(),
                    });
                }
            }
        }

        // Measure how often QueueFileName and JobName diverge across the scanned set.
        var divergent = candidates.Count(c => !string.Equals(c.JobName, c.SubmitFileName, StringComparison.Ordinal));

        return new CollisionScanResult
        {
            FindingsByStoreRef = findings,
            TotalReleases = candidates.Count,
            JobNameDivergentReleases = divergent,
        };
    }

    /// <summary>
    /// Detects collisions with pre-existing live NzbDAV content. Kept
    /// separate and delegate-driven so it stays testable and re-runnable at
    /// Review (the live queue moves). <paramref name="queueItemExists"/> checks
    /// <c>QueueItems</c> for <c>(cat, QueueFileName)</c> (Red — would evict live
    /// content); <paramref name="davItemExists"/> checks for an existing
    /// <c>/content/{cat}/{JobName}</c> (Amber — <c>mount_folder_exists</c>).
    /// </summary>
    public static IReadOnlyDictionary<string, List<CollisionFinding>> DetectAgainstExisting(
        IReadOnlyList<CollisionCandidate> candidates,
        Func<string, string, bool> queueItemExists,
        Func<string, string, bool> davItemExists)
    {
        var findings = new Dictionary<string, List<CollisionFinding>>(StringComparer.Ordinal);

        foreach (var c in candidates)
        {
            var list = new List<CollisionFinding>();

            if (queueItemExists(c.TargetCategory, c.QueueFileName))
                list.Add(new CollisionFinding
                {
                    Reason = VerdictReason.CollidesWithExistingQueueItem,
                    SiblingStoreRefs = Array.Empty<string>(),
                });

            if (davItemExists(c.TargetCategory, c.JobName))
                list.Add(new CollisionFinding
                {
                    Reason = VerdictReason.MountFolderExists,
                    SiblingStoreRefs = Array.Empty<string>(),
                });

            if (list.Count > 0)
                findings[c.StoreRef] = list;
        }

        return findings;
    }
}
