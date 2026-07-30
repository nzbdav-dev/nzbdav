using Microsoft.EntityFrameworkCore;
using NzbWebDAV.UsenetMigration.Naming;
using NzbWebDAV.UsenetMigration.Symlinks;
using NzbWebDAV.Config;
using NzbWebDAV.Database.Models;
using NzbWebDAV.Database.Models.UsenetMigration;
using NzbWebDAV.WebDav;

namespace NzbWebDAV.Tests.UsenetMigration;

/// <summary>
/// End-to-end coverage of <see cref="SymlinkPlanner"/> over both databases: it
/// exercises <see cref="SymlinkMatcher.LoadLeavesAsync"/> against the live Dav DB
/// (leaves found by durable NzbBlobId after HistoryItemId cleanup), conservative
/// matching, symlink classifications, NewTarget == GetTargetPath, and that
/// NewDavItemId is persisted back onto matched ReleaseFiles.
/// </summary>
public class SymlinkPlannerTests
{
    private static async Task SeedReleaseAsync(
        MigrationTestHarness h, string storeRef, string virtualPath, Guid? nzoId, string state)
    {
        var fileName = Path.GetFileName(virtualPath);
        await using var mig = h.Mig();
        mig.Releases.Add(new MigrationRelease
        {
            StoreRef = storeRef,
            StoreBasename = fileName,
            SubmitFileName = fileName,
            QueueFileName = fileName + ".nzb",
            JobName = fileName,
            TargetCategory = "tv",
            Verdict = "green",
            VerdictReasons = "[]",
            ScannedAt = DateTime.UtcNow,
        });
        mig.ReleaseFiles.Add(new MigrationReleaseFile
        {
            StoreRef = storeRef,
            MetaPath = virtualPath + ".meta",
            VirtualPath = virtualPath,
            FileName = fileName,
            NormalisedName = MatchKey.ForLeaf(fileName),
        });
        if (nzoId is { } id)
            mig.Submissions.Add(new MigrationSubmission
            {
                StoreRef = storeRef,
                NzoId = id.ToString(),
                State = state,
                UpdatedAt = DateTime.UtcNow,
            });
        await mig.SaveChangesAsync();
    }

    private static async Task SeedLeafAsync(MigrationTestHarness h, Guid nzoId, string leafName, Guid davId)
    {
        await using var dav = h.Dav();
        dav.Items.Add(DavItem.New(
            davId, DavItem.ContentFolder, leafName, 1000,
            DavItem.ItemType.UsenetFile, DavItem.ItemSubType.NzbFile,
            releaseDate: null, lastHealthCheck: null, historyItemId: null, fileBlobId: null,
            nzbBlobId: nzoId));
        await dav.SaveChangesAsync();
    }

    [Fact]
    public async Task Plan_ClassifiesAllFour_ComputesTargets_AndPersistsNewDavItemId()
    {
        await using var h = await MigrationTestHarness.CreateAsync();
        var config = new ConfigManager();
        var mountDir = config.GetRcloneMountDir();

        var (nzo1, dav1) = (Guid.NewGuid(), Guid.NewGuid());
        var (nzo2, dav2) = (Guid.NewGuid(), Guid.NewGuid());

        // store-1: completed; live leaf name matches ⇒ exact.
        await SeedReleaseAsync(h, "store-1", "tv/Show.S01/Show.S01E01.mkv", nzo1, "history_cleared");
        await SeedLeafAsync(h, nzo1, "Show.S01E01.mkv", dav1);

        // store-2: completed; live leaf name differs (Altmount deobfuscated, NzbDAV
        // didn't) ⇒ single-leaf fallback.
        await SeedReleaseAsync(h, "store-2", "tv/Movie/altmount-name.mkv", nzo2, "completed");
        await SeedLeafAsync(h, nzo2, "obfuscated-xyz.mkv", dav2);

        // store-3: scanned but not completed ⇒ orphan (correlates, no leaf).
        await SeedReleaseAsync(h, "store-3", "tv/Unmigrated/unmig.mkv", nzoId: null, state: "pending");

        await h.Store.UpdateSessionAsync(s => { s.SymlinkLibraryRoot = "/lib"; s.Status = "linked"; });

        var links = new[]
        {
            new SymlinkPair("/lib/tv/a.mkv", "/mnt/altmount/tv/Show.S01/Show.S01E01.mkv"), // rewrite (exact)
            new SymlinkPair("/lib/tv/b.mkv", "/mnt/altmount/tv/Movie/altmount-name.mkv"),  // rewrite (fallback)
            new SymlinkPair("/lib/tv/c.mkv", "/mnt/altmount/tv/Unmigrated/unmig.mkv"),      // orphan
            new SymlinkPair("/lib/tv/d.mkv", "/somewhere/.ids/a/b/c/d/e/zzz"),              // already-nzbdav
            new SymlinkPair("/lib/tv/e.mkv", "/mnt/other/random/notours.mkv"),             // not-altmount
        };

        var planner = new SymlinkPlanner(h.Store, config)
        {
            DavContextFactory = h.DavFactory,
            SymlinkEnumerator = (_, _) => new LibraryWalkResult(links, []),
            LibraryRootValidator = root => root,
        };

        var summary = await planner.PlanAsync();

        Assert.Equal(2, summary.Rewrite);
        Assert.Equal(1, summary.Orphan);
        Assert.Equal(1, summary.AlreadyNzbdav);
        Assert.Equal(1, summary.NotAltmount);
        Assert.Equal(5, summary.Total);

        await using var mig = h.Mig();
        var rows = await mig.SymlinkRewrites.ToListAsync();

        var a = rows.Single(r => r.SymlinkPath == "/lib/tv/a.mkv");
        Assert.Equal("rewrite", a.Status);
        Assert.Equal("exact", a.MatchMethod);
        Assert.Equal("store-1", a.StoreRef);
        Assert.Equal(DatabaseStoreSymlinkFile.GetTargetPath(dav1, mountDir, '/'), a.NewTarget);

        var b = rows.Single(r => r.SymlinkPath == "/lib/tv/b.mkv");
        Assert.Equal("rewrite", b.Status);
        Assert.Equal("single-leaf-fallback", b.MatchMethod);
        Assert.Equal(DatabaseStoreSymlinkFile.GetTargetPath(dav2, mountDir, '/'), b.NewTarget);

        Assert.Equal("orphan", rows.Single(r => r.SymlinkPath == "/lib/tv/c.mkv").Status);
        Assert.Equal("already-nzbdav", rows.Single(r => r.SymlinkPath == "/lib/tv/d.mkv").Status);
        Assert.Equal("not-altmount", rows.Single(r => r.SymlinkPath == "/lib/tv/e.mkv").Status);

        // NewDavItemId persisted for matched files, left null for the unmigrated one.
        var files = await mig.ReleaseFiles.ToListAsync();
        Assert.Equal(dav1.ToString(), files.Single(f => f.StoreRef == "store-1").NewDavItemId);
        Assert.Equal(dav2.ToString(), files.Single(f => f.StoreRef == "store-2").NewDavItemId);
        Assert.Null(files.Single(f => f.StoreRef == "store-3").NewDavItemId);
    }

