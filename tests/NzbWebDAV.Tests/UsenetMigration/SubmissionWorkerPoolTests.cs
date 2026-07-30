using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using NzbWebDAV.Api.Controllers.UsenetMigration;
using NzbWebDAV.Clients.Usenet;
using NzbWebDAV.Config;
using NzbWebDAV.Database.Models;
using NzbWebDAV.Database.Models.UsenetMigration;
using NzbWebDAV.Queue;
using NzbWebDAV.Services;
using NzbWebDAV.Services.Metrics;
using NzbWebDAV.Services.StreamTrace;
using NzbWebDAV.UsenetMigration;
using NzbWebDAV.UsenetMigration.Runner;
using NzbWebDAV.Websocket;

namespace NzbWebDAV.Tests.UsenetMigration;

public sealed class SubmissionWorkerPoolTests
{
    [Fact]
    public async Task SubmitBatch_CancelledAfterFirstSubmission_DoesNotSubmitNextPendingRelease()
    {
        await using var h = await MigrationTestHarness.CreateAsync();
        await SeedPendingAsync(h, "store-a", "store-b");
        using var queueManager = CreateQueueManager();
        using var submission = new CancellationTokenSource();
        var submitCalls = 0;
        var pool = CreatePool(h, queueManager);
        pool.SubmitPreparedReleaseOverride = (_, _, _, _) =>
        {
            submitCalls++;
            submission.Cancel();
            return Task.CompletedTask;
        };

        var submitted = await pool.SubmitBatchAsync(submission.Token);

        Assert.Equal(1, submitted);
        Assert.Equal(1, submitCalls);
        await using var check = h.Mig();
        var states = await check.Submissions
            .OrderBy(s => s.StoreRef)
            .Select(s => s.State)
            .ToListAsync();
        Assert.Equal(["submitted", "pending"], states);
    }

