using Microsoft.EntityFrameworkCore;
using NzbWebDAV.UsenetMigration.Naming;
using NzbWebDAV.UsenetMigration.Runner;
using NzbWebDAV.Database.Models;
using NzbWebDAV.Database.Models.UsenetMigration;

namespace NzbWebDAV.Tests.UsenetMigration;

public class SubmissionReconcilerTests
{
    /// <summary>
    /// A completed import remains successful without conflating that lifecycle
    /// state with optional SAB-history housekeeping.
    /// </summary>
    [Fact]
    public async Task Completed_RetainsHistory_AndLeavesDavItems()
    {
        await using var h = await MigrationTestHarness.CreateAsync();
        var nzoId = Guid.NewGuid();

        await SeedReleaseAndSubmissionAsync(h, "store-1", nzoId, "submitted", targetCategory: "tv");

        // Live NzbDAV: a completed history row + the DavItem it imported.
        await using (var dav = h.Dav())
        {
            dav.HistoryItems.Add(new HistoryItem
            {
                Id = nzoId,
                CreatedAt = DateTime.UtcNow,
                FileName = "Show.S01E01.nzb",
                JobName = "Show.S01E01",
                Category = "tv",
                DownloadStatus = HistoryItem.DownloadStatusOption.Completed,
            });
            var mounted = DavItem.New(
                Guid.NewGuid(), DavItem.ContentFolder, "Show.S01E01.mkv", 1_000,
                DavItem.ItemType.UsenetFile, DavItem.ItemSubType.NzbFile,
                null, null, historyItemId: nzoId, fileBlobId: null, nzbBlobId: nzoId);
            dav.Items.Add(mounted);
            await dav.SaveChangesAsync();
        }

        var reconciler = new SubmissionReconciler(h.Store) { DavContextFactory = h.DavFactory };
        var summary = await reconciler.ReconcileAsync();

        Assert.Equal(1, summary.Completed);

        // Both the history row and mounted content remain until explicit cleanup.
        await using (var dav = h.Dav())
        {
            Assert.True(await dav.HistoryItems.AnyAsync(x => x.Id == nzoId));
            Assert.True(await dav.Items.AnyAsync(i => i.HistoryItemId == nzoId));
        }

        await using (var mig = h.Mig())
        {
            var sub = await mig.Submissions.SingleAsync(s => s.StoreRef == "store-1");
            Assert.Equal("completed", sub.State);
            Assert.Equal("/content/tv/Show.S01E01", sub.MountPath);
            Assert.Equal(1, sub.DavItemCount);
            Assert.Null(sub.HistoryClearedAt);
            var provenance = await mig.MigratedReleases.SingleAsync();
            Assert.Equal("store-1", provenance.SourceReleaseId);
            Assert.Equal(1, provenance.ExpectedFileCount);
            Assert.Equal(1, provenance.MappedFileCount);
            var migratedFile = await mig.MigratedFiles.SingleAsync();
            Assert.Equal("tv/Show.S01E01/Show.S01E01.mkv", migratedFile.VirtualPath);
            Assert.Equal(nzoId, migratedFile.NzbBlobId);
        }
    }

    [Fact]
    public async Task Failed_RecordsMessage_AndKeepsHistory()
    {
        await using var h = await MigrationTestHarness.CreateAsync();
        var nzoId = Guid.NewGuid();
        await SeedReleaseAndSubmissionAsync(h, "store-1", nzoId, "submitted", targetCategory: "tv");

        await using (var dav = h.Dav())
        {
            dav.HistoryItems.Add(new HistoryItem
            {
                Id = nzoId,
                CreatedAt = DateTime.UtcNow,
                FileName = "Bad.nzb",
                JobName = "Bad",
                Category = "tv",
                DownloadStatus = HistoryItem.DownloadStatusOption.Failed,
                FailMessage = "all articles missing",
            });
            await dav.SaveChangesAsync();
        }

        var reconciler = new SubmissionReconciler(h.Store) { DavContextFactory = h.DavFactory };
        var summary = await reconciler.ReconcileAsync();

        Assert.Equal(1, summary.Failed);
        await using (var dav = h.Dav())
            Assert.True(await dav.HistoryItems.AnyAsync(x => x.Id == nzoId)); // NOT cleared
        await using (var mig = h.Mig())
        {
            var sub = await mig.Submissions.SingleAsync(s => s.StoreRef == "store-1");
            Assert.Equal("failed", sub.State);
            Assert.Equal("all articles missing", sub.Error);
        }
    }

