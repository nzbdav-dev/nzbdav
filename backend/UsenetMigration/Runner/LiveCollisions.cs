using Microsoft.EntityFrameworkCore;
using NzbWebDAV.UsenetMigration.Triage;
using NzbWebDAV.Database;

namespace NzbWebDAV.UsenetMigration.Runner;

/// <summary>
/// Shared collision helpers used by both the initial scan
/// and the in-place recompute that follows an include/exclude edit. Keeps the
/// live-content snapshot and key format in one place.
/// </summary>
public static class LiveCollisions
{
    /// <summary>The four reason codes that are a property of the SET, not the release.</summary>
    public static readonly IReadOnlySet<string> CollisionReasons = new HashSet<string>(StringComparer.Ordinal)
    {
        VerdictReason.QueueKeyCollision,
        VerdictReason.MountFolderCollision,
        VerdictReason.CollidesWithExistingQueueItem,
        VerdictReason.MountFolderExists,
    };

    /// <summary>The <c>(Category, FileName)</c> UNIQUE-key half, formatted for hashing.</summary>
    public static string Key(string category, string fileName) => category + " " + fileName;

    /// <summary>
    /// Snapshots live NzbDAV state for pass 3: the set of queue <c>(cat, fileName)</c>
    /// keys and the set of existing <c>/content/…</c> mount paths.
    /// <paramref name="davContextFactory"/> is a test seam; production passes null.
    /// </summary>
    public static (HashSet<string> queueKeys, HashSet<string> contentPaths) LoadSets(
        Func<DavDatabaseContext>? davContextFactory = null)
    {
        using var ctx = davContextFactory?.Invoke() ?? new DavDatabaseContext();
        var queueKeys = ctx.QueueItems.AsNoTracking()
            .Select(q => new { q.Category, q.FileName })
            .AsEnumerable()
            .Select(q => Key(q.Category, q.FileName))
            .ToHashSet(StringComparer.Ordinal);
        var contentPaths = ctx.Items.AsNoTracking()
            .Where(i => i.Path.StartsWith("/content/"))
            .Select(i => i.Path)
            .ToHashSet(StringComparer.Ordinal);
        return (queueKeys, contentPaths);
    }
}
