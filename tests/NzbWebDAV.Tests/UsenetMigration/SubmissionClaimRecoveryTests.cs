using Microsoft.EntityFrameworkCore;
using NzbWebDAV.Database.Models;
using NzbWebDAV.Database.Models.UsenetMigration;
using NzbWebDAV.UsenetMigration.Runner;

namespace NzbWebDAV.Tests.UsenetMigration;

public sealed class SubmissionClaimRecoveryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Recover_AdoptsExactClaimedIdFromQueueOrHistory(bool useHistory)
    {
        await using var h = await MigrationTestHarness.CreateAsync();
        var claimedId = Guid.NewGuid();
        await SeedClaimAsync(h, claimedId);

        await using (var dav = h.Dav())
        {
            if (useHistory)
                dav.HistoryItems.Add(CreateHistoryItem(claimedId));
            else
                dav.QueueItems.Add(CreateQueueItem(claimedId));
            await dav.SaveChangesAsync();
        }

        var summary = await SubmissionClaimRecovery.RecoverAsync(h.Store, h.DavFactory);

        Assert.Equal(1, summary.Adopted);
        Assert.Equal(0, summary.Retried);
        Assert.Equal(0, summary.Ambiguous);
        await using var check = h.Mig();
        var submission = await check.Submissions.SingleAsync();
        Assert.Equal("submitted", submission.State);
        Assert.Equal(claimedId.ToString(), submission.NzoId);
        Assert.NotNull(submission.SubmittedAt);
    }

    [Fact]
    public async Task Recover_WhenNoJobExists_RetriesSameClaimedId()
    {
        await using var h = await MigrationTestHarness.CreateAsync();
        var claimedId = Guid.NewGuid();
        await SeedClaimAsync(h, claimedId);

        var summary = await SubmissionClaimRecovery.RecoverAsync(h.Store, h.DavFactory);

        Assert.Equal(1, summary.Retried);
        await using var check = h.Mig();
        var submission = await check.Submissions.SingleAsync();
        Assert.Equal("pending", submission.State);
        Assert.Equal(claimedId.ToString(), submission.NzoId);
        Assert.Null(submission.Error);
    }

    [Fact]
    public async Task Recover_RefusesDifferentJobWithSameQueueKey()
    {
        await using var h = await MigrationTestHarness.CreateAsync();
        var claimedId = Guid.NewGuid();
        var conflictingId = Guid.NewGuid();
        await SeedClaimAsync(h, claimedId);
        await using (var dav = h.Dav())
        {
            dav.QueueItems.Add(CreateQueueItem(conflictingId));
            await dav.SaveChangesAsync();
        }

        var summary = await SubmissionClaimRecovery.RecoverAsync(h.Store, h.DavFactory);

        Assert.Equal(1, summary.Ambiguous);
        await using (var check = h.Mig())
        {
            var submission = await check.Submissions.SingleAsync();
            Assert.Equal("failed", submission.State);
            Assert.Contains("refusing to resubmit or replace", submission.Error);
        }
        await using (var davCheck = h.Dav())
            Assert.True(await davCheck.QueueItems.AnyAsync(q => q.Id == conflictingId));
    }

    [Fact]
    public async Task Recover_RefusesClaimWhoseHistoryWasClearedAfterMounting()
    {
        await using var h = await MigrationTestHarness.CreateAsync();
        var claimedId = Guid.NewGuid();
        await SeedClaimAsync(h, claimedId);
        await using (var dav = h.Dav())
        {
            dav.Items.Add(DavItem.New(
                Guid.NewGuid(),
                DavItem.ContentFolder,
                "episode.mkv",
                100,
                DavItem.ItemType.UsenetFile,
                DavItem.ItemSubType.NzbFile,
                null,
                null,
                claimedId,
                null));
            await dav.SaveChangesAsync();
        }

        var summary = await SubmissionClaimRecovery.RecoverAsync(h.Store, h.DavFactory);

        Assert.Equal(1, summary.Ambiguous);
        await using var check = h.Mig();
        var submission = await check.Submissions.SingleAsync();
        Assert.Equal("failed", submission.State);
        Assert.Contains("produced mounted content", submission.Error);
    }

    private static async Task SeedClaimAsync(MigrationTestHarness h, Guid claimedId)
    {
        await using var migration = h.Mig();
        migration.Releases.Add(new MigrationRelease
        {
            StoreRef = "store-1",
            StoreBasename = "release",
            SubmitFileName = "release",
            QueueFileName = "release.nzb",
            JobName = "release",
            TargetCategory = "tv",
            Verdict = "green",
            VerdictReasons = "[]",
            ScannedAt = DateTime.UtcNow,
        });
        migration.Submissions.Add(new MigrationSubmission
        {
            StoreRef = "store-1",
            NzoId = claimedId.ToString(),
            State = "submitting",
            Attempt = 1,
            UpdatedAt = DateTime.UtcNow,
        });
        await migration.SaveChangesAsync();
    }

    private static QueueItem CreateQueueItem(Guid id) => new()
    {
        Id = id,
        CreatedAt = DateTime.UtcNow,
        FileName = "release.nzb",
        JobName = "release",
        NzbFileSize = 100,
        TotalSegmentBytes = 100,
        Category = "tv",
        Priority = QueueItem.PriorityOption.Low,
        PostProcessing = QueueItem.PostProcessingOption.None,
    };

    private static HistoryItem CreateHistoryItem(Guid id) => new()
    {
        Id = id,
        CreatedAt = DateTime.UtcNow,
        FileName = "release.nzb",
        JobName = "release",
        Category = "tv",
        DownloadStatus = HistoryItem.DownloadStatusOption.Completed,
        TotalSegmentBytes = 100,
        DownloadTimeSeconds = 1,
    };
}
