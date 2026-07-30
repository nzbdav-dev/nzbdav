using NzbWebDAV.UsenetMigration.Model;

namespace NzbWebDAV.UsenetMigration.Triage;

/// <summary>Availability/decodability of a release's <c>.nzbz</c> store.</summary>
public enum StoreAvailability
{
    Ok,
    Missing,
    Corrupt,
    Empty,
}

/// <summary>
/// Per-release inputs the triage classifier needs. Collision-related state is NOT
/// here — those reasons are a property of the whole scanned set and are added by
/// <see cref="CollisionDetector"/> in a second pass.
/// </summary>
public sealed class TriageInput
{
    /// <summary>False for v1 metadata with no shared store, producing Red <c>no_store_ref</c>.</summary>
    public required bool HasStoreRef { get; init; }

    public required StoreAvailability Store { get; init; }

    /// <summary>Every <c>.meta</c> that points at this store.</summary>
    public required IReadOnlyList<AltmountFileMetadata> Metas { get; init; }

    /// <summary>False when the store-path category has no mapping row.</summary>
    public required bool CategoryMapped { get; init; }

    /// <summary><c>JobName != SubmitFileName</c> (computed via <see cref="Naming.NzbDavNaming"/>).</summary>
    public required bool JobNameDiverges { get; init; }

    /// <summary>The submit filename matches NzbDAV's <c>PasswordRegex</c>.</summary>
    public required bool FilenamePasswordMarker { get; init; }
}

/// <summary>
/// Classifies a single release into machine-readable verdict reasons.
/// Answers three of triage's four questions; the fourth (collisions / damage to
/// other content) is <see cref="CollisionDetector"/>'s job.
/// </summary>
public static class TriageClassifier
{
    public static IReadOnlyList<string> Classify(TriageInput input)
    {
        // Hard store-level failures: nothing else about the release is evaluable.
        if (!input.HasStoreRef)
            return new[] { VerdictReason.NoStoreRef };

        switch (input.Store)
        {
            case StoreAvailability.Missing:
                return new[] { VerdictReason.StoreMissing };
            case StoreAvailability.Corrupt:
                return new[] { VerdictReason.StoreCorrupt };
            case StoreAvailability.Empty:
                return new[] { VerdictReason.StoreEmpty };
        }

        // Health: every file dead is a hard exclude (Altmount already knows).
        var (corrupted, total) = CountCorrupted(input.Metas);
        if (total > 0 && corrupted == total)
            return new[] { VerdictReason.StatusCorrupted };

        var reasons = new List<string>();

        if (!input.CategoryMapped)
            reasons.Add(VerdictReason.CategoryUnmapped);

        if (corrupted > 0)
            reasons.Add(VerdictReason.SomeFilesCorrupted);

        if (input.Metas.Any(m => m.Encryption != AltmountEncryption.None))
            reasons.Add(VerdictReason.Encrypted);
        if (input.Metas.Any(m => !string.IsNullOrEmpty(m.Password)))
            reasons.Add(VerdictReason.Password);
        if (input.Metas.Any(m => m.HasNestedSources))
            reasons.Add(VerdictReason.NestedSources);
        if (input.Metas.Any(m => m.HasClipBoundaries))
            reasons.Add(VerdictReason.ClipBoundaries);

        if (input.JobNameDiverges)
            reasons.Add(VerdictReason.JobNameDiverges);
        if (input.FilenamePasswordMarker)
            reasons.Add(VerdictReason.FilenamePasswordMarker);

        return reasons;
    }

    /// <summary>Worst (most severe) file status across the release, for reporting.</summary>
    public static AltmountFileStatus WorstFileStatus(IReadOnlyList<AltmountFileMetadata> metas)
    {
        if (metas.Any(m => m.Status == AltmountFileStatus.Corrupted))
            return AltmountFileStatus.Corrupted;
        if (metas.Any(m => m.Status == AltmountFileStatus.Healthy))
            return AltmountFileStatus.Healthy;
        return AltmountFileStatus.Unspecified;
    }

    private static (int corrupted, int total) CountCorrupted(IReadOnlyList<AltmountFileMetadata> metas)
    {
        var corrupted = 0;
        foreach (var m in metas)
            if (m.Status == AltmountFileStatus.Corrupted)
                corrupted++;
        return (corrupted, metas.Count);
    }
}
