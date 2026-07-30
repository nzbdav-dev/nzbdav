using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NzbWebDAV.UsenetMigration.Triage;
using NzbWebDAV.Database;
using NzbWebDAV.Database.Models.UsenetMigration;

namespace NzbWebDAV.UsenetMigration.Runner;

/// <summary>
/// Re-runs collision detection over the already-scanned release set after
/// an include/exclude edit, without re-reading a single store. Collisions are a
/// property of the included set, so toggling one release can flip another's
/// verdict; this keeps the persisted verdicts and pending submissions consistent
/// with the current include flags.
///
/// It only touches the four collision reason codes (<see cref="LiveCollisions.CollisionReasons"/>) —
/// the per-release "base" reasons decided at scan (store health, category mapping,
/// encryption, …) are preserved verbatim. A change that alters base reasons (e.g.
/// re-mapping a category) requires a re-scan, not this recompute.
/// </summary>
public sealed class CollisionRecomputer(UsenetMigrationStore store)
{
    /// <summary>Test seam for the live NzbDAV context; production leaves it null.</summary>
    internal Func<DavDatabaseContext>? DavContextFactory { get; set; }

    public async Task RecomputeAsync(CancellationToken ct = default)
    {
        await using var ctx = store.NewContext();
        var releases = await ctx.Releases.ToListAsync(ct).ConfigureAwait(false);

        // Strip collision reasons back to the scan-time base reasons.
        var baseReasons = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var r in releases)
        {
            var reasons = DeserializeReasons(r.VerdictReasons)
                .Where(code => !LiveCollisions.CollisionReasons.Contains(code))
                .ToList();
            baseReasons[r.StoreRef] = reasons;
        }

        // Candidate set: included, not already Red on base reasons, mapped.
        var byStoreRef = releases.ToDictionary(r => r.StoreRef, r => r, StringComparer.Ordinal);
        var candidates = new List<CollisionCandidate>();
        foreach (var r in releases)
        {
            var baseVerdict = VerdictReason.VerdictFor(baseReasons[r.StoreRef]);
            if (!r.Included || baseVerdict == Verdict.Red || string.IsNullOrEmpty(r.TargetCategory) ||
                baseReasons[r.StoreRef].Contains(VerdictReason.AlreadyMigrated))
                continue;

            candidates.Add(new CollisionCandidate
            {
                StoreRef = r.StoreRef,
                TargetCategory = r.TargetCategory!,
                QueueFileName = r.QueueFileName,
                JobName = r.JobName,
                SubmitFileName = r.SubmitFileName,
            });
        }

        // Merge fresh findings (local passes + live pass) into the base reasons.
        var merged = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var (storeRef, reasons) in baseReasons)
            merged[storeRef] = new List<string>(reasons);

        void Add(IReadOnlyDictionary<string, List<CollisionFinding>> findings)
        {
            foreach (var (storeRef, list) in findings)
            {
                if (!merged.TryGetValue(storeRef, out var reasons)) continue;
                foreach (var f in list)
                    if (!reasons.Contains(f.Reason))
                        reasons.Add(f.Reason);
            }
        }

        Add(CollisionDetector.Detect(candidates).FindingsByStoreRef);

        var (queueKeys, contentPaths) = LiveCollisions.LoadSets(DavContextFactory);
        Add(CollisionDetector.DetectAgainstExisting(
            candidates,
            (cat, qfn) => queueKeys.Contains(LiveCollisions.Key(cat, qfn)),
            (cat, job) => contentPaths.Contains($"/content/{cat}/{job}")));

        // Write back verdicts and reconcile the pending-submission set.
        var existingSubmissions = await ctx.Submissions
            .ToDictionaryAsync(s => s.StoreRef, s => s, ct)
            .ConfigureAwait(false);

        foreach (var r in releases)
        {
            var reasons = merged[r.StoreRef];
            var verdict = VerdictReason.VerdictFor(reasons);
            r.Verdict = verdict switch
            {
                Verdict.Green => "green",
                Verdict.Amber => "amber",
                _ => "red",
            };
            r.VerdictReasons = JsonSerializer.Serialize(reasons);
            r.CollisionGroupKey = string.IsNullOrEmpty(r.TargetCategory) ? null : $"{r.TargetCategory} {r.JobName}";

            var shouldSubmit = r.Included && verdict != Verdict.Red &&
                               !reasons.Contains(VerdictReason.AlreadyMigrated);
            existingSubmissions.TryGetValue(r.StoreRef, out var submission);

            if (shouldSubmit && submission is null)
            {
                ctx.Submissions.Add(new MigrationSubmission
                {
                    StoreRef = r.StoreRef,
                    State = "pending",
                    UpdatedAt = DateTime.UtcNow,
                });
            }
            else if (!shouldSubmit && submission is { State: "pending" })
            {
                // Only a not-yet-submitted row is safe to drop; anything in flight
                // stays and is handled by the reconciler.
                ctx.Submissions.Remove(submission);
            }
        }

        await ctx.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private static IEnumerable<string> DeserializeReasons(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<string>();
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
    }
}
