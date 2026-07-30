using Microsoft.EntityFrameworkCore;
using NzbWebDAV.UsenetMigration.Naming;
using NzbWebDAV.UsenetMigration.Symlinks;
using NzbWebDAV.Database;
using NzbWebDAV.Database.Models;
using NzbWebDAV.Database.Models.UsenetMigration;
using Serilog;

namespace NzbWebDAV.UsenetMigration.Provenance;

public sealed class AlreadyMigratedCandidate
{
    public required string StoreRef { get; init; }
    public required string TargetCategory { get; init; }
    public required string JobName { get; init; }
    public required IReadOnlyList<MigrationReleaseFile> Files { get; init; }
}

/// <summary>
/// Conservatively recognizes a source release only when every expected file maps
/// uniquely to a live NzbDAV leaf. Existing provenance is preferred; the predicted
/// mount folder is used to recover provenance created before this ledger existed.
/// </summary>
public sealed class AlreadyMigratedDetector
{
    private sealed record DetectedFile(
        MigrationReleaseFile Source,
        ReleaseLeaf Leaf,
        string MatchMethod);

    public async Task<IReadOnlySet<string>> DetectAndRecordAsync(
        IReadOnlyList<AlreadyMigratedCandidate> candidates,
        UsenetMigrationDbContext migrationContext,
        DavDatabaseContext davContext,
        CancellationToken ct = default)
    {
        if (candidates.Count == 0)
            return new HashSet<string>(StringComparer.Ordinal);

        var storeRefs = candidates.Select(c => c.StoreRef).ToList();
        var provenance = await migrationContext.MigratedReleases
            .Where(r => r.SourceType == "altmount" && storeRefs.Contains(r.SourceReleaseId))
            .ToDictionaryAsync(r => r.SourceReleaseId, StringComparer.Ordinal, ct)
            .ConfigureAwait(false);
        var provenanceIds = provenance.Values.Select(r => r.Id).ToList();
        var provenanceFiles = provenanceIds.Count == 0
            ? new List<MigratedFile>()
            : await migrationContext.MigratedFiles
                .Where(f => provenanceIds.Contains(f.MigratedReleaseId))
                .ToListAsync(ct)
                .ConfigureAwait(false);
        var provenanceFilesByRelease = provenanceFiles
            .GroupBy(f => f.MigratedReleaseId)
            .ToDictionary(g => g.Key, g => g.ToDictionary(f => f.VirtualPath, StringComparer.Ordinal));

        var liveLeaves = await davContext.Items.AsNoTracking()
            .Where(i => i.Type == DavItem.ItemType.UsenetFile && i.Path.StartsWith("/content"))
            .Select(i => new ReleaseLeaf
            {
                DavItemId = i.Id,
                Name = i.Name,
                Path = i.Path,
                FileSize = i.FileSize,
                NzbBlobId = i.NzbBlobId,
                IdentityMethod = "live-content",
            })
            .ToListAsync(ct)
            .ConfigureAwait(false);
        var liveById = liveLeaves.ToDictionary(l => l.DavItemId);

        long? discoveryRunId = null;
        var detectedStoreRefs = new HashSet<string>(StringComparer.Ordinal);
        foreach (var candidate in candidates)
        {
            ct.ThrowIfCancellationRequested();
            List<DetectedFile>? detected = null;
            provenance.TryGetValue(candidate.StoreRef, out var migratedRelease);
            if (migratedRelease is not null
                && provenanceFilesByRelease.TryGetValue(migratedRelease.Id, out var historicalFiles))
            {
                detected = MatchHistorical(candidate, historicalFiles, liveById);
            }

            detected ??= MatchLiveMount(candidate, liveLeaves);
            if (detected is null)
                continue;

            if (migratedRelease is null)
            {
                discoveryRunId ??= await CreateDiscoveryRunAsync(migrationContext, ct).ConfigureAwait(false);
                migratedRelease = new MigratedRelease
                {
                    SourceType = "altmount",
                    SourceReleaseId = candidate.StoreRef,
                    FirstRunId = discoveryRunId.Value,
                    LastRunId = discoveryRunId.Value,
                    MigratedAt = DateTime.UtcNow,
                };
                migrationContext.MigratedReleases.Add(migratedRelease);
                await migrationContext.SaveChangesAsync(ct).ConfigureAwait(false);
                provenance[candidate.StoreRef] = migratedRelease;
            }

            await UpsertFilesAsync(
                    migrationContext, migratedRelease, candidate, detected, ct)
                .ConfigureAwait(false);
            detectedStoreRefs.Add(candidate.StoreRef);
            Log.Information(
                "Recognized already migrated release {StoreRef}: {FileCount} file(s), Method={Method}",
                candidate.StoreRef, detected.Count,
                detected.All(f => f.MatchMethod == "provenance") ? "provenance" : "live-content");
        }

        return detectedStoreRefs;
    }

