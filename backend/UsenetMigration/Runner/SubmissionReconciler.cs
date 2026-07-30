using Microsoft.EntityFrameworkCore;
using NzbWebDAV.UsenetMigration.Provenance;
using NzbWebDAV.Database;
using NzbWebDAV.Database.Models;
using NzbWebDAV.Database.Models.UsenetMigration;
using Serilog;

namespace NzbWebDAV.UsenetMigration.Runner;

/// <summary>Counts from one reconciliation pass, for the runner's progress log.</summary>
public sealed class ReconcileSummary
{
    public int Completed { get; init; }
    public int Failed { get; init; }
    public int StillProcessing { get; init; }
    public int Evicted { get; init; }
}

/// <summary>
/// Polls the outcome of submitted releases against NzbDAV's own queue/history and
/// advances each submission's lifecycle. Reads the authoritative
/// post-import values off <see cref="HistoryItem"/> — never the predicted JobName.
///
/// Successful submissions retain their SAB history until the user invokes the
/// optional cleanup action after Step 5 or Step 6. Import success and housekeeping
/// are deliberately separate lifecycle concerns.
/// </summary>
public sealed class SubmissionReconciler(UsenetMigrationStore store)
{
    private readonly MigrationProvenanceService _provenance = new();

    /// <summary>Test seam for the live NzbDAV context; production leaves it null.</summary>
    internal Func<DavDatabaseContext>? DavContextFactory { get; set; }

    public async Task<ReconcileSummary> ReconcileAsync(CancellationToken ct = default)
    {
        await using var ctx = store.NewContext();
        var active = await ctx.Submissions
            .Where(s => s.State == "submitted" || s.State == "processing")
            .ToListAsync(ct)
            .ConfigureAwait(false);
        if (active.Count == 0)
            return new ReconcileSummary();

        await using var davCtx = DavContextFactory?.Invoke() ?? new DavDatabaseContext();
        int completed = 0, failed = 0, processing = 0, evicted = 0;
        foreach (var sub in active)
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrEmpty(sub.NzoId) || !Guid.TryParse(sub.NzoId, out var nzoId))
                continue;

            var history = await davCtx.HistoryItems.AsNoTracking()
                .FirstOrDefaultAsync(h => h.Id == nzoId, ct)
                .ConfigureAwait(false);

            if (history is not null)
            {
                if (history.DownloadStatus == HistoryItem.DownloadStatusOption.Completed)
                {
                    await CompleteAsync(sub, nzoId, history, ctx, davCtx, ct).ConfigureAwait(false);
                    completed++;
                }
                else
                {
                    // Record the failure reason verbatim and retain the history item.
                    sub.State = "failed";
                    sub.Error = history.FailMessage;
                    failed++;
                }
            }
            else
            {
                var inQueue = await davCtx.QueueItems.AsNoTracking()
                    .AnyAsync(q => q.Id == nzoId, ct)
                    .ConfigureAwait(false);
                if (inQueue)
                {
                    sub.State = "processing";
                    processing++;
                }
                else
                {
                    // A submission missing from both queue and history was silently evicted
                    // by a colliding re-add. Treat it as terminal to prevent resubmit loops.
                    await MarkEvictedAsync(sub, ctx, ct).ConfigureAwait(false);
                    evicted++;
                }
            }

            sub.UpdatedAt = DateTime.UtcNow;
        }

        await ctx.SaveChangesAsync(ct).ConfigureAwait(false);

        return new ReconcileSummary
        {
            Completed = completed,
            Failed = failed,
            StillProcessing = processing,
            Evicted = evicted,
        };
    }

    private async Task CompleteAsync(
        MigrationSubmission sub,
        Guid nzoId,
        HistoryItem history,
        UsenetMigrationDbContext migrationContext,
        DavDatabaseContext davCtx,
        CancellationToken ct)
    {
        // Verify the import produced mounted content and record the authoritative
        // mount path from the history row.
        var davItemCount = await davCtx.Items.AsNoTracking()
            .CountAsync(i => i.HistoryItemId == nzoId, ct)
            .ConfigureAwait(false);

        await _provenance.RecordCompletedAsync(
                migrationContext, davCtx, sub, nzoId, history, ct)
            .ConfigureAwait(false);

        sub.MountPath = $"/content/{history.Category}/{history.JobName}";
        sub.DavItemCount = davItemCount;
        sub.CompletedAt = DateTime.UtcNow;
        sub.State = "completed";

        if (davItemCount == 0)
        {
            Log.Warning(
                "Migration release {StoreRef} (nzo {NzoId}) completed but produced 0 DavItems at {MountPath}",
                sub.StoreRef, nzoId, sub.MountPath);
        }
    }

    private static async Task MarkEvictedAsync(
        MigrationSubmission sub, UsenetMigrationDbContext ctx, CancellationToken ct)
    {
        sub.State = "evicted";

        var release = await ctx.Releases.AsNoTracking()
            .FirstOrDefaultAsync(r => r.StoreRef == sub.StoreRef, ct)
            .ConfigureAwait(false);
        var siblings = release is null
            ? new List<string>()
            : await ctx.Releases.AsNoTracking()
                .Where(r => r.StoreRef != release.StoreRef
                            && r.TargetCategory == release.TargetCategory
                            && r.QueueFileName == release.QueueFileName)
                .Select(r => r.StoreRef)
                .ToListAsync(ct)
                .ConfigureAwait(false);

        sub.Error = release is null
            ? "The submission disappeared from both the NzbDAV queue and history before it completed."
            : $"Another migration release used the same NzbDAV queue key " +
              $"({release.TargetCategory}, {release.QueueFileName}), replacing this submission.";

        Log.Error(
            "Migration release {StoreRef} (nzo {NzoId}) vanished from both queue and history — " +
            "silently evicted by a colliding re-add on key ({Category}, {FileName}). " +
            "Marked evicted (terminal, will NOT auto-resubmit). Colliding sibling(s): {Siblings}",
            sub.StoreRef, sub.NzoId, release?.TargetCategory, release?.QueueFileName,
            siblings.Count == 0 ? "none found" : string.Join(", ", siblings));
    }
}
