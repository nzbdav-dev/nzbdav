using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using NzbWebDAV.UsenetMigration.Model;
using NzbWebDAV.UsenetMigration.Nzb;
using NzbWebDAV.UsenetMigration.Source;
using NzbWebDAV.Api.SabControllers.AddFile;
using NzbWebDAV.Config;
using NzbWebDAV.Database;
using NzbWebDAV.Database.Models;
using NzbWebDAV.Database.Models.UsenetMigration;
using NzbWebDAV.Queue;
using NzbWebDAV.Websocket;
using Serilog;

namespace NzbWebDAV.UsenetMigration.Runner;

/// <summary>
/// Submits pending releases into NzbDAV's own SAB pipeline, in-process, up to the
/// session's queue-depth gate. Rebuilds each release's NZB from
/// its <c>.nzbz</c> store, re-injects the encryption head, and calls
/// <see cref="AddFileController.AddFileAsync"/> directly — the controller reads
/// only the <see cref="AddFileRequest"/>, not the HttpContext, so a bare
/// <see cref="DefaultHttpContext"/> suffices.
///
/// Scan-time collision validation guarantees that included releases have distinct
/// queue identities, allowing the configured workers to submit safely in parallel.
/// </summary>
public sealed class SubmissionWorkerPool(
    UsenetMigrationStore store,
    QueueManager queueManager,
    ConfigManager configManager,
    WebsocketManager websocketManager)
{
    /// <summary>Test seam for the live NzbDAV context; production leaves it null.</summary>
    internal Func<DavDatabaseContext>? DavContextFactory { get; set; }

    /// <summary>Test seams around the external submission boundary.</summary>
    internal Func<MigrationRelease, CancellationToken, Task<byte[]>>? BuildNzbOverride { get; set; }
    internal Func<MigrationRelease, Guid, byte[], CancellationToken, Task>? SubmitPreparedReleaseOverride { get; set; }

    internal Task<SubmissionRecoverySummary> RecoverClaimsAsync(CancellationToken ct = default) =>
        SubmissionClaimRecovery.RecoverAsync(store, DavContextFactory, ct);

    /// <summary>
    /// Submits as many pending releases as the queue-depth gate allows, oldest
    /// first. A pause/cancel token stops before the next external submission;
    /// the host token controls I/O and shutdown. Returns the number submitted
    /// this pass.
    /// </summary>
    public async Task<int> SubmitBatchAsync(
        CancellationToken submissionToken,
        CancellationToken ct = default)
    {
        // Resolve claims left on the external AddFile boundary before taking any
        // new work. Recovery either adopts the exact durable id, safely retries
        // that same id, or refuses an ambiguous submission.
        await RecoverClaimsAsync(ct).ConfigureAwait(false);

        var session = await store.GetSessionAsync(ct).ConfigureAwait(false);
        var maxDepth = Math.Max(1, session.MaxQueueDepth);
        var workerCount = Math.Clamp(session.SubmitWorkers, 1, maxDepth);

        var depth = await CurrentQueueDepthAsync(ct).ConfigureAwait(false);
        if (depth >= maxDepth)
            return 0;

        List<string> pending;
        await using (var ctx = store.NewContext())
        {
            pending = await ctx.Submissions.AsNoTracking()
                .Where(s => s.State == "pending")
                .OrderBy(s => s.StoreRef)
                .Take(maxDepth - depth)
                .Select(s => s.StoreRef)
                .ToListAsync(ct)
                .ConfigureAwait(false);
        }
        if (pending.Count == 0)
            return 0;

        var submitted = 0;
        var stopScheduling = 0;
        await Parallel.ForEachAsync(
            pending,
            new ParallelOptions { MaxDegreeOfParallelism = workerCount, CancellationToken = ct },
            async (storeRef, workerToken) =>
            {
                if (Volatile.Read(ref stopScheduling) != 0
                    || !await CanSubmitNextAsync(store, submissionToken, workerToken).ConfigureAwait(false))
                    return;

                await using var workerContext = store.NewContext();
                var release = await workerContext.Releases.AsNoTracking()
                    .FirstOrDefaultAsync(r => r.StoreRef == storeRef, workerToken)
                    .ConfigureAwait(false);
                if (release is null || string.IsNullOrEmpty(release.TargetCategory))
                {
                    await store.UpdateSubmissionAsync(storeRef, current =>
                    {
                        current.State = "failed";
                        current.Error = "Release missing or has no target category at submit time.";
                        current.Attempt++;
                    }, workerToken).ConfigureAwait(false);
                    return;
                }

                byte[] nzbBytes;
                try
                {
                    nzbBytes = BuildNzbOverride is null
                        ? await BuildNzbAsync(release, session, workerContext, workerToken).ConfigureAwait(false)
                        : await BuildNzbOverride(release, workerToken).ConfigureAwait(false);
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    Log.Warning(
                        "Failed to prepare migration release {StoreRef}. Reason: {Reason}",
                        release.StoreRef, e.Message);
                    Log.Debug(e, "Migration release {StoreRef} preparation failure stack", release.StoreRef);
                    await store.UpdateSubmissionAsync(storeRef, current =>
                    {
                        current.State = "failed";
                        current.Error = e.Message;
                        current.Attempt++;
                    }, workerToken).ConfigureAwait(false);
                    return;
                }

                // Preparation can be slow. Do not create a claim unless this run is
                // still active, then persist the identity before AddFile can mutate
                // the queue.
                if (Volatile.Read(ref stopScheduling) != 0
                    || !await CanSubmitNextAsync(store, submissionToken, workerToken).ConfigureAwait(false))
                    return;

                var claim = await store.ClaimSubmissionAsync(storeRef, workerToken).ConfigureAwait(false);
                var claimedId = Guid.Parse(claim.NzoId!);

                // A pause/cancel can race the durable claim. Leaving it in submitting
                // is intentional: the next active pass proves no queue item exists
                // and safely returns the same id to pending.
                if (Volatile.Read(ref stopScheduling) != 0
                    || !await CanSubmitNextAsync(store, submissionToken, workerToken).ConfigureAwait(false))
                    return;

                try
                {
                    if (SubmitPreparedReleaseOverride is null)
                        await SubmitPreparedReleaseAsync(release, claimedId, nzbBytes, workerToken).ConfigureAwait(false);
                    else
                        await SubmitPreparedReleaseOverride(release, claimedId, nzbBytes, workerToken).ConfigureAwait(false);

                    // Persist each success immediately. If the process stops between
                    // AddFile and this save, the durable claim above is recovered by id.
                    await store.UpdateSubmissionAsync(storeRef, current =>
                    {
                        current.NzoId = claimedId.ToString();
                        current.State = "submitted";
                        current.SubmittedAt = DateTime.UtcNow;
                        current.Error = null;
                    }, workerToken).ConfigureAwait(false);

                    Interlocked.Increment(ref submitted);
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    Interlocked.Exchange(ref stopScheduling, 1);
                    Log.Warning(
                        "Migration release {StoreRef} stopped at the submission boundary; " +
                        "its durable claim {NzoId} will be recovered before retry. Reason: {Reason}",
                        release.StoreRef, claimedId, e.Message);
                    Log.Debug(
                        e,
                        "Migration release {StoreRef} submission-boundary failure stack",
                        release.StoreRef);

                    // The exception may have happened before or after AddFile's DB
                    // commit. Never guess here and never mark the row pending. The
                    // next pass will inspect queue/history using the claimed id.
                    await store.UpdateSubmissionAsync(storeRef, current =>
                    {
                        current.State = "submitting";
                        current.Error = $"Submission outcome requires recovery: {e.Message}";
                    }, workerToken).ConfigureAwait(false);
                }
            }).ConfigureAwait(false);

        return submitted;
    }

    private static async Task<byte[]> BuildNzbAsync(
        MigrationRelease release,
        MigrationSessionState session,
        UsenetMigrationDbContext ctx,
        CancellationToken ct)
    {
        var storePath = StoreLocator.Resolve(release.StoreRef, session.AltmountStoreRoot)
                        ?? throw new InvalidOperationException(
                            $"Store '{release.StoreRef}' is no longer readable at submit time.");

        var nzbStore = await AltmountStoreReader.ReadStoreAsync(storePath, ct).ConfigureAwait(false);
        var nzbBytes = NzbXmlBuilder.Build(nzbStore);

        if (release.HasPassword || release.Encryption is not null)
        {
            var encryptionMeta = await LoadEncryptionMetaAsync(release.StoreRef, ctx, ct).ConfigureAwait(false);
            if (encryptionMeta is not null)
                nzbBytes = EncryptionHeadInjector.Inject(nzbBytes, encryptionMeta);
        }

        return nzbBytes;
    }

    private async Task SubmitPreparedReleaseAsync(
        MigrationRelease release,
        Guid claimedId,
        byte[] nzbBytes,
        CancellationToken ct)
    {
        await using var dbCtx = NewDavContext();
        var dbClient = new DavDatabaseClient(dbCtx);
        var controller = new AddFileController(
            new DefaultHttpContext(), dbClient, queueManager, configManager, websocketManager);

        var request = new AddFileRequest
        {
            NzoId = claimedId,
            ReplaceExistingQueueItem = false,
            // QueueFileName already carries the resolved ".nzb" filename that lands
            // in QueueItem.FileName, so do not resolve it a second time.
            FileName = release.QueueFileName,
            NzbFileStream = new MemoryStream(nzbBytes),
            Category = release.TargetCategory!,
            Priority = QueueItem.PriorityOption.Low,
            PostProcessing = QueueItem.PostProcessingOption.None,
            CancellationToken = ct,
        };

        var response = await controller.AddFileAsync(request).ConfigureAwait(false);
        if (response.NzoIds.Count != 1
            || !Guid.TryParse(response.NzoIds[0], out var returnedId)
            || returnedId != claimedId)
        {
            throw new InvalidOperationException(
                $"AddFileAsync did not return the durable claimed nzo id {claimedId}.");
        }
    }

    internal static async Task<bool> CanSubmitNextAsync(
        UsenetMigrationStore store,
        CancellationToken submissionToken,
        CancellationToken ct = default)
    {
        if (submissionToken.IsCancellationRequested)
            return false;

        var current = await store.GetSessionAsync(ct).ConfigureAwait(false);
        return !submissionToken.IsCancellationRequested && current.Status is "running";
    }

    /// <summary>
    /// Reads the first virtual file's meta that actually carries encryption or a
    /// password, for the head injection. Only reached for encrypted/passworded
    /// releases, so the extra disk reads are rare.
    /// </summary>
    private static async Task<AltmountFileMetadata?> LoadEncryptionMetaAsync(
        string storeRef, UsenetMigrationDbContext ctx, CancellationToken ct)
    {
        var metaPaths = await ctx.ReleaseFiles.AsNoTracking()
            .Where(f => f.StoreRef == storeRef)
            .OrderBy(f => f.Id)
            .Select(f => f.MetaPath)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        foreach (var metaPath in metaPaths)
        {
            AltmountFileMetadata meta;
            try
            {
                meta = await AltmountMetaReader.ReadAsync(metaPath, ct).ConfigureAwait(false);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                continue;
            }

            if (meta.Encryption != AltmountEncryption.None || !string.IsNullOrEmpty(meta.Password))
                return meta;
        }

        return null;
    }

    /// <summary>
    /// Current NzbDAV queue depth. <see cref="QueueManager"/> has no depth accessor,
    /// so this counts <c>QueueItems</c> directly.
    /// </summary>
    private async Task<int> CurrentQueueDepthAsync(CancellationToken ct)
    {
        await using var davCtx = NewDavContext();
        return await davCtx.QueueItems.AsNoTracking().CountAsync(ct).ConfigureAwait(false);
    }

    private DavDatabaseContext NewDavContext() =>
        DavContextFactory?.Invoke() ?? new DavDatabaseContext();
}
