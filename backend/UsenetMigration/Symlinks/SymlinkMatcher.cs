using Microsoft.EntityFrameworkCore;
using NzbWebDAV.UsenetMigration.Naming;
using NzbWebDAV.Database;
using NzbWebDAV.Database.Models;

namespace NzbWebDAV.UsenetMigration.Symlinks;

/// <summary>A live NzbDAV leaf under a completed release, reduced to what the matcher needs.</summary>
public sealed class ReleaseLeaf
{
    public required Guid DavItemId { get; init; }

    /// <summary><c>DavItem.Name</c> — already SanitizeComponent'd at import time.</summary>
    public required string Name { get; init; }
    public required string Path { get; init; }
    public long? FileSize { get; init; }
    public Guid? NzbBlobId { get; init; }
    public required string IdentityMethod { get; init; }
}

/// <summary>An Altmount virtual file to match, reduced to its identity + normalised key.</summary>
public sealed class MatchableFile
{
    public required long ReleaseFileId { get; init; }

    /// <summary><c>MatchKey.ForLeaf(basename)</c>, computed during the migration scan.</summary>
    public required string NormalisedName { get; init; }
    public required string NormalisedRelativePath { get; init; }
    public long? FileSize { get; init; }
}

/// <summary>Outcome of matching one Altmount file to a live DavItem leaf.</summary>
public sealed class LeafMatch
{
    public required long ReleaseFileId { get; init; }

    /// <summary>The matched DavItem, or null when the file must remain an orphan.</summary>
    public Guid? DavItemId { get; init; }

    /// <summary>relative-path|exact|unique-size|single-leaf-fallback, or null when unmatched.</summary>
    public string? MatchMethod { get; init; }
}