    private static List<DetectedFile>? MatchHistorical(
        AlreadyMigratedCandidate candidate,
        IReadOnlyDictionary<string, MigratedFile> historicalFiles,
        IReadOnlyDictionary<Guid, ReleaseLeaf> liveById)
    {
        if (candidate.Files.Count == 0 || historicalFiles.Count != candidate.Files.Count)
            return null;

        var result = new List<DetectedFile>(candidate.Files.Count);
        foreach (var source in candidate.Files)
        {
            if (!historicalFiles.TryGetValue(source.VirtualPath, out var historical)
                || historical.NormalisedName != source.NormalisedName
                || historical.FileSize != source.FileSize
                || !liveById.TryGetValue(historical.DavItemId, out var leaf))
                return null;
            result.Add(new DetectedFile(source, leaf, "provenance"));
        }
        return result;
    }

    private static List<DetectedFile>? MatchLiveMount(
        AlreadyMigratedCandidate candidate,
        IReadOnlyList<ReleaseLeaf> liveLeaves)
    {
        if (candidate.Files.Count == 0)
            return null;

        var mountKey = MatchKey.ForRelativePath(
            $"/content/{candidate.TargetCategory}/{candidate.JobName}");
        var candidates = liveLeaves
            .Where(l => MatchKey.ForRelativePath(l.Path)
                .StartsWith(mountKey + "/", StringComparison.Ordinal))
            .ToList();
        if (candidates.Count == 0)
            return null;

        var sourceById = candidate.Files
            .Select((file, index) => new { Id = (long)index + 1, File = file })
            .ToDictionary(x => x.Id, x => x.File);
        var matches = SymlinkMatcher.Match(
            sourceById.Select(x => new MatchableFile
            {
                ReleaseFileId = x.Key,
                NormalisedName = x.Value.NormalisedName,
                NormalisedRelativePath = MatchKey.ForRelativePath(x.Value.VirtualPath),
                FileSize = x.Value.FileSize,
            }).ToList(),
            candidates);
        if (matches.Count != candidate.Files.Count
            || matches.Any(m => m.DavItemId == null || m.MatchMethod == null)
            || matches.Select(m => m.DavItemId).Distinct().Count() != candidate.Files.Count)
            return null;

        var leafById = candidates.ToDictionary(l => l.DavItemId);
        return matches.Select(m => new DetectedFile(
                sourceById[m.ReleaseFileId],
                leafById[m.DavItemId!.Value],
                "live-" + m.MatchMethod))
            .ToList();
    }

    private static async Task UpsertFilesAsync(
        UsenetMigrationDbContext context,
        MigratedRelease release,
        AlreadyMigratedCandidate candidate,
        IReadOnlyList<DetectedFile> detected,
        CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var blobIds = detected.Select(f => f.Leaf.NzbBlobId).Distinct().ToList();
        release.NzoId = blobIds.Count == 1 && blobIds[0] is { } blobId
            ? blobId.ToString()
            : release.NzoId;
        release.TargetCategory = candidate.TargetCategory;
        release.JobName = candidate.JobName;
        release.MountPath = $"/content/{candidate.TargetCategory}/{candidate.JobName}";
        release.ExpectedFileCount = candidate.Files.Count;
        release.MappedFileCount = detected.Count;
        release.LastVerifiedAt = now;

        var existing = await context.MigratedFiles
            .Where(f => f.MigratedReleaseId == release.Id)
            .ToDictionaryAsync(f => f.VirtualPath, StringComparer.Ordinal, ct)
            .ConfigureAwait(false);
        foreach (var match in detected)
        {
            if (!existing.TryGetValue(match.Source.VirtualPath, out var row))
            {
                row = new MigratedFile
                {
                    MigratedReleaseId = release.Id,
                    VirtualPath = match.Source.VirtualPath,
                };
                context.MigratedFiles.Add(row);
            }
            row.NormalisedRelativePath = MatchKey.ForRelativePath(match.Source.VirtualPath);
            row.NormalisedName = match.Source.NormalisedName;
            row.FileSize = match.Source.FileSize;
            row.DavItemId = match.Leaf.DavItemId;
            row.NzbBlobId = match.Leaf.NzbBlobId;
            row.MatchMethod = match.MatchMethod;
            row.LastVerifiedAt = now;
        }

        var currentPaths = detected.Select(f => f.Source.VirtualPath).ToHashSet(StringComparer.Ordinal);
        context.MigratedFiles.RemoveRange(
            existing.Values.Where(f => !currentPaths.Contains(f.VirtualPath)));
        await context.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private static async Task<long> CreateDiscoveryRunAsync(
        UsenetMigrationDbContext context,
        CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var run = new MigrationRun
        {
            SourceType = "altmount",
            Status = "discovered",
            StartedAt = now,
            CompletedAt = now,
        };
        context.MigrationRuns.Add(run);
        await context.SaveChangesAsync(ct).ConfigureAwait(false);
        return run.Id;
    }
}
