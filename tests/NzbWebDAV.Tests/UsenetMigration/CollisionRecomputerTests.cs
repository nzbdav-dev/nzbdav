using Microsoft.EntityFrameworkCore;
using NzbWebDAV.UsenetMigration.Runner;
using NzbWebDAV.UsenetMigration.Triage;
using NzbWebDAV.Database.Models.UsenetMigration;

namespace NzbWebDAV.Tests.UsenetMigration;

public class CollisionRecomputerTests
{
    /// <summary>
    /// Excluding one of a queue-key-colliding pair must clear the OTHER release's
    /// collision — it becomes submittable and gains a pending submission —
    /// all without a re-scan.
    /// </summary>
    [Fact]
    public async Task ExcludingOneOfCollidingPair_FlipsOtherToSubmittable()
    {
        await using var h = await MigrationTestHarness.CreateAsync();
        await SeedCollidingPairAsync(h, aReasons: new[] { VerdictReason.QueueKeyCollision },
            bReasons: new[] { VerdictReason.QueueKeyCollision });

        var recomputer = new CollisionRecomputer(h.Store) { DavContextFactory = h.DavFactory };

        // Both included ⇒ both stay Red, no pending submissions.
        await recomputer.RecomputeAsync();
        await using (var mig = h.Mig())
        {
            Assert.Equal("red", (await mig.Releases.SingleAsync(r => r.StoreRef == "a")).Verdict);
            Assert.Equal("red", (await mig.Releases.SingleAsync(r => r.StoreRef == "b")).Verdict);
            Assert.False(await mig.Submissions.AnyAsync());
        }

        // Exclude B, recompute.
        await using (var mig = h.Mig())
        {
            (await mig.Releases.SingleAsync(r => r.StoreRef == "b")).Included = false;
            await mig.SaveChangesAsync();
        }
        await recomputer.RecomputeAsync();

        await using (var mig = h.Mig())
        {
            var a = await mig.Releases.SingleAsync(r => r.StoreRef == "a");
            Assert.Equal("green", a.Verdict);
            Assert.DoesNotContain(VerdictReason.QueueKeyCollision, a.VerdictReasons);
            Assert.True(await mig.Submissions.AnyAsync(s => s.StoreRef == "a" && s.State == "pending"));
            Assert.False(await mig.Submissions.AnyAsync(s => s.StoreRef == "b"));
        }
    }

    /// <summary>
    /// Recompute must strip ONLY the four collision codes and preserve the
    /// scan-time base reasons (here: <c>encrypted</c> ⇒ still Amber).
    /// </summary>
    [Fact]
    public async Task PreservesBaseReasons_StripsOnlyCollisionCodes()
    {
        await using var h = await MigrationTestHarness.CreateAsync();
        await SeedCollidingPairAsync(h,
            aReasons: new[] { VerdictReason.Encrypted, VerdictReason.QueueKeyCollision },
            bReasons: new[] { VerdictReason.QueueKeyCollision });

        var recomputer = new CollisionRecomputer(h.Store) { DavContextFactory = h.DavFactory };

        await using (var mig = h.Mig())
        {
            (await mig.Releases.SingleAsync(r => r.StoreRef == "b")).Included = false;
            await mig.SaveChangesAsync();
        }
        await recomputer.RecomputeAsync();

        await using var check = h.Mig();
        var a = await check.Releases.SingleAsync(r => r.StoreRef == "a");
        Assert.Equal("amber", a.Verdict);
        Assert.Contains(VerdictReason.Encrypted, a.VerdictReasons);
        Assert.DoesNotContain(VerdictReason.QueueKeyCollision, a.VerdictReasons);
        // Amber is still submittable.
        Assert.True(await check.Submissions.AnyAsync(s => s.StoreRef == "a" && s.State == "pending"));
    }

    [Fact]
    public async Task AlreadyMigrated_IsNeitherCollisionCandidateNorSubmission()
    {
        await using var h = await MigrationTestHarness.CreateAsync();
        await using (var mig = h.Mig())
        {
            mig.Releases.Add(Release("existing", new[] { VerdictReason.AlreadyMigrated }));
            await mig.SaveChangesAsync();
        }

        await new CollisionRecomputer(h.Store) { DavContextFactory = h.DavFactory }.RecomputeAsync();

        await using var check = h.Mig();
        var release = await check.Releases.SingleAsync();
        Assert.Equal("green", release.Verdict);
        Assert.Contains(VerdictReason.AlreadyMigrated, release.VerdictReasons);
        Assert.DoesNotContain(VerdictReason.MountFolderExists, release.VerdictReasons);
        Assert.False(await check.Submissions.AnyAsync());
    }

    private static async Task SeedCollidingPairAsync(
        MigrationTestHarness h, string[] aReasons, string[] bReasons)
    {
        await using var mig = h.Mig();
        mig.Releases.Add(Release("a", aReasons));
        mig.Releases.Add(Release("b", bReasons));
        await mig.SaveChangesAsync();
    }

    private static MigrationRelease Release(string storeRef, string[] reasons) => new()
    {
        StoreRef = storeRef,
        StoreBasename = "Show.S01E01",
        SubmitFileName = "Show.S01E01",
        QueueFileName = "Show.S01E01.nzb",
        JobName = "Show.S01E01",
        TargetCategory = "tv",
        CollisionGroupKey = "tv Show.S01E01",
        Verdict = "red",
        VerdictReasons = System.Text.Json.JsonSerializer.Serialize(reasons),
        Included = true,
        ScannedAt = DateTime.UtcNow,
    };
}
