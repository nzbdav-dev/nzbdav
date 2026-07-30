using Microsoft.EntityFrameworkCore;
using NzbWebDAV.Database;
using Serilog;

namespace NzbWebDAV.UsenetMigration.Runner;

public sealed class MigrationHistoryCleanupSummary
{
    public int Eligible { get; init; }
    public int Removed { get; init; }
    public int AlreadyAbsent { get; init; }
    public int Skipped { get; init; }
    public int Failed { get; init; }
}

/// <summary>
/// Explicitly clears SAB history created by successful migration submissions while
/// preserving mounted content. Cleanup is separate from submission success and is
/// safe to repeat; <c>HistoryClearedAt</c> records housekeeping without changing a
/// completed submission's lifecycle state.
/// </summary>
public sealed class MigrationHistoryCleaner(UsenetMigrationStore store)
{
    private const int BatchSize = 500;

    /// <summary>Test seam for the live NzbDAV context; production leaves it null.</summary>
    internal Func<DavDatabaseContext>? DavContextFactory { get; set; }

    public async Task<MigrationHistoryCleanupSummary> CleanAsync(CancellationToken ct = default)
    {
        await using var migrationContext = store.NewContext();
        var candidates = await migrationContext.Submissions
            .Where(s => (s.State == "completed" || s.State == "history_cleared")
                        && s.HistoryClearedAt == null)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var valid = candidates
            .Select(s => new { Submission = s, Parsed = Guid.TryParse(s.NzoId, out var id), Id = id })
            .Where(x => x.Parsed)
            .ToList();
        var skipped = candidates.Count - valid.Count;
        if (valid.Count == 0)
        {
            return new MigrationHistoryCleanupSummary
            {
                Eligible = candidates.Count,
                Skipped = skipped,
            };
        }

        await using var davContext = DavContextFactory?.Invoke() ?? new DavDatabaseContext();
        var davClient = new DavDatabaseClient(davContext);
        var removed = 0;

        foreach (var batch in valid.Select(x => x.Id).Distinct().Chunk(BatchSize))
        {
            var existingIds = await davContext.HistoryItems.AsNoTracking()
                .Where(h => batch.Contains(h.Id))
                .Select(h => h.Id)
                .ToListAsync(ct)
                .ConfigureAwait(false);
            if (existingIds.Count == 0)
                continue;

            await davClient.RemoveHistoryItemsAsync(existingIds, deleteFiles: false, ct)
                .ConfigureAwait(false);
            await davContext.SaveChangesAsync(ct).ConfigureAwait(false);
            davContext.ChangeTracker.Clear();
            removed += existingIds.Count;
        }

        var clearedAt = DateTime.UtcNow;
        foreach (var candidate in valid)
        {
            candidate.Submission.HistoryClearedAt = clearedAt;
            candidate.Submission.UpdatedAt = clearedAt;
        }
        await migrationContext.SaveChangesAsync(ct).ConfigureAwait(false);

        var summary = new MigrationHistoryCleanupSummary
        {
            Eligible = candidates.Count,
            Removed = removed,
            AlreadyAbsent = valid.Count - removed,
            Skipped = skipped,
        };
        Log.Information(
            "Usenet migration history cleanup: Eligible={Eligible}, Removed={Removed}, " +
            "AlreadyAbsent={AlreadyAbsent}, Skipped={Skipped}, Failed={Failed}",
            summary.Eligible, summary.Removed, summary.AlreadyAbsent, summary.Skipped, summary.Failed);
        return summary;
    }
}