/// <summary>
/// Conservatively connects each Altmount virtual file to the
/// live NzbDAV leaf the migration produced for it, so its symlink can be repointed
/// at the leaf's <c>.ids/…/&lt;guid&gt;</c> path.
///
/// <para>
/// Leaves are loaded primarily through the durable <c>NzbBlobId == NzoId</c>
/// identity, with <c>HistoryItemId</c> retained as a compatibility fallback. Normal
/// history retention clears <c>HistoryItemId</c>, while <c>NzbBlobId</c> remains on
/// mounted content. Within a release, matching proceeds from normalized relative
/// path to normalized basename, unique file size, and finally the singular-release
/// fallback. Every strategy is unambiguous and a DavItem may be assigned only once.
/// </para>
/// </summary>
public static class SymlinkMatcher
{
    /// <summary>
    /// Loads the live leaf DavItems for one completed release via its <c>NzoId</c>
    /// through durable <c>DavItem.NzbBlobId</c>, falling back to
    /// <c>DavItem.HistoryItemId</c>. <paramref name="davContextFactory"/> is a test
    /// seam; production passes null.
    /// </summary>
    public static async Task<IReadOnlyList<ReleaseLeaf>> LoadLeavesAsync(
        Guid nzoId,
        Func<DavDatabaseContext>? davContextFactory = null,
        CancellationToken ct = default)
    {
        await using var ctx = davContextFactory?.Invoke() ?? new DavDatabaseContext();
        return await ctx.Items.AsNoTracking()
            .Where(i => (i.NzbBlobId == nzoId || i.HistoryItemId == nzoId)
                        && i.Type == DavItem.ItemType.UsenetFile)
            .Select(i => new ReleaseLeaf
            {
                DavItemId = i.Id,
                Name = i.Name,
                Path = i.Path,
                FileSize = i.FileSize,
                NzbBlobId = i.NzbBlobId,
                IdentityMethod = i.NzbBlobId == nzoId ? "nzb-blob-id" : "history-item-id",
            })
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// The pure conservative match over one release's files and its live leaves.
    /// <list type="number">
    /// <item><b>Relative path</b> — a unique normalized path/suffix pair.</item>
    /// <item><b>Exact name</b> — a leaf whose normalised name uniquely equals the file's
    ///   <c>NormalisedName</c>. Ambiguous keys (two leaves normalising alike) are not
    ///   matched exactly, to avoid an arbitrary pick.</item>
    /// <item><b>Unique size</b> — one unmatched file and leaf share a size.</item>
    /// <item><b>Single-leaf fallback</b> — when the release produced exactly one leaf
    ///   AND has exactly one file, pair them regardless of name. This is the
    ///   deobfuscation-divergence case: Altmount deobfuscated the inner name,
    ///   NzbDAV did not. Requiring both sides singular prevents aliasing several
    ///   symlinks onto one target.</item>
    /// </list>
    /// Everything else is left unmatched (⇒ orphan).
    /// </summary>
    public static IReadOnlyList<LeafMatch> Match(
        IReadOnlyList<MatchableFile> files,
        IReadOnlyList<ReleaseLeaf> leaves)
    {
        // Group leaves by their normalised key; only keys with a single leaf are
        // eligible for an exact match.
        var results = files.ToDictionary(
            f => f.ReleaseFileId,
            f => new LeafMatch { ReleaseFileId = f.ReleaseFileId });
        var usedDavItemIds = new HashSet<Guid>();

        void Assign(MatchableFile file, ReleaseLeaf leaf, string method)
        {
            results[file.ReleaseFileId] = new LeafMatch
            {
                ReleaseFileId = file.ReleaseFileId,
                DavItemId = leaf.DavItemId,
                MatchMethod = method,
            };
            usedDavItemIds.Add(leaf.DavItemId);
        }

        static bool RelativePathMatches(MatchableFile file, ReleaseLeaf leaf)
        {
            if (string.IsNullOrEmpty(file.NormalisedRelativePath)) return false;
            var leafPath = MatchKey.ForRelativePath(leaf.Path);
            return leafPath.Equals(file.NormalisedRelativePath, StringComparison.Ordinal)
                   || leafPath.EndsWith("/" + file.NormalisedRelativePath, StringComparison.Ordinal)
                   || file.NormalisedRelativePath.EndsWith("/" + leafPath, StringComparison.Ordinal);
        }

        // Prefer the full normalised path when exactly one source file and one leaf
        // identify each other. Prefix differences such as /content are tolerated.
        var pathCandidates = files.ToDictionary(
            f => f.ReleaseFileId,
            f => leaves.Where(l => RelativePathMatches(f, l)).ToList());
        foreach (var file in files)
        {
            var candidates = pathCandidates[file.ReleaseFileId];
            if (candidates.Count != 1) continue;
            var leaf = candidates[0];
            var sourceCount = files.Count(f => pathCandidates[f.ReleaseFileId]
                .Any(l => l.DavItemId == leaf.DavItemId));
            if (sourceCount == 1 && !usedDavItemIds.Contains(leaf.DavItemId))
                Assign(file, leaf, "relative-path");
        }

        // Exact normalised names must be unique on both sides. Requiring source
        // uniqueness prevents two Altmount files from reusing one DavItem.
        var uniqueFilesByName = files
            .Where(f => results[f.ReleaseFileId].DavItemId == null)
            .GroupBy(f => f.NormalisedName, StringComparer.Ordinal)
            .Where(g => g.Count() == 1)
            .ToDictionary(g => g.Key, g => g.Single(), StringComparer.Ordinal);
        var uniqueLeavesByName = leaves
            .Where(l => !usedDavItemIds.Contains(l.DavItemId))
            .GroupBy(l => MatchKey.ForLeaf(l.Name), StringComparer.Ordinal)
            .Where(g => g.Count() == 1)
            .ToDictionary(g => g.Key, g => g.Single(), StringComparer.Ordinal);
        foreach (var (key, file) in uniqueFilesByName)
        {
            if (uniqueLeavesByName.TryGetValue(key, out var leaf)
                && !usedDavItemIds.Contains(leaf.DavItemId))
                Assign(file, leaf, "exact");
        }

        // Size is a conservative fallback only when exactly one unmatched file and
        // one unused leaf share that size.
        var uniqueFilesBySize = files
            .Where(f => results[f.ReleaseFileId].DavItemId == null && f.FileSize != null)
            .GroupBy(f => f.FileSize!.Value)
            .Where(g => g.Count() == 1)
            .ToDictionary(g => g.Key, g => g.Single());
        var uniqueLeavesBySize = leaves
            .Where(l => !usedDavItemIds.Contains(l.DavItemId) && l.FileSize != null)
            .GroupBy(l => l.FileSize!.Value)
            .Where(g => g.Count() == 1)
            .ToDictionary(g => g.Key, g => g.Single());
        foreach (var (size, file) in uniqueFilesBySize)
        {
            if (uniqueLeavesBySize.TryGetValue(size, out var leaf)
                && !usedDavItemIds.Contains(leaf.DavItemId))
                Assign(file, leaf, "unique-size");
        }

        if (files.Count == 1 && leaves.Count == 1
            && results[files[0].ReleaseFileId].DavItemId == null
            && !usedDavItemIds.Contains(leaves[0].DavItemId))
            Assign(files[0], leaves[0], "single-leaf-fallback");

        return files.Select(f => results[f.ReleaseFileId]).ToList();
    }
}
