using Microsoft.EntityFrameworkCore;
using NzbWebDAV.UsenetMigration.Naming;
using NzbWebDAV.UsenetMigration.Provenance;
using NzbWebDAV.Database.Models;
using NzbWebDAV.Database.Models.UsenetMigration;

namespace NzbWebDAV.Tests.UsenetMigration;

public class AlreadyMigratedDetectorTests
{
    [Fact]
    public async Task CompleteLiveMatch_IsRecordedAndIdempotent()
    {
        await using var h = await MigrationTestHarness.CreateAsync();
        var nzoId = Guid.NewGuid();
        await SeedMountedFilesAsync(h, nzoId, ("Episode.mkv", 1_000), ("Episode.srt", 100));
        var candidate = Candidate(
            File("tv/Show/Episode.mkv", 1_000),
            File("tv/Show/Episode.srt", 100));

        await using (var migration = h.Mig())
        await using (var dav = h.Dav())
        {
            var detected = await new AlreadyMigratedDetector()
                .DetectAndRecordAsync([candidate], migration, dav);
            Assert.Contains("store-1", detected);
        }

        // A repeated scan validates and updates the same records rather than duplicating them.
        await using (var migration = h.Mig())
        await using (var dav = h.Dav())
        {
            var detected = await new AlreadyMigratedDetector()
                .DetectAndRecordAsync([candidate], migration, dav);
            Assert.Contains("store-1", detected);
        }

        await using var check = h.Mig();
        var release = await check.MigratedReleases.SingleAsync();
        Assert.Equal(2, release.ExpectedFileCount);
        Assert.Equal(2, release.MappedFileCount);
        Assert.Equal(2, await check.MigratedFiles.CountAsync());
        Assert.Equal(1, await check.MigrationRuns.CountAsync());
    }

    [Fact]
    public async Task PartialLiveMatch_IsNotAlreadyMigrated()
    {
        await using var h = await MigrationTestHarness.CreateAsync();
        await SeedMountedFilesAsync(h, Guid.NewGuid(), ("Episode.mkv", 1_000));
        var candidate = Candidate(
            File("tv/Show/Episode.mkv", 1_000),
            File("tv/Show/Episode.srt", 100));

        await using var migration = h.Mig();
        await using var dav = h.Dav();
        var detected = await new AlreadyMigratedDetector()
            .DetectAndRecordAsync([candidate], migration, dav);

        Assert.DoesNotContain("store-1", detected);
        Assert.False(await migration.MigratedReleases.AnyAsync());
        Assert.False(await migration.MigrationRuns.AnyAsync());
    }

    private static AlreadyMigratedCandidate Candidate(params MigrationReleaseFile[] files) =>
        new()
        {
            StoreRef = "store-1",
            TargetCategory = "tv",
            JobName = "Show",
            Files = files,
        };

    private static MigrationReleaseFile File(string virtualPath, long size)
    {
        var name = Path.GetFileName(virtualPath);
        return new MigrationReleaseFile
        {
            StoreRef = "store-1",
            MetaPath = virtualPath + ".meta",
            VirtualPath = virtualPath,
            FileName = name,
            NormalisedName = MatchKey.ForLeaf(name),
            FileSize = size,
        };
    }

    private static async Task SeedMountedFilesAsync(
        MigrationTestHarness h,
        Guid nzoId,
        params (string name, long size)[] files)
    {
        await using var dav = h.Dav();
        var category = DavItem.New(
            Guid.NewGuid(), DavItem.ContentFolder, "tv", null,
            DavItem.ItemType.Directory, DavItem.ItemSubType.Directory,
            null, null, historyItemId: null, fileBlobId: null);
        var release = DavItem.New(
            Guid.NewGuid(), category, "Show", null,
            DavItem.ItemType.Directory, DavItem.ItemSubType.Directory,
            null, null, historyItemId: null, fileBlobId: null);
        dav.Items.AddRange(category, release);
        foreach (var (name, size) in files)
        {
            dav.Items.Add(DavItem.New(
                Guid.NewGuid(), release, name, size,
                DavItem.ItemType.UsenetFile, DavItem.ItemSubType.NzbFile,
                null, null, historyItemId: null, fileBlobId: null, nzbBlobId: nzoId));
        }
        await dav.SaveChangesAsync();
    }
}
