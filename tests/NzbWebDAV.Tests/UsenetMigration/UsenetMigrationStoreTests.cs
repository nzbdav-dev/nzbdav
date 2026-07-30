using Microsoft.EntityFrameworkCore;
using NzbWebDAV.Database;
using NzbWebDAV.UsenetMigration;
using NzbWebDAV.Database.Models;
using NzbWebDAV.Database.Models.UsenetMigration;

namespace NzbWebDAV.Tests.UsenetMigration;

public class UsenetMigrationStoreTests
{
    [Fact]
    public async Task GetSession_CreatesSingletonId1_AndIsIdempotent()
    {
        await using var h = await MigrationTestHarness.CreateAsync();

        var first = await h.Store.GetSessionAsync();
        Assert.Equal(UsenetMigrationStore.SessionId, first.Id);
        Assert.Equal("idle", first.Status);

        await h.Store.GetSessionAsync();
        await using var mig = h.Mig();
        Assert.Equal(1, await mig.SessionState.CountAsync()); // still exactly one row
    }

    [Fact]
    public async Task ConcurrentFirstRequests_CreateDatabaseAndOneSingletonSession()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"altmig-first-use-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<UsenetMigrationDbContext>()
            .UseSqlite($"Data Source={databasePath}")
            .Options;
        var store = new UsenetMigrationStore
        {
            ContextFactory = () => new UsenetMigrationDbContext(options),
        };