    [Fact]
    public async Task GoneFromQueueAndHistory_MarksEvictedTerminal()
    {
        await using var h = await MigrationTestHarness.CreateAsync();
        var nzoId = Guid.NewGuid();
        await SeedReleaseAndSubmissionAsync(h, "store-1", nzoId, "submitted", targetCategory: "tv");
        // Dav DB has neither a queue item nor a history item for nzoId.

        var reconciler = new SubmissionReconciler(h.Store) { DavContextFactory = h.DavFactory };
        var summary = await reconciler.ReconcileAsync();

        Assert.Equal(1, summary.Evicted);
        await using var mig = h.Mig();
        var sub = await mig.Submissions.SingleAsync(s => s.StoreRef == "store-1");
        Assert.Equal("evicted", sub.State);
        Assert.NotNull(sub.Error);
        Assert.Contains("same NzbDAV queue key", sub.Error);
    }

    [Fact]
    public async Task StillInQueue_MarksProcessing()
    {
        await using var h = await MigrationTestHarness.CreateAsync();
        var nzoId = Guid.NewGuid();
        await SeedReleaseAndSubmissionAsync(h, "store-1", nzoId, "submitted", targetCategory: "tv");

        await using (var dav = h.Dav())
        {
            dav.QueueItems.Add(new QueueItem
            {
                Id = nzoId,
                CreatedAt = DateTime.UtcNow,
                FileName = "Show.S01E01.nzb",
                JobName = "Show.S01E01",
                Category = "tv",
                Priority = QueueItem.PriorityOption.Low,
                PostProcessing = QueueItem.PostProcessingOption.None,
            });
            await dav.SaveChangesAsync();
        }

        var reconciler = new SubmissionReconciler(h.Store) { DavContextFactory = h.DavFactory };
        var summary = await reconciler.ReconcileAsync();

        Assert.Equal(1, summary.StillProcessing);
        await using var mig = h.Mig();
        var sub = await mig.Submissions.SingleAsync(s => s.StoreRef == "store-1");
        Assert.Equal("processing", sub.State);
    }

    private static async Task SeedReleaseAndSubmissionAsync(
        MigrationTestHarness h, string storeRef, Guid nzoId, string state, string targetCategory)
    {
        await using var mig = h.Mig();
        mig.Releases.Add(new MigrationRelease
        {
            StoreRef = storeRef,
            StoreBasename = "Show.S01E01",
            SubmitFileName = "Show.S01E01",
            QueueFileName = "Show.S01E01.nzb",
            JobName = "Show.S01E01",
            TargetCategory = targetCategory,
            Verdict = "green",
            VerdictReasons = "[]",
            ScannedAt = DateTime.UtcNow,
        });
        mig.ReleaseFiles.Add(new MigrationReleaseFile
        {
            StoreRef = storeRef,
            MetaPath = "Show.S01E01.mkv.meta",
            VirtualPath = "tv/Show.S01E01/Show.S01E01.mkv",
            FileName = "Show.S01E01.mkv",
            NormalisedName = MatchKey.ForLeaf("Show.S01E01.mkv"),
            FileSize = 1_000,
        });
        mig.Submissions.Add(new MigrationSubmission
        {
            StoreRef = storeRef,
            NzoId = nzoId.ToString(),
            State = state,
            UpdatedAt = DateTime.UtcNow,
        });
        await mig.SaveChangesAsync();
    }
}
