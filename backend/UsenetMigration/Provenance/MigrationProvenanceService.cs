using Microsoft.EntityFrameworkCore;
using NzbWebDAV.UsenetMigration.Naming;
using NzbWebDAV.UsenetMigration.Symlinks;
using NzbWebDAV.Database;
using NzbWebDAV.Database.Models;
using NzbWebDAV.Database.Models.UsenetMigration;
using Serilog;

namespace NzbWebDAV.UsenetMigration.Provenance;

/// <summary>
/// Persists successful source-to-NzbDAV mappings independently from the current
/// wizard scan so later migration runs can still rewrite earlier symlinks.
/// </summary>
public sealed class MigrationProvenanceService
{
    public async Task<int> RecordCompletedAsync(
        UsenetMigrationDbContext migrationContext,
        DavDatabaseContext davContext,
        MigrationSubmission submission,
        Guid nzoId,
        HistoryItem history,
        CancellationToken ct = default)
    {
        var runId = await EnsureCurrentRunAsync(migrationContext, ct).ConfigureAwait(false);
        var release = await migrationContext.Releases.AsNoTracking()
            .FirstAsync(r => r.StoreRef == submission.StoreRef, ct)
            .ConfigureAwait(false);
        var sourceFiles = await migrationContext.ReleaseFiles.AsNoTracking()
            .Where(f => f.StoreRef == submission.StoreRef)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        var leaves = await davContext.Items.AsNoTracking()
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

        var matches = SymlinkMatcher.Match(
            sourceFiles.Select(f => new MatchableFile
            {
                ReleaseFileId = f.Id,
                NormalisedName = f.NormalisedName,
                NormalisedRelativePath = MatchKey.ForRelativePath(f.VirtualPath),
                FileSize = f.FileSize,
            }).ToList(),
            leaves);
        var matchByFileId = matches
            .Where(m => m.DavItemId != null && m.MatchMethod != null)
            .ToDictionary(m => m.ReleaseFileId);

        var now = DateTime.UtcNow;
        var migratedRelease = await migrationContext.MigratedReleases
            .FirstOrDefaultAsync(
                r => r.SourceType == "altmount" && r.SourceReleaseId == submission.StoreRef,
                ct)
            .ConfigureAwait(false);
        if (migratedRelease is null)
        {
            migratedRelease = new MigratedRelease
            {
                SourceType = "altmount",
                SourceReleaseId = submission.StoreRef,
                FirstRunId = runId,
                MigratedAt = now,
            };
            migrationContext.MigratedReleases.Add(migratedRelease);
            await migrationContext.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        migratedRelease.LastRunId = runId;
        migratedRelease.NzoId = nzoId.ToString();
        migratedRelease.TargetCategory = history.Category;
        migratedRelease.JobName = history.JobName;
        migratedRelease.MountPath = $"/content/{history.Category}/{history.JobName}";
        migratedRelease.ExpectedFileCount = sourceFiles.Count;
        migratedRelease.MappedFileCount = matchByFileId.Count;
        migratedRelease.LastVerifiedAt = now;

        var existingFiles = await migrationContext.MigratedFiles
            .Where(f => f.MigratedReleaseId == migratedRelease.Id)
            .ToDictionaryAsync(f => f.VirtualPath, StringComparer.Ordinal, ct)
            .ConfigureAwait(false);
        foreach (var sourceFile in sourceFiles)
        {
            if (!matchByFileId.TryGetValue(sourceFile.Id, out var match))
                continue;

            if (!existingFiles.TryGetValue(sourceFile.VirtualPath, out var migratedFile))
            {
                migratedFile = new MigratedFile
                {
                    MigratedReleaseId = migratedRelease.Id,
                    VirtualPath = sourceFile.VirtualPath,
                };
                migrationContext.MigratedFiles.Add(migratedFile);
            }

            migratedFile.NormalisedRelativePath = MatchKey.ForRelativePath(sourceFile.VirtualPath);
            migratedFile.NormalisedName = sourceFile.NormalisedName;
            migratedFile.FileSize = sourceFile.FileSize;
            migratedFile.DavItemId = match.DavItemId!.Value;
            migratedFile.NzbBlobId = nzoId;
            migratedFile.MatchMethod = match.MatchMethod!;
            migratedFile.LastVerifiedAt = now;
        }

        if (matchByFileId.Count == sourceFiles.Count)
        {
            var currentPaths = sourceFiles.Select(f => f.VirtualPath).ToHashSet(StringComparer.Ordinal);
            migrationContext.MigratedFiles.RemoveRange(
                existingFiles.Values.Where(f => !currentPaths.Contains(f.VirtualPath)));
        }

        Log.Information(
            "Recorded migration provenance for {StoreRef}: Run={RunId}, ExpectedFiles={Expected}, " +
            "MappedFiles={Mapped}, NzoId={NzoId}",
            submission.StoreRef, runId, sourceFiles.Count, matchByFileId.Count, nzoId);
        return matchByFileId.Count;
    }

    private static async Task<long> EnsureCurrentRunAsync(
        UsenetMigrationDbContext context,
        CancellationToken ct)
    {
        var session = await UsenetMigrationStore.GetOrCreateSessionAsync(context, ct).ConfigureAwait(false);
        if (session.CurrentRunId is { } currentRunId
            && await context.MigrationRuns.AnyAsync(r => r.Id == currentRunId, ct).ConfigureAwait(false))
            return currentRunId;

        var now = DateTime.UtcNow;
        var run = new MigrationRun
        {
            SourceType = "altmount",
            Status = "running",
            StartedAt = now,
        };
        context.MigrationRuns.Add(run);
        await context.SaveChangesAsync(ct).ConfigureAwait(false);
        session.CurrentRunId = run.Id;
        session.RunStartedAt ??= now;
        session.UpdatedAt = now;
        await context.SaveChangesAsync(ct).ConfigureAwait(false);
        return run.Id;
    }
}