    [Fact]
    public async Task SubmitBatch_UsesConfiguredWorkerConcurrency()
    {
        await using var h = await MigrationTestHarness.CreateAsync();
        await SeedPendingAsync(h, "store-a", "store-b", "store-c");
        await h.Store.UpdateSessionAsync(s => s.SubmitWorkers = 2);
        using var queueManager = CreateQueueManager();
        var pool = CreatePool(h, queueManager);
        var active = 0;
        var peak = 0;
        var entered = 0;
        var twoEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseWorkers = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        pool.SubmitPreparedReleaseOverride = async (_, _, _, _) =>
        {
            var current = Interlocked.Increment(ref active);
            UpdatePeak(ref peak, current);
            if (Interlocked.Increment(ref entered) == 2)
                twoEntered.TrySetResult(true);
            await releaseWorkers.Task;
            Interlocked.Decrement(ref active);
        };

        var batch = pool.SubmitBatchAsync(CancellationToken.None);
        await twoEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(2, Volatile.Read(ref peak));
        Assert.Equal(2, Volatile.Read(ref entered));
        Assert.False(batch.IsCompleted);

        releaseWorkers.TrySetResult(true);
        Assert.Equal(3, await batch.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(2, peak);
    }

    [Fact]
    public async Task SubmitBatch_WorkersNeverExceedAvailableQueueSlots()
    {
        await using var h = await MigrationTestHarness.CreateAsync();
        await SeedPendingAsync(h, "store-a", "store-b", "store-c", "store-d");
        await h.Store.UpdateSessionAsync(s =>
        {
            s.MaxQueueDepth = 2;
            s.SubmitWorkers = 4;
        });
        using var queueManager = CreateQueueManager();
        var pool = CreatePool(h, queueManager);
        var submitCalls = 0;
        pool.SubmitPreparedReleaseOverride = (_, _, _, _) =>
        {
            Interlocked.Increment(ref submitCalls);
            return Task.CompletedTask;
        };

        var submitted = await pool.SubmitBatchAsync(CancellationToken.None);

        Assert.Equal(2, submitted);
        Assert.Equal(2, submitCalls);
        await using var check = h.Mig();
        Assert.Equal(2, await check.Submissions.CountAsync(s => s.State == "submitted"));
        Assert.Equal(2, await check.Submissions.CountAsync(s => s.State == "pending"));
    }

    [Fact]
    public async Task CancelDuringBlockedSubmission_BlocksResetUntilClaimIsReconciled()
    {
        await using var h = await MigrationTestHarness.CreateAsync();
        await SeedPendingAsync(h, "store-a");
        var runId = await h.Store.BeginRunAsync();
        using var queueManager = CreateQueueManager();
        var configManager = new ConfigManager();
        var websocketManager = new WebsocketManager();
        var runner = new UsenetMigrationRunner(
            h.Store, queueManager, configManager, websocketManager);
        runner.WorkerPoolForTests.DavContextFactory = h.DavFactory;
        runner.WorkerPoolForTests.BuildNzbOverride = (_, _) => Task.FromResult<byte[]>([1]);
        runner.ReconcilerForTests.DavContextFactory = h.DavFactory;

        var submissionEntered = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSubmission = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        runner.WorkerPoolForTests.SubmitPreparedReleaseOverride = async (
            release, claimedId, _, _) =>
        {
            submissionEntered.TrySetResult(true);
            await releaseSubmission.Task;

            await using var dav = h.Dav();
            dav.QueueItems.Add(new QueueItem
            {
                Id = claimedId,
                CreatedAt = DateTime.UtcNow,
                FileName = release.QueueFileName,
                JobName = release.JobName,
                NzbFileSize = 100,
                TotalSegmentBytes = 100,
                Category = release.TargetCategory!,
                Priority = QueueItem.PriorityOption.Low,
                PostProcessing = QueueItem.PostProcessingOption.None,
            });
            await dav.SaveChangesAsync();
        };

        var activeTick = runner.TickOnceForTestsAsync();
        await submissionEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal("cancelling", await h.Store.BeginCancellationAsync());
        runner.InterruptSubmissionBatch();

        await Assert.ThrowsAsync<BadHttpRequestException>(
            () => UsenetMigrationController.ResetWizardAsync(h.Store));
        await runner.TickOnceForTestsAsync();
        await using (var blocked = h.Mig())
        {
            Assert.Equal("cancelling", (await blocked.SessionState.SingleAsync()).Status);
            Assert.Equal("submitting", (await blocked.Submissions.SingleAsync()).State);
            Assert.Single(await blocked.Releases.ToListAsync());
        }

        releaseSubmission.TrySetResult(true);
        await activeTick.WaitAsync(TimeSpan.FromSeconds(5));

        await using (var drained = h.Mig())
        {
            Assert.Equal("cancelling", (await drained.SessionState.SingleAsync()).Status);
            Assert.Equal("processing", (await drained.Submissions.SingleAsync()).State);
        }

        await runner.TickOnceForTestsAsync();

        await using (var cancelled = h.Mig())
        {
            Assert.Equal("cancelled", (await cancelled.SessionState.SingleAsync()).Status);
            var run = await cancelled.MigrationRuns.SingleAsync(r => r.Id == runId);
            Assert.Equal("cancelled", run.Status);
            Assert.NotNull(run.CompletedAt);
        }

        await UsenetMigrationController.ResetWizardAsync(h.Store);
        await using var reset = h.Mig();
        Assert.Equal("idle", (await reset.SessionState.SingleAsync()).Status);
        Assert.Empty(await reset.Submissions.ToListAsync());
    }

    [Fact]
    public async Task CancelDuringBlockedScan_BlocksResetAndDiscardsUncommittedResults()
    {
        await using var h = await MigrationTestHarness.CreateAsync();
        var metadataRoot = Directory.CreateTempSubdirectory("altmig-scan-");
        try
        {
            await h.Store.UpdateSessionAsync(s =>
            {
                s.Status = "scanning";
                s.AltmountMetadataRoot = metadataRoot.FullName;
            });
            await using (var seed = h.Mig())
            {
                seed.Releases.Add(new MigrationRelease
                {
                    StoreRef = "previous-scan",
                    StoreBasename = "previous-scan",
                    SubmitFileName = "previous-scan.nzb",
                    QueueFileName = "previous-scan.nzb",
                    JobName = "previous-scan",
                    Verdict = "green",
                    VerdictReasons = "[]",
                    ScannedAt = DateTime.UtcNow,
                });
                await seed.SaveChangesAsync();
            }

            using var queueManager = CreateQueueManager();
            var runner = new UsenetMigrationRunner(
                h.Store, queueManager, new ConfigManager(), new WebsocketManager());
            runner.ScanRunnerForTests.DavContextFactory = h.DavFactory;
            var scanReachedCommitBoundary = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseScan = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            runner.ScanRunnerForTests.BeforePersistOverride = async _ =>
            {
                scanReachedCommitBoundary.TrySetResult(true);
                await releaseScan.Task;
            };

            var activeTick = runner.TickOnceForTestsAsync();
            await scanReachedCommitBoundary.Task.WaitAsync(TimeSpan.FromSeconds(5));

            var cancellation = await h.Store.TryTransitionSessionAsync(
                MigrationSessionTransition.CancelScan);
            Assert.Equal(MigrationSessionTransitionOutcome.Applied, cancellation.Outcome);
            runner.InterruptScan();

            await Assert.ThrowsAsync<BadHttpRequestException>(
                () => UsenetMigrationController.ResetWizardAsync(h.Store));
            await using (var blocked = h.Mig())
            {
                Assert.Equal("scan_cancelling", (await blocked.SessionState.SingleAsync()).Status);
                Assert.Single(await blocked.Releases.ToListAsync());
            }

            releaseScan.TrySetResult(true);
            await activeTick.WaitAsync(TimeSpan.FromSeconds(5));

            await using (var cancelled = h.Mig())
            {
                var session = await cancelled.SessionState.SingleAsync();
                Assert.Equal("mapped", session.Status);
                Assert.Null(session.ScanCompletedAt);
                Assert.Equal("previous-scan", (await cancelled.Releases.SingleAsync()).StoreRef);
            }

            await UsenetMigrationController.ResetWizardAsync(h.Store);
        }
        finally
        {
            metadataRoot.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task SubmitBatch_CrashAfterQueueCommit_RecoversClaimWithoutResubmitting()
    {
        await using var h = await MigrationTestHarness.CreateAsync();
        await SeedPendingAsync(h, "store-a");
        using var queueManager = CreateQueueManager();
        var submitCalls = 0;
        Guid committedId = default;
        var pool = CreatePool(h, queueManager);
        pool.SubmitPreparedReleaseOverride = async (release, claimedId, _, _) =>
        {
            submitCalls++;
            committedId = claimedId;
            await using var dav = h.Dav();
            dav.QueueItems.Add(new QueueItem
            {
                Id = claimedId,
                CreatedAt = DateTime.UtcNow,
                FileName = release.QueueFileName,
                JobName = release.JobName,
                NzbFileSize = 100,
                TotalSegmentBytes = 100,
                Category = release.TargetCategory!,
                Priority = QueueItem.PriorityOption.Low,
                PostProcessing = QueueItem.PostProcessingOption.None,
            });
            await dav.SaveChangesAsync();
            throw new IOException("simulated process loss after AddFile committed");
        };

        Assert.Equal(0, await pool.SubmitBatchAsync(CancellationToken.None));
        await using (var afterCrash = h.Mig())
        {
            var claimed = await afterCrash.Submissions.SingleAsync();
            Assert.Equal("submitting", claimed.State);
            Assert.Equal(committedId.ToString(), claimed.NzoId);
        }

        Assert.Equal(0, await pool.SubmitBatchAsync(CancellationToken.None));

        Assert.Equal(1, submitCalls);
        await using (var recovered = h.Mig())
        {
            var adopted = await recovered.Submissions.SingleAsync();
            Assert.Equal("submitted", adopted.State);
            Assert.Equal(committedId.ToString(), adopted.NzoId);
        }
        await using (var dav = h.Dav())
            Assert.Equal(committedId, (await dav.QueueItems.SingleAsync()).Id);
    }

    private static SubmissionWorkerPool CreatePool(MigrationTestHarness h, QueueManager queueManager)
    {
        var pool = new SubmissionWorkerPool(
            h.Store,
            queueManager,
            new ConfigManager(),
            new WebsocketManager())
        {
            DavContextFactory = h.DavFactory,
            BuildNzbOverride = (_, _) => Task.FromResult<byte[]>([1]),
        };
        return pool;
    }

    private static void UpdatePeak(ref int peak, int current)
    {
        var observed = Volatile.Read(ref peak);
        while (current > observed)
        {
            var previous = Interlocked.CompareExchange(ref peak, current, observed);
            if (previous == observed)
                return;
            observed = previous;
        }
    }

    private static async Task SeedPendingAsync(MigrationTestHarness h, params string[] storeRefs)
    {
        await h.Store.UpdateSessionAsync(s =>
        {
            s.Status = "running";
            s.MaxQueueDepth = 10;
        });
        await using var migration = h.Mig();
        foreach (var storeRef in storeRefs)
        {
            migration.Releases.Add(new MigrationRelease
            {
                StoreRef = storeRef,
                StoreBasename = storeRef,
                SubmitFileName = $"{storeRef}.nzb",
                QueueFileName = $"{storeRef}.nzb",
                JobName = storeRef,
                TargetCategory = "tv",
                Verdict = "green",
                VerdictReasons = "[]",
                ScannedAt = DateTime.UtcNow,
            });
            migration.Submissions.Add(new MigrationSubmission
            {
                StoreRef = storeRef,
                State = "pending",
                UpdatedAt = DateTime.UtcNow,
            });
        }
        await migration.SaveChangesAsync();
    }

    private static QueueManager CreateQueueManager()
    {
        var config = new ConfigManager();
        config.UpdateValues(
        [
            new ConfigItem
            {
                ConfigName = ConfigKeys.UsenetProviders,
                ConfigValue = JsonSerializer.Serialize(new UsenetProviderConfig()),
            },
        ]);
        var websocket = new WebsocketManager();
        var usenet = new UsenetStreamingClient(
            config,
            websocket,
            new ProviderUsageTracker(),
            new MetricsWriter(),
            new ProviderBytesTracker(),
            new StreamTraceBuffer(100),
            new ActiveReadRegistry());
        return new QueueManager(
            usenet,
            config,
            websocket,
            new ProviderUsageTracker(),
            new WatchdogLog(),
            new QueueItemSourceTracker(),
            new BenchmarkGate(),
            startLoop: false);
    }
}
