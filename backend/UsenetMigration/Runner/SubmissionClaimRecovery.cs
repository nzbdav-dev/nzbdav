using Microsoft.EntityFrameworkCore;
using NzbWebDAV.Database;
using NzbWebDAV.Database.Models.UsenetMigration;
using Serilog;

namespace NzbWebDAV.UsenetMigration.Runner;

/// <summary>Counts from one recovery pass over durable pre-submit claims.</summary>
internal sealed class SubmissionRecoverySummary
{
    public int Adopted { get; init; }
    public int Retried { get; init; }
    public int Ambiguous { get; init; }
}

/// <summary>
/// Reconciles releases left in <c>submitting</c> after a crash. An exact claimed
/// id is authoritative. A different job occupying the same queue key is
/// deliberately terminal: migration must never evict it or blindly resubmit.
/// </summary>
internal static class SubmissionClaimRecovery
{
    internal static async Task<SubmissionRecoverySummary> RecoverAsync(
        UsenetMigrationStore store,
        Func<DavDatabaseContext>? davContextFactory = null,
        CancellationToken ct = default)
    {
        await using var migrationContext = store.NewContext();
        var claims = await migrationContext.Submissions
            .Where(s => s.State == "submitting")
            .ToListAsync(ct)
            .ConfigureAwait(false);
        if (claims.Count == 0)
            return new SubmissionRecoverySummary();

        await using var davContext = davContextFactory?.Invoke() ?? new DavDatabaseContext();
        var adopted = 0;
        var retried = 0;
        var ambiguous = 0;

        foreach (var claim in claims)
        {
            ct.ThrowIfCancellationRequested();
            var release = await migrationContext.Releases.AsNoTracking()
                .SingleOrDefaultAsync(r => r.StoreRef == claim.StoreRef, ct)
                .ConfigureAwait(false);

            if (release is null || string.IsNullOrEmpty(release.TargetCategory)
                                || string.IsNullOrEmpty(release.QueueFileName)
                                || !Guid.TryParse(claim.NzoId, out var claimedId))
            {
                MarkAmbiguous(
                    claim,
                    "The durable submission claim is incomplete; refusing to resubmit an ambiguous release.");
                ambiguous++;
                continue;
            }

            var queuedAt = await davContext.QueueItems.AsNoTracking()
                .Where(q => q.Id == claimedId)
                .Select(q => (DateTime?)q.CreatedAt)
                .SingleOrDefaultAsync(ct)
                .ConfigureAwait(false);
            var historyAt = await davContext.HistoryItems.AsNoTracking()
                .Where(h => h.Id == claimedId)
                .Select(h => (DateTime?)h.CreatedAt)
                .SingleOrDefaultAsync(ct)
                .ConfigureAwait(false);

            if (queuedAt is not null || historyAt is not null)
            {
                claim.State = "submitted";
                claim.SubmittedAt ??= queuedAt ?? historyAt ?? DateTime.UtcNow;
                claim.Error = null;
                adopted++;
                Stamp(claim);
                continue;
            }

            // History cleanup may remove the row while mounted content still
            // carries its id. That proves AddFile succeeded, but without history
            // the normal reconciler cannot safely reconstruct the outcome.
            var hasMountedContent = await davContext.Items.AsNoTracking()
                .AnyAsync(i => i.HistoryItemId == claimedId, ct)
                .ConfigureAwait(false);
            if (hasMountedContent)
            {
                MarkAmbiguous(
                    claim,
                    $"Submission {claimedId} produced mounted content but its history is unavailable; " +
                    "refusing to resubmit.");
                ambiguous++;
                continue;
            }

            var conflictingQueueId = await davContext.QueueItems.AsNoTracking()
                .Where(q => q.Category == release.TargetCategory
                            && q.FileName == release.QueueFileName
                            && q.Id != claimedId)
                .Select(q => (Guid?)q.Id)
                .FirstOrDefaultAsync(ct)
                .ConfigureAwait(false);
            if (conflictingQueueId is not null)
            {
                MarkAmbiguous(
                    claim,
                    $"Another NzbDAV job ({conflictingQueueId}) uses queue key " +
                    $"({release.TargetCategory}, {release.QueueFileName}); refusing to resubmit or replace it.");
                ambiguous++;
                continue;
            }

            // Nothing crossed the queue boundary (or it was rolled back). Keep
            // the same claimed id so the retry remains exactly identifiable.
            claim.State = "pending";
            claim.Error = null;
            retried++;
            Stamp(claim);
        }

        await migrationContext.SaveChangesAsync(ct).ConfigureAwait(false);

        if (adopted + retried + ambiguous > 0)
        {
            Log.Information(
                "Recovered migration submission claims: {Adopted} adopted, {Retried} safe to retry, " +
                "{Ambiguous} refused as ambiguous",
                adopted, retried, ambiguous);
        }

        return new SubmissionRecoverySummary
        {
            Adopted = adopted,
            Retried = retried,
            Ambiguous = ambiguous,
        };
    }

    private static void MarkAmbiguous(
        MigrationSubmission claim,
        string error)
    {
        claim.State = "failed";
        claim.Error = error;
        Stamp(claim);
        Log.Error("Migration release {StoreRef} has an ambiguous submission claim: {Error}",
            claim.StoreRef, error);
    }

    private static void Stamp(MigrationSubmission claim) =>
        claim.UpdatedAt = DateTime.UtcNow;
}