    [Fact]
    public async Task Plan_IsReRunnable_ClearsPriorPlan()
    {
        await using var h = await MigrationTestHarness.CreateAsync();
        var config = new ConfigManager();
        await h.Store.UpdateSessionAsync(s => { s.SymlinkLibraryRoot = "/lib"; s.Status = "linked"; });

        var planner = new SymlinkPlanner(h.Store, config)
        {
            DavContextFactory = h.DavFactory,
            SymlinkEnumerator = (_, _) => new LibraryWalkResult(
                [new SymlinkPair("/lib/x.mkv", "/mnt/other/x.mkv")],
                []),
            LibraryRootValidator = root => root,
        };

        await planner.PlanAsync();
        await planner.PlanAsync(); // second run must not duplicate rows

        await using var mig = h.Mig();
        Assert.Equal(1, await mig.SymlinkRewrites.CountAsync());
    }

    [Fact]
    public async Task Plan_TraversalFailure_LeavesPriorPlanIntact()
    {
        await using var h = await MigrationTestHarness.CreateAsync();
        await h.Store.UpdateSessionAsync(s =>
        {
            s.SymlinkLibraryRoot = "/lib";
            s.Status = "linked";
        });

        await using (var seed = h.Mig())
        {
            seed.SymlinkRewrites.Add(new MigrationSymlinkRewrite
            {
                SymlinkPath = "/lib/prior.mkv",
                OldTarget = "/mnt/altmount/tv/prior.mkv",
                NewTarget = "/mnt/nzbdav/.ids/prior",
                Status = "rewrite",
                UpdatedAt = DateTime.UtcNow,
            });
            await seed.SaveChangesAsync();
        }

        var planner = new SymlinkPlanner(h.Store, new ConfigManager())
        {
            DavContextFactory = h.DavFactory,
            SymlinkEnumerator = (_, _) => throw new IOException("simulated traversal failure"),
            LibraryRootValidator = root => root,
        };

        var error = await Assert.ThrowsAsync<IOException>(() => planner.PlanAsync());

        Assert.Contains("traversal failure", error.Message);
        await using var mig = h.Mig();
        var prior = Assert.Single(await mig.SymlinkRewrites.ToListAsync());
        Assert.Equal("/lib/prior.mkv", prior.SymlinkPath);
        Assert.Equal("rewrite", prior.Status);
    }

