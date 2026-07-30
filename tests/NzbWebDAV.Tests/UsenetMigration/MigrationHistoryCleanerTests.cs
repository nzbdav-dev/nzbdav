using Microsoft.EntityFrameworkCore;
using NzbWebDAV.UsenetMigration.Runner;
using NzbWebDAV.Database.Models;
using NzbWebDAV.Database.Models.UsenetMigration;

namespace NzbWebDAV.Tests.UsenetMigration;

public class MigrationHistoryCleanerTests
{
    [Fact]
    public async Task Cleanup_RemovesHistory_PreservesContent_AndIsIdempotent()
    {
        await using var h = await MigrationTestHarness.CreateAsync();
        var nzoId = Guid.NewGuid();
        await SeedCompletedSubmissionAsync(h, "store-1", nzoId.ToString());

        await using (var dav = h.Dav())
        {
            dav.HistoryItems.Add(new HistoryItem
            {
                Id = nzoId,
                CreatedAt = DateTime.UtcNow,
                FileName = "Movie.nzb",
                JobName = "Movie",
                Category = "movies",
                DownloadStatus = HistoryItem.DownloadStatusOption.Completed,
                NzbBlobId = nzoId,
            });
            dav.Items.Add(DavItem.New(
                Guid.NewGuid(), DavItem.ContentFolder, "Movie.mkv", 1_000,
                DavItem.ItemType.UsenetFile, DavItem.ItemSubType.NzbFile,
                null, null, historyItemId: nzoId, fileBlobId: null, nzbBlobId: nzoId));
            await dav.SaveChangesAsync();
        }

        var cleaner = new MigrationHistoryCleaner(h.Store) { DavContextFactory = h.DavFactory };
        var first = await cleaner.CleanAsync();

        Assert.Equal(1, first.Eligible);
        Assert.Equal(1, first.Removed);
        Assert.Equal(0, first.AlreadyAbsent);
        await using (var dav = h.Dav())
        {
            Assert.False(await dav.HistoryItems.AnyAsync(h => h.Id == nzoId));
            Assert.True(await dav.Items.AnyAsync(i => i.NzbBlobId == nzoId));
            var cleanup = await dav.HistoryCleanupItems.SingleAsync(i => i.Id == nzoId);
            Assert.False(cleanup.DeleteMountedFiles);
        }
        await using (var migration = h.Mig())
        {
            var submission = await migration.Submissions.SingleAsync();
            Assert.Equal("completed", submission.State);
            Assert.NotNull(submission.HistoryClearedAt);
        }

        var second = await cleaner.CleanAsync();
        Assert.Equal(0, second.Eligible);
        Assert.Equal(0, second.Removed);
    }

    [Fact]
    public async Task Cleanup_MarksHistoryAlreadyRemovedByRetention()
    {
        await using var h = await MigrationTestHarness.CreateAsync();
        await SeedCompletedSubmissionAsync(h, "store-1", Guid.NewGuid().ToString());

        var cleaner = new MigrationHistoryCleaner(h.Store) { DavContextFactory = h.DavFactory };
        var summary = await cleaner.CleanAsync();

        Assert.Equal(1, summary.Eligible);
        Assert.Equal(0, summary.Removed);
        Assert.Equal(1, summary.AlreadyAbsent);
        await using var migration = h.Mig();
        Assert.NotNull((await migration.Submissions.SingleAsync()).HistoryClearedAt);
    }

    private static async Task SeedCompletedSubmissionAsync(
        MigrationTestHarness h, string storeRef, string nzoId)
    {
        await using var migration = h.Mig();
        migration.Releases.Add(new MigrationRelease
        {
            StoreRef = storeRef,
            StoreBasename = "Movie",
            SubmitFileName = "Movie",
            QueueFileName = "Movie.nzb",
            JobName = "Movie",
            TargetCategory = "movies",
            Verdict = "green",
            VerdictReasons = "[]",
            ScannedAt = DateTime.UtcNow,
        });
        migration.Submissions.Add(new MigrationSubmission
        {
            StoreRef = storeRef,
            NzoId = nzoId,
            State = "completed",
            CompletedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });
        await migration.SaveChangesAsync();
    }
}