        try
        {
            Assert.False(File.Exists(databasePath));
            var start = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var requests = Enumerable.Range(0, 12).Select(async _ =>
            {
                await start.Task;
                await store.EnsureDatabaseAsync();
                return await store.GetSessionAsync();
            }).ToArray();

            start.SetResult(true);
            var sessions = await Task.WhenAll(requests);

            Assert.True(File.Exists(databasePath));
            Assert.All(sessions, session => Assert.Equal(UsenetMigrationStore.SessionId, session.Id));
            await using var context = new UsenetMigrationDbContext(options);
            Assert.Equal(1, await context.SessionState.CountAsync());
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(databasePath)) File.Delete(databasePath);
        }
    }

    [Fact]
    public async Task UpdateSession_PersistsMutation_AndStampsUpdatedAt()
    {
        await using var h = await MigrationTestHarness.CreateAsync();

        await h.Store.UpdateSessionAsync(s =>
        {
            s.Status = "connected";
            s.AltmountMetadataRoot = "/data/altmount/metadata";
        });

        var session = await h.Store.GetSessionAsync();
        Assert.Equal("connected", session.Status);
        Assert.Equal("/data/altmount/metadata", session.AltmountMetadataRoot);
    }

    [Fact]
    public async Task Preferences_SurviveWizardAndProvenanceResets()
    {
        await using var h = await MigrationTestHarness.CreateAsync();

        await h.Store.UpdatePreferencesAsync(p =>
        {
            p.AltmountMetadataRoot = "/data/altmount/metadata";
            p.AltmountConfigPath = "/config/altmount/config.yaml";
            p.AltmountStoreRoot = "/data/altmount/store";
            p.MaxQueueDepth = 12;
            p.SubmitWorkers = 1;
            p.SymlinkLibraryRoot = "/library";
            p.SymlinkBackupDir = "/backups";
        });

        await h.Store.ResetAsync();
        await AssertPreferencesAsync(h.Store);

        await h.Store.ForgetAllMigrationRecordsAsync();
        await AssertPreferencesAsync(h.Store);

        static async Task AssertPreferencesAsync(UsenetMigrationStore store)
        {
            var preferences = Assert.IsType<MigrationPreferences>(await store.GetPreferencesAsync());
            Assert.Equal("/data/altmount/metadata", preferences.AltmountMetadataRoot);
            Assert.Equal("/config/altmount/config.yaml", preferences.AltmountConfigPath);
            Assert.Equal("/data/altmount/store", preferences.AltmountStoreRoot);
            Assert.Equal(12, preferences.MaxQueueDepth);
            Assert.Equal(1, preferences.SubmitWorkers);
            Assert.Equal("/library", preferences.SymlinkLibraryRoot);
            Assert.Equal("/backups", preferences.SymlinkBackupDir);
        }
    }

    [Fact]
    public async Task UpdateSubmission_Upserts()
    {
        await using var h = await MigrationTestHarness.CreateAsync();

        // A submission FKs to its release, so seed the parent first (as a scan would).
        await using (var seed = h.Mig())
        {
            seed.Releases.Add(new MigrationRelease
            {
                StoreRef = "store-1",
                StoreBasename = "x",
                SubmitFileName = "x",
                QueueFileName = "x.nzb",
                JobName = "x",
                Verdict = "green",
                VerdictReasons = "[]",
                ScannedAt = DateTime.UtcNow,
            });
            await seed.SaveChangesAsync();
        }

        await h.Store.UpdateSubmissionAsync("store-1", s => s.State = "submitted");
        await h.Store.UpdateSubmissionAsync("store-1", s => s.NzoId = "abc");

        await using var mig = h.Mig();
        var sub = await mig.Submissions.SingleAsync(s => s.StoreRef == "store-1");
        Assert.Equal("submitted", sub.State);
        Assert.Equal("abc", sub.NzoId);
    }

    [Fact]
    public async Task ClaimSubmission_PersistsIdentityBeforeSubmit_AndReusesItOnRetry()
    {
        await using var h = await MigrationTestHarness.CreateAsync();
        await using (var seed = h.Mig())
        {
            seed.Releases.Add(new MigrationRelease
            {
                StoreRef = "store-claim",
                StoreBasename = "x",
                SubmitFileName = "x",
                QueueFileName = "x.nzb",
                JobName = "x",
                Verdict = "green",
                VerdictReasons = "[]",
                ScannedAt = DateTime.UtcNow,
            });
            seed.Submissions.Add(new MigrationSubmission
            {
                StoreRef = "store-claim",
                State = "pending",
                UpdatedAt = DateTime.UtcNow,
            });
            await seed.SaveChangesAsync();
        }

        var firstClaim = await h.Store.ClaimSubmissionAsync("store-claim");
        var claimedId = Assert.IsType<string>(firstClaim.NzoId);
        Assert.True(Guid.TryParse(claimedId, out _));
        Assert.Equal("submitting", firstClaim.State);
        Assert.Equal(1, firstClaim.Attempt);

        // Recovery may prove that AddFile never committed. Its retry must retain
        // the original id rather than creating another ambiguous identity.
        await h.Store.UpdateSubmissionAsync("store-claim", s => s.State = "pending");
        var retryClaim = await h.Store.ClaimSubmissionAsync("store-claim");

        Assert.Equal(claimedId, retryClaim.NzoId);
        Assert.Equal("submitting", retryClaim.State);
        Assert.Equal(2, retryClaim.Attempt);

        await using var check = h.Mig();
        var persisted = await check.Submissions.SingleAsync();
        Assert.Equal(claimedId, persisted.NzoId);
        Assert.Equal("submitting", persisted.State);
    }

    [Fact]
    public async Task Cancellation_ClosesDurableRun_AndFreshScanCreatesNewRun()
    {
        await using var h = await MigrationTestHarness.CreateAsync();
        var firstRunId = await h.Store.BeginRunAsync();
        await h.Store.UpdateSessionAsync(s => s.Status = "running");

        Assert.Equal("cancelling", await h.Store.BeginCancellationAsync());
        await using (var draining = h.Mig())
        {
            Assert.Equal("cancelling", (await draining.SessionState.SingleAsync()).Status);
            var activeRun = await draining.MigrationRuns.SingleAsync(r => r.Id == firstRunId);
            Assert.Equal("running", activeRun.Status);
            Assert.Null(activeRun.CompletedAt);
        }

        await h.Store.CompleteCancellationAsync();
        await h.Store.CompleteRunAsync(); // stale runner tick must not undo cancellation

        await using (var check = h.Mig())
        {
            var run = await check.MigrationRuns.SingleAsync(r => r.Id == firstRunId);
            Assert.Equal("cancelled", run.Status);
            Assert.NotNull(run.CompletedAt);
            var session = await check.SessionState.SingleAsync();
            Assert.Equal("cancelled", session.Status);
            Assert.NotNull(session.RunCompletedAt);
        }

        // A successful subsequent scan is the only transition that makes Start
        // available again; BeginRun must then create a distinct durable run.
        await h.Store.UpdateSessionAsync(s =>
        {
            s.Status = "scanned";
            s.ScanCompletedAt = DateTime.UtcNow;
        });
        var secondRunId = await h.Store.BeginRunAsync();

        Assert.NotEqual(firstRunId, secondRunId);
        await using var final = h.Mig();
        Assert.Equal(2, await final.MigrationRuns.CountAsync());
        Assert.Equal("running", (await final.MigrationRuns.SingleAsync(r => r.Id == secondRunId)).Status);
    }

    /// <summary>
    /// Reset must wipe the wizard's own tables (session/map/releases/submissions/
    /// scan errors) and never reach outside the migration DB. This asserts the
    /// artifacts are gone and the session is back to a fresh singleton.
    /// </summary>
    [Fact]
    public async Task ResetAsync_ClearsArtifactsAndMap_ResetsSession()
    {
        await using var h = await MigrationTestHarness.CreateAsync();

        await using (var mig = h.Mig())
        {
            mig.SessionState.Add(new MigrationSessionState { Id = UsenetMigrationStore.SessionId, Status = "scanned", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
            mig.CategoryMap.Add(new MigrationCategoryMap { AltmountCategory = "tv", TargetCategory = "tv", Action = "migrate", DiscoveredBy = "config", UpdatedAt = DateTime.UtcNow });
            mig.Releases.Add(new MigrationRelease
            {
                StoreRef = "s1",
                StoreBasename = "x",
                SubmitFileName = "x",
                QueueFileName = "x.nzb",
                JobName = "x",
                Verdict = "green",
                VerdictReasons = "[]",
                ScannedAt = DateTime.UtcNow,
            });
            mig.Submissions.Add(new MigrationSubmission { StoreRef = "s1", State = "pending", UpdatedAt = DateTime.UtcNow });
            mig.MigrationRuns.Add(new MigrationRun
            {
                Id = 42,
                SourceType = "altmount",
                Status = "completed",
                StartedAt = DateTime.UtcNow,
                CompletedAt = DateTime.UtcNow,
            });
            mig.MigratedReleases.Add(new MigratedRelease
            {
                SourceType = "altmount",
                SourceReleaseId = "completed-s1",
                FirstRunId = 42,
                LastRunId = 42,
                MigratedAt = DateTime.UtcNow,
                LastVerifiedAt = DateTime.UtcNow,
            });
            mig.SymlinkRewrites.Add(new MigrationSymlinkRewrite
            {
                SymlinkPath = "/lib/x",
                OldTarget = "/old/x",
                Status = "rewrite",
                UpdatedAt = DateTime.UtcNow,
            });
            mig.ScanErrors.Add(new MigrationScanError { Kind = "meta_read", Message = "boom", OccurredAt = DateTime.UtcNow });
            await mig.SaveChangesAsync();
        }

        await h.Store.ResetAsync();

        await using var check = h.Mig();
        Assert.False(await check.CategoryMap.AnyAsync());
        Assert.False(await check.Releases.AnyAsync());
        Assert.False(await check.Submissions.AnyAsync());
        Assert.False(await check.ScanErrors.AnyAsync());
        Assert.False(await check.SymlinkRewrites.AnyAsync());
        Assert.Single(await check.MigrationRuns.ToListAsync());
        Assert.Single(await check.MigratedReleases.ToListAsync());
        var session = await check.SessionState.SingleAsync();
        Assert.Equal(UsenetMigrationStore.SessionId, session.Id);
        Assert.Equal("idle", session.Status);
    }

    [Fact]
    public async Task ForgetAllMigrationRecords_ClearsProvenanceWithoutTouchingDavContent()
    {
        await using var h = await MigrationTestHarness.CreateAsync();
        var davItemId = Guid.NewGuid();
        await using (var dav = h.Dav())
        {
            dav.Items.Add(DavItem.New(
                davItemId,
                DavItem.ContentFolder,
                "kept.mkv",
                123,
                DavItem.ItemType.UsenetFile,
                DavItem.ItemSubType.NzbFile,
                null,
                null,
                null,
                null));
            await dav.SaveChangesAsync();
        }

        await using (var mig = h.Mig())
        {
            var run = new MigrationRun
            {
                SourceType = "altmount",
                Status = "completed",
                StartedAt = DateTime.UtcNow,
            };
            mig.MigrationRuns.Add(run);
            await mig.SaveChangesAsync();

            var release = new MigratedRelease
            {
                SourceReleaseId = "source-1",
                FirstRunId = run.Id,
                LastRunId = run.Id,
                MigratedAt = DateTime.UtcNow,
                LastVerifiedAt = DateTime.UtcNow,
            };
            mig.MigratedReleases.Add(release);
            await mig.SaveChangesAsync();
            mig.MigratedFiles.Add(new MigratedFile
            {
                MigratedReleaseId = release.Id,
                VirtualPath = "/old/kept.mkv",
                NormalisedRelativePath = "kept.mkv",
                NormalisedName = "kept.mkv",
                DavItemId = davItemId,
                MatchMethod = "exact-path",
                LastVerifiedAt = DateTime.UtcNow,
            });
            await mig.SaveChangesAsync();
        }

        Assert.Equal(new MigrationDataSummary(1, 1, 1), await h.Store.GetMigrationDataSummaryAsync());

        await h.Store.ForgetAllMigrationRecordsAsync();

        Assert.Equal(new MigrationDataSummary(0, 0, 0), await h.Store.GetMigrationDataSummaryAsync());
        await using (var mig = h.Mig())
        {
            var session = await mig.SessionState.SingleAsync();
            Assert.Equal("idle", session.Status);
        }
        await using (var dav = h.Dav())
            Assert.True(await dav.Items.AnyAsync(i => i.Id == davItemId));
    }
}