    [Fact]
    public async Task Plan_UnreadableLinks_ArePlannedNotDropped()
    {
        await using var h = await MigrationTestHarness.CreateAsync();
        await h.Store.UpdateSessionAsync(s =>
        {
            s.SymlinkLibraryRoot = "/lib";
            s.Status = "linked";
        });

        var planner = new SymlinkPlanner(h.Store, new ConfigManager())
        {
            DavContextFactory = h.DavFactory,
            SymlinkEnumerator = (_, _) => new LibraryWalkResult(
                [new SymlinkPair("/lib/readable.mkv", "/mnt/other/readable.mkv")],
                [new UnreadableLink("/lib/unreadable.mkv", "Permission denied")]),
            LibraryRootValidator = root => root,
        };

        var summary = await planner.PlanAsync();

        Assert.Equal(1, summary.NotAltmount);
        Assert.Equal(1, summary.Unreadable);
        Assert.Equal(2, summary.Total);
        await using var mig = h.Mig();
        var unreadable = await mig.SymlinkRewrites.SingleAsync(r => r.Status == "unreadable");
        Assert.Equal("/lib/unreadable.mkv", unreadable.SymlinkPath);
        Assert.Equal("", unreadable.OldTarget);
        Assert.Null(unreadable.NewTarget);
        Assert.Equal("Permission denied", unreadable.Error);
    }

    [Fact]
    public async Task Plan_UsesValidatedProvenanceFromAnEarlierRun()
    {
        await using var h = await MigrationTestHarness.CreateAsync();
        var config = new ConfigManager();
        var davItemId = Guid.NewGuid();
        var nzoId = Guid.NewGuid();
        await SeedLeafAsync(h, nzoId, "Earlier.mkv", davItemId);

        await using (var mig = h.Mig())
        {
            var run = new MigrationRun
            {
                SourceType = "altmount",
                Status = "completed",
                StartedAt = DateTime.UtcNow.AddMinutes(-5),
                CompletedAt = DateTime.UtcNow,
            };
            mig.MigrationRuns.Add(run);
            await mig.SaveChangesAsync();
            var release = new MigratedRelease
            {
                SourceType = "altmount",
                SourceReleaseId = "earlier-store",
                FirstRunId = run.Id,
                LastRunId = run.Id,
                NzoId = nzoId.ToString(),
                ExpectedFileCount = 1,
                MappedFileCount = 1,
                MigratedAt = DateTime.UtcNow,
                LastVerifiedAt = DateTime.UtcNow,
            };
            mig.MigratedReleases.Add(release);
            await mig.SaveChangesAsync();
            mig.MigratedFiles.Add(new MigratedFile
            {
                MigratedReleaseId = release.Id,
                VirtualPath = "tv/Earlier/Earlier.mkv",
                NormalisedRelativePath = MatchKey.ForRelativePath("tv/Earlier/Earlier.mkv"),
                NormalisedName = MatchKey.ForLeaf("Earlier.mkv"),
                FileSize = 1_000,
                DavItemId = davItemId,
                NzbBlobId = nzoId,
                MatchMethod = "exact",
                LastVerifiedAt = DateTime.UtcNow,
            });
            await mig.SaveChangesAsync();
        }

        await h.Store.UpdateSessionAsync(s =>
        {
            s.SymlinkLibraryRoot = "/lib";
            s.Status = "linked";
        });
        var planner = new SymlinkPlanner(h.Store, config)
        {
            DavContextFactory = h.DavFactory,
            SymlinkEnumerator = (_, _) => new LibraryWalkResult(
                [new SymlinkPair("/lib/earlier.mkv", "/mnt/altmount/tv/Earlier/Earlier.mkv")],
                []),
            LibraryRootValidator = root => root,
        };

        var summary = await planner.PlanAsync();

        Assert.Equal(1, summary.Rewrite);
        await using var resultContext = h.Mig();
        var rewrite = await resultContext.SymlinkRewrites.SingleAsync();
        Assert.Equal("provenance", rewrite.MatchMethod);
        Assert.Equal("earlier-store", rewrite.StoreRef);
        Assert.Equal(
            DatabaseStoreSymlinkFile.GetTargetPath(davItemId, config.GetRcloneMountDir(), '/'),
            rewrite.NewTarget);
    }

    [SkippableFact]
    public async Task Plan_RejectsSymlinkedLibraryRoot()
    {
        Skip.IfNot(OperatingSystem.IsLinux(), "Directory symlink behavior is validated on the Linux deployment platform.");

        await using var h = await MigrationTestHarness.CreateAsync();
        var realRoot = Path.Combine(Path.GetTempPath(), $"altmig-library-{Guid.NewGuid():N}");
        var linkedRoot = Path.Combine(Path.GetTempPath(), $"altmig-library-link-{Guid.NewGuid():N}");
        Directory.CreateDirectory(realRoot);
        Directory.CreateSymbolicLink(linkedRoot, realRoot);

        try
        {
            await h.Store.UpdateSessionAsync(s => s.SymlinkLibraryRoot = linkedRoot);

            var error = await Assert.ThrowsAsync<IOException>(() =>
                new SymlinkPlanner(h.Store, new ConfigManager()).PlanAsync());

            Assert.Contains("Library Root", error.Message);
            Assert.Contains("symbolic link", error.Message);
        }
        finally
        {
            Directory.Delete(linkedRoot);
            Directory.Delete(realRoot);
        }
    }
}
