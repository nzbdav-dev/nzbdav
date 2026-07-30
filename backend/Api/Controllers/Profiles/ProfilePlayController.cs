using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NzbWebDAV.Api.Controllers.GetWebdavItem;
using NzbWebDAV.Api.SabControllers.AddFile;
using NzbWebDAV.Clients.Usenet.Concurrency;
using NzbWebDAV.Config;
using NzbWebDAV.Database;
using NzbWebDAV.Database.Models;
using NzbWebDAV.Exceptions;
using NzbWebDAV.Extensions;
using NzbWebDAV.Models.Nzb;
using NzbWebDAV.Queue;
using NzbWebDAV.Services;
using NzbWebDAV.Utils;
using NzbWebDAV.WebDav;
using NzbWebDAV.Websocket;
using Serilog;

namespace NzbWebDAV.Api.Controllers.Profiles;

[ApiController]
[Route("adapters/addon/{token}/play/{nzbToken}.mkv")]
[Route("api/search/{token}/play/{nzbToken}.mkv")]
public class ProfilePlayController(
    ConfigManager configManager,
    NzbResolutionCache cache,
    CandidateNegativeCache negativeCache,
    PlaybackFastVerifier fastVerifier,
    WatchdogLog watchdogLog,
    NewznabRateLimiter rateLimiter,
    IndexerHitTracker hitTracker,
    DavDatabaseClient dbClient,
    QueueManager queueManager,
    WebsocketManager websocketManager,
    QueueItemSourceTracker sourceTracker,
    NzbFetchCoalescer nzbFetchCoalescer,
    PreflightCache preflightCache,
    PreflightSessionRegistry preflightSessions,
    VariantResolver variantResolver,
    WatchtowerStore watchtowerStore,
    WardenStore wardenStore,
    PlayResolutionCoalescer playCoalescer,
    PreferredOrderStore preferredOrderStore
) : ControllerBase
{
    private static readonly TimeSpan NzbFetchTimeout = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

    // Tracks the last time a Play click touched a watchdog-created queue item.
    // Used by ScheduleOrphanCleanup to remove items the player has stopped polling.
    private static readonly ConcurrentDictionary<Guid, DateTimeOffset> _playLastSeen = new();
    private static readonly TimeSpan OrphanIdleThreshold = TimeSpan.FromMinutes(5);

    [HttpGet]
    public async Task<IActionResult> Get(string token, string nzbToken)
    {
        Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate";
        try
        {
            return await HandleAsync(token, nzbToken).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Log.Error(e, "Play handler crashed for token {Token} / nzbToken {NzbToken}", token, nzbToken);
            if (HttpContext.Response.HasStarted) return new EmptyResult();
            return StatusCode(500, $"Internal error: {e.GetType().Name}: {e.Message}");
        }
    }

    private async Task<IActionResult> HandleAsync(string token, string nzbToken)
    {
        var profile = configManager.GetProfileConfig().FindByToken(token);
        if (profile is null) return NotFound();

        var entry = cache.Get(nzbToken);
        if (entry is null) return NotFound("Link expired. Re-search in your client.");

        if (configManager.IsWatchtowerEnabled())
            await watchtowerStore.TryWarmCacheAsync(entry.Type, entry.Id, HttpContext.RequestAborted).ConfigureAwait(false);

        preflightSessions.Cancel(entry.ProfileToken, entry.Type, entry.Id);

        var skipLegacyMatch = false;
        if (variantResolver.IsEnabled)
        {
            var decision = await variantResolver.ResolveAsync(dbClient.Ctx, entry, HttpContext.RequestAborted)
                .ConfigureAwait(false);
            if (decision.ReuseMatch is not null)
            {
                var variantRedirect = await BuildRedirectForHistoryItemAsync(
                    decision.ReuseMatch.Row.HistoryItemId, entry, HttpContext.RequestAborted).ConfigureAwait(false);
                if (variantRedirect is not null) return variantRedirect;
            }
            skipLegacyMatch = decision.GroupHasMembers;
        }

        if (!skipLegacyMatch)
        {
            var existingResolved = await TryResolveExistingAsync(entry, HttpContext.RequestAborted).ConfigureAwait(false);
            if (existingResolved is not null) return existingResolved;
        }

        var inFlight = variantResolver.IsEnabled
            ? await variantResolver.FindInFlightAsync(dbClient.Ctx, entry, HttpContext.RequestAborted)
                .ConfigureAwait(false)
            : await FindInFlightQueueItemAsync(entry, HttpContext.RequestAborted).ConfigureAwait(false);
        if (inFlight.HasValue)
        {
            var waitRedirect = await WaitForInFlightAsync(inFlight.Value, entry, HttpContext.RequestAborted)
                .ConfigureAwait(false);
            if (waitRedirect is not null) return waitRedirect;
        }

        return await ResolveAndCommitAsync(nzbToken, entry).ConfigureAwait(false);
    }

    private async Task<IActionResult> ResolveAndCommitAsync(string nzbToken, NzbResolutionCache.Entry entry)
    {
        var coalesceKey = VariantResolver.BuildContentGroupKey(entry);
        PlayResolutionCoalescer.Lease? coalesceLease = null;
        if (coalesceKey is not null)
        {
            coalesceLease = playCoalescer.Acquire(coalesceKey);
            if (!coalesceLease.IsLeader)
            {
                var followed = await FollowResolvedLeaderAsync(coalesceLease, entry, HttpContext.RequestAborted)
                    .ConfigureAwait(false);
                if (followed is not null) return followed;
                coalesceLease = null;
            }
        }

        var resolvedNzoId = new StrongBox<Guid?>(null);
        try
        {
            return await ResolveLeaderBodyAsync(nzbToken, entry, resolvedNzoId).ConfigureAwait(false);
        }
        finally
        {
            coalesceLease?.Publish(resolvedNzoId.Value);
        }
    }

    private async Task<IActionResult?> FollowResolvedLeaderAsync(
        PlayResolutionCoalescer.Lease lease, NzbResolutionCache.Entry entry, CancellationToken ct)
    {
        Guid? nzoId;
        try
        {
            using var waitCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            waitCts.CancelAfter(TimeSpan.FromSeconds(configManager.GetPlayTotalBudgetSeconds()));
            nzoId = await lease.WaitAsync(waitCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return null;
        }

        if (nzoId is { } id)
        {
            var redirect = await WaitForInFlightAsync(id, entry, ct).ConfigureAwait(false);
            if (redirect is not null) return redirect;
        }

        var existing = await TryResolveExistingAsync(entry, ct).ConfigureAwait(false);
        if (existing is not null) return existing;

        var inflight = variantResolver.IsEnabled
            ? await variantResolver.FindInFlightAsync(dbClient.Ctx, entry, ct).ConfigureAwait(false)
            : await FindInFlightQueueItemAsync(entry, ct).ConfigureAwait(false);
        if (inflight is { } q)
            return await WaitForInFlightAsync(q, entry, ct).ConfigureAwait(false);

        return null;
    }

    private async Task<IActionResult> ResolveLeaderBodyAsync(
        string nzbToken, NzbResolutionCache.Entry entry, StrongBox<Guid?> resolvedNzoId)
    {
        var totalBudget = TimeSpan.FromSeconds(configManager.GetPlayTotalBudgetSeconds());
        var hedgeDelay = TimeSpan.FromSeconds(configManager.GetPlayHedgeDelaySeconds());
        var maxCandidates = configManager.GetPlayMaxCandidates();
        var maxAttempts = configManager.GetPlayMaxAttempts();
        var verifyMode = configManager.GetPlayVerifyMode();
        var verifySampleCount = configManager.GetPlayVerifySampleCount();
        var watchdogEnabled = configManager.IsPlaybackWatchdogEnabled();

        using var totalCts = CancellationTokenSource.CreateLinkedTokenSource(HttpContext.RequestAborted);
        totalCts.CancelAfter(totalBudget);
        var deadline = DateTimeOffset.UtcNow + totalBudget;

        var clickId = Guid.NewGuid();
        var requestedTitle = entry.Primary.Title;
        var contentType = entry.Type;
        var startsAt = new Dictionary<string, DateTimeOffset>();
        // Compute once so every attempt logged below carries the cascade-cleanup link.
        var contentGroupKey = VariantResolver.BuildContentGroupKey(entry);

        // Watchdog OFF → simple single-candidate flow (no auto-fallback, no pre-verify, no negative cache).
        if (!watchdogEnabled)
        {
            startsAt[entry.Primary.NzbUrl] = DateTimeOffset.UtcNow;
            var nzbBytes = await FetchNzbBytesAsync(entry.Primary, totalCts.Token).ConfigureAwait(false);
            if (nzbBytes is null)
            {
                RecordAttempt(clickId, entry.Primary, contentType, requestedTitle, 0,
                    WatchdogEntry.Outcome.EnqueueFailed, "Indexer NZB fetch failed", startsAt, isWinner: false,
                    contentGroupKey: contentGroupKey);
                return await ResolveExistingOrErrorAsync(entry, 502,
                    "Failed to fetch NZB from indexer.", 10, HttpContext.RequestAborted).ConfigureAwait(false);
            }
            var single = new PreVerifyResult(entry.Primary, nzbBytes, PlaybackFastVerifier.Verdict.Available, null, false);
            var (result, reason, newNzoId) = await CommitAsync(nzbToken, single, deadline, totalCts.Token).ConfigureAwait(false);
            RecordAttempt(clickId, entry.Primary, contentType, requestedTitle, 0,
                MapCommitReason(reason), CommitReasonToMessage(reason), startsAt, isWinner: reason == CommitReason.Completed,
                contentGroupKey: contentGroupKey);
            if (reason == CommitReason.Completed) resolvedNzoId.Value = newNzoId;
            if (reason == CommitReason.BudgetTimeout && newNzoId.HasValue)
                ScheduleOrphanCleanup(newNzoId.Value);
            if (result is not null) return result;
            return await ResolveExistingOrErrorAsync(entry, 503,
                "Still processing. Retry the link in a few seconds.", 5, HttpContext.RequestAborted).ConfigureAwait(false);
        }

        // Batch retry loop: try up to maxCandidates candidates in parallel per batch;
        // if all in a batch fail, advance to the next batch — until a winner, budget elapses,
        // total attempts (maxAttempts) are exhausted, or we run out of cached candidates.
        var preferredOrder = preferredOrderStore.GetOrder(entry.ProfileToken, entry.Type, entry.Id);
        var preferSubtitles = configManager.IsPlaySubtitlePreferenceEnabled();
        var fallbackQueue = BuildFallbackQueue(entry, preferredOrder, preferSubtitles);
        var rankIndex = new Dictionary<string, int>();
        var displayRank = 0;
        var queueIndex = 0;
        var attemptsUsed = 0;
        var sawAnyBatch = false;

        while (attemptsUsed < maxAttempts && queueIndex < fallbackQueue.Count)
        {
            if (totalCts.IsCancellationRequested) break;
            if (DateTimeOffset.UtcNow >= deadline) break;

            var batchBudget = Math.Min(maxCandidates, maxAttempts - attemptsUsed);
            var pool = new List<NzbResolutionCache.Candidate>();
            while (queueIndex < fallbackQueue.Count && pool.Count < batchBudget)
            {
                var c = fallbackQueue[queueIndex];
                queueIndex++;
                if (negativeCache.IsFailed(c.NzbUrl)
                    || wardenStore.IsDeadAnywhere(WardenFingerprint.Compute(c.Size, c.Poster, c.UsenetDate)))
                {
                    // Surface this in the watchdog so users see every tried candidate,
                    // including those preflight (or a prior click) already poisoned —
                    // otherwise the skip is invisible.
                    var skippedRank = displayRank++;
                    rankIndex[c.NzbUrl] = skippedRank;
                    startsAt[c.NzbUrl] = DateTimeOffset.UtcNow;
                    RecordAttempt(clickId, c, contentType, requestedTitle, skippedRank,
                        WatchdogEntry.Outcome.PreVerifyDead,
                        "Previously marked dead by preflight or earlier verify — skipped",
                        startsAt, isWinner: false,
                        contentGroupKey: contentGroupKey,
                        providerHost: AllConfiguredProvidersDisplay());
                    continue;
                }
                var candidateFileName = $"{SanitizeFileName(c.Title)}.nzb";
                if (negativeCache.IsFileNameBroken(candidateFileName))
                {
                    var skippedRank = displayRank++;
                    rankIndex[c.NzbUrl] = skippedRank;
                    startsAt[c.NzbUrl] = DateTimeOffset.UtcNow;
                    RecordAttempt(clickId, c, contentType, requestedTitle, skippedRank,
                        WatchdogEntry.Outcome.PreVerifyDead,
                        "Candidate proven broken by a prior mid-read failure — skipped",
                        startsAt, isWinner: false,
                        contentGroupKey: contentGroupKey,
                        providerHost: AllConfiguredProvidersDisplay());
                    continue;
                }
                rankIndex[c.NzbUrl] = displayRank++;
                pool.Add(c);
            }
            if (pool.Count == 0) break;

            sawAnyBatch = true;
            attemptsUsed += pool.Count;

            var batch = await RunBatchAsync(pool, rankIndex, nzbToken, contentType, requestedTitle,
                clickId, startsAt, verifyMode, verifySampleCount, hedgeDelay, deadline, totalCts, contentGroupKey,
                preferSubtitles).ConfigureAwait(false);

            switch (batch.Outcome)
            {
                case BatchOutcome.Winner:
                case BatchOutcome.Cancelled:
                    resolvedNzoId.Value = batch.WinnerNzoId;
                    return batch.Action!;
                case BatchOutcome.BudgetTimeout:
                    return await ResolveExistingOrErrorAsync(entry, 503,
                        "Still processing. Retry the link in a few seconds.", 5,
                        HttpContext.RequestAborted).ConfigureAwait(false);
                case BatchOutcome.AllFailed:
                    break; // try next batch
            }
        }

        if (!sawAnyBatch)
        {
            return await ResolveExistingOrErrorAsync(entry, 503,
                "All ranked candidates recently failed; try again shortly.", 5,
                HttpContext.RequestAborted).ConfigureAwait(false);
        }

        return await ResolveExistingOrErrorAsync(entry, 503,
            "All tried candidates failed. Retry in a few seconds.", 5,
            HttpContext.RequestAborted).ConfigureAwait(false);
    }

    private static List<NzbResolutionCache.Candidate> BuildFallbackQueue(
        NzbResolutionCache.Entry entry, IReadOnlyList<string>? preferredOrder, bool preferSubtitles)
    {
        var primary = entry.Primary;
        var queue = new List<NzbResolutionCache.Candidate>(entry.Candidates.Count) { primary };

        var others = entry.Candidates
            .Where((_, i) => i != entry.StartIndex)
            .ToList();

        // Establish the base fallback order: the client-reported order when present,
        // otherwise size proximity to the clicked release.
        List<NzbResolutionCache.Candidate> orderedOthers;
        if (preferredOrder is { Count: > 0 })
        {
            orderedOthers = PreferredOrderStore.ApplyOrder(others, KeyOf, preferredOrder);
        }
        else if (primary.Size <= 0)
        {
            orderedOthers = others.OrderByDescending(c => c.Size).ToList();
        }
        else
        {
            orderedOthers = others.Where(c => c.Size <= primary.Size).OrderByDescending(c => c.Size)
                .Concat(others.Where(c => c.Size > primary.Size).OrderBy(c => c.Size))
                .ToList();
        }

        // When enabled, bias fallbacks toward releases that also carry subtitles — and,
        // where the indexer reports it, the same subtitle language as the clicked release.
        // OrderByDescending is stable, so the base order above is preserved within each
        // subtitle tier and no candidate is ever discarded.
        if (preferSubtitles)
        {
            var primaryLanguages = SubtitlePreference.ParseLanguages(primary.Subs);
            var primaryHasSubs = primaryLanguages.Count > 0;
            orderedOthers = orderedOthers
                .OrderByDescending(c => SubtitlePreference.Rank(c.Subs, primaryLanguages, primaryHasSubs))
                .ToList();
        }

        queue.AddRange(orderedOthers);
        return queue;
    }

    private static string KeyOf(NzbResolutionCache.Candidate c) =>
        ReleaseIdentity.Key(c.Size, c.Poster, c.UsenetDate, c.NzbUrl);

    private enum BatchOutcome { Winner, AllFailed, Cancelled, BudgetTimeout }

    private async Task<(BatchOutcome Outcome, IActionResult? Action, Guid? WinnerNzoId)> RunBatchAsync(
        List<NzbResolutionCache.Candidate> pool,
        Dictionary<string, int> rankIndex,
        string nzbToken,
        string contentType,
        string requestedTitle,
        Guid clickId,
        Dictionary<string, DateTimeOffset> startsAt,
        string verifyMode,
        int verifySampleCount,
        TimeSpan hedgeDelay,
        DateTimeOffset deadline,
        CancellationTokenSource totalCts,
        string? contentGroupKey,
        bool preferSubtitles)
    {
        foreach (var c in pool) startsAt[c.NzbUrl] = DateTimeOffset.UtcNow;

        // Phase 1 — pre-verify (parallel, hedged): fetch NZB + verify segment availability.
        // Track each task's candidate so that if a task throws before producing a result
        // (rare, but possible — see CancelRemainingAndRecord), we can still record a
        // watchdog entry for that candidate instead of dropping it silently.
        var preVerifies = new List<Task<PreVerifyResult>>();
        var taskCandidates = new Dictionary<Task<PreVerifyResult>, NzbResolutionCache.Candidate>();
        var primaryTask = PreVerifyAsync(pool[0], verifyMode, verifySampleCount, preferSubtitles, totalCts.Token);
        preVerifies.Add(primaryTask);
        taskCandidates[primaryTask] = pool[0];

        if (pool.Count > 1)
        {
            // Give the primary a brief head start; if it hasn't passed by then, fire backups.
            var hedgeTask = Task.Delay(hedgeDelay, totalCts.Token);
            var settled = await Task.WhenAny(preVerifies[0], hedgeTask).ConfigureAwait(false);
            var primaryReady = settled == preVerifies[0]
                               && preVerifies[0].IsCompletedSuccessfully
                               && preVerifies[0].Result.Verdict == PlaybackFastVerifier.Verdict.Available;

            if (!primaryReady)
            {
                for (var i = 1; i < pool.Count; i++)
                {
                    startsAt[pool[i].NzbUrl] = DateTimeOffset.UtcNow;
                    var t = PreVerifyAsync(pool[i], verifyMode, verifySampleCount, preferSubtitles, totalCts.Token);
                    preVerifies.Add(t);
                    taskCandidates[t] = pool[i];
                }
            }
        }

        // Phase 2 — commit. Take pre-verified candidates in their original ranking order,
        // try to commit each (enqueue + poll), first one to complete wins.
        var remaining = new List<Task<PreVerifyResult>>(preVerifies);
        var ready = new SortedList<int, PreVerifyResult>();

        while (remaining.Count > 0 || ready.Count > 0)
        {
            // Pull off any newly-settled pre-verifications.
            while (remaining.Count > 0)
            {
                var anyDone = remaining.FirstOrDefault(t => t.IsCompleted);
                if (anyDone == null) break;
                remaining.Remove(anyDone);
                var r = await anyDone.ConfigureAwait(false);
                switch (r.Verdict)
                {
                    case PlaybackFastVerifier.Verdict.Available:
                        ready[rankIndex[r.Candidate.NzbUrl]] = r;
                        break;
                    case PlaybackFastVerifier.Verdict.Dead:
                        negativeCache.MarkFailed(r.Candidate.NzbUrl);
                        wardenStore.MarkDead(WardenFingerprint.Compute(r.Candidate.Size, r.Candidate.Poster, r.Candidate.UsenetDate));
                        RecordAttempt(clickId, r.Candidate, contentType, requestedTitle,
                            rankIndex[r.Candidate.NzbUrl],
                            WatchdogEntry.Outcome.PreVerifyDead,
                            "STAT/HEAD reported article missing on every provider", startsAt, isWinner: false,
                            contentGroupKey: contentGroupKey,
                            providerHost: AllConfiguredProvidersDisplay());
                        break;
                    case PlaybackFastVerifier.Verdict.Timeout:
                        // Don't poison on timeout — provider was just slow.
                        // Try it anyway as a last resort if we run out of candidates, but only
                        // when we actually fetched the NZB; a timeout from cancellation has no
                        // bytes to commit and must not be enqueued (CommitAsync dereferences
                        // NzbBytes! and the candidate would be wrongly poisoned).
                        if (r.NzbBytes is not null)
                            ready[rankIndex[r.Candidate.NzbUrl] + 10000] = r;
                        break;
                }
            }

            if (ready.Count > 0)
            {
                // Among the verified candidates ready to commit, prefer the best-ranked one
                // that carries subtitles (indexer-declared or a sidecar file in the NZB) over
                // a subless one — but never trade a stronger verdict (Available) for a weaker
                // one (Timeout) just to gain subs. ready is key-sorted, so each verdict tier is
                // a contiguous block and we only reorder within the head's tier.
                var pickIdx = 0;
                if (preferSubtitles)
                {
                    var headVerdict = ready.Values[0].Verdict;
                    for (var i = 0; i < ready.Count && ready.Values[i].Verdict == headVerdict; i++)
                    {
                        if (HasSubtitles(ready.Values[i])) { pickIdx = i; break; }
                    }
                }
                var best = ready.Values[pickIdx];
                ready.RemoveAt(pickIdx);
                var (action, reason, newNzoId) = await CommitAsync(nzbToken, best, deadline, totalCts.Token).ConfigureAwait(false);
                RecordAttempt(clickId, best.Candidate, contentType, requestedTitle,
                    rankIndex[best.Candidate.NzbUrl],
                    MapCommitReason(reason), CommitReasonToMessage(reason), startsAt,
                    isWinner: reason == CommitReason.Completed,
                    contentGroupKey: contentGroupKey,
                    providerHost: best.ResponderHost);
                if (action is not null)
                {
                    // Winner found. Cancel in-flight losers so they stop holding
                    // indexer/NNTP connections, and log them in /watchdog as Cancelled.
                    CancelRemainingAndRecord(clickId, contentType, requestedTitle,
                        rankIndex, startsAt, remaining, taskCandidates, ready, totalCts,
                        "Winner found; loser cancelled to free provider connections",
                        contentGroupKey);
                    return (BatchOutcome.Winner, action, newNzoId);
                }
                if (reason == CommitReason.Aborted)
                {
                    if (newNzoId.HasValue)
                        await RemoveAbortedQueueItemAsync(newNzoId.Value).ConfigureAwait(false);
                    continue;
                }
                if (reason == CommitReason.QueueFailed || reason == CommitReason.EnqueueFailed)
                    negativeCache.MarkFailed(best.Candidate.NzbUrl);
                if (reason == CommitReason.Cancelled)
                {
                    CancelRemainingAndRecord(clickId, contentType, requestedTitle,
                        rankIndex, startsAt, remaining, taskCandidates, ready, totalCts,
                        "Client disconnected; loser cancelled",
                        contentGroupKey);
                    return (BatchOutcome.Cancelled, new EmptyResult(), null);
                }
                if (reason == CommitReason.BudgetTimeout)
                {
                    // The queue item is still processing. Schedule cleanup so we
                    // don't keep downloading a UHD release that the player gave up on.
                    if (newNzoId.HasValue) ScheduleOrphanCleanup(newNzoId.Value);
                    CancelRemainingAndRecord(clickId, contentType, requestedTitle,
                        rankIndex, startsAt, remaining, taskCandidates, ready, totalCts,
                        "Budget exhausted; loser cancelled",
                        contentGroupKey);
                    // HandleAsync converts this to a 503 + Retry-After (or a 302 if
                    // another click finished the same group in the meantime).
                    return (BatchOutcome.BudgetTimeout, null, null);
                }
                continue;
            }

            if (remaining.Count == 0) break;
            if (DateTimeOffset.UtcNow >= deadline) break;
            await Task.WhenAny(remaining).ConfigureAwait(false);
        }

        // All candidates in this batch failed (dead or commit-failed). Record any
        // pre-verified-but-unused candidates (ready) and any in-flight tasks
        // (remaining) so the watchdog reflects every attempt that was tried —
        // including the ones the deadline cut short. Caller may try another batch.
        CancelRemainingAndRecord(clickId, contentType, requestedTitle,
            rankIndex, startsAt, remaining, taskCandidates, ready, totalCts,
            "Batch exited without a winner; remaining verifies cancelled",
            contentGroupKey);
        return (BatchOutcome.AllFailed, null, null);
    }

    // Signal in-flight pre-verify tasks to abort (freeing indexer/NNTP connections),
    // and record both pre-verified-but-unused candidates and in-flight ones as Cancelled
    // so they show up in /watchdog instead of vanishing silently.
    private void CancelRemainingAndRecord(
        Guid clickId,
        string contentType,
        string requestedTitle,
        Dictionary<string, int> rankIndex,
        Dictionary<string, DateTimeOffset> startsAt,
        List<Task<PreVerifyResult>> remaining,
        Dictionary<Task<PreVerifyResult>, NzbResolutionCache.Candidate> taskCandidates,
        SortedList<int, PreVerifyResult> ready,
        CancellationTokenSource totalCts,
        string reason,
        string? contentGroupKey)
    {
        // Only cancel totalCts when there are in-flight verify tasks to abort.
        // totalCts is shared with the outer batch-retry loop in HandleAsync; cancelling
        // it unconditionally here turned the multi-batch fallback into a single-batch
        // run, because the next outer iteration's IsCancellationRequested check would
        // exit immediately. When remaining is empty (typical AllFailed path) there is
        // nothing to cancel — leave totalCts alive so the next batch can run.
        if (remaining.Count > 0 && !totalCts.IsCancellationRequested)
        {
            try { totalCts.Cancel(); }
            catch (ObjectDisposedException) { /* race with using disposal — already done */ }
        }

        // Already-verified but unused — no connections to release, just visibility.
        foreach (var r in ready.Values)
        {
            RecordAttempt(clickId, r.Candidate, contentType, requestedTitle,
                rankIndex[r.Candidate.NzbUrl],
                WatchdogEntry.Outcome.Cancelled, reason, startsAt, isWinner: false,
                contentGroupKey: contentGroupKey);
        }

        // Still in flight — record their cancellation in the background once they observe it.
        // Don't await synchronously; the response should return immediately.
        if (remaining.Count == 0) return;
        var pending = remaining.ToList();
        var localTaskCandidates = new Dictionary<Task<PreVerifyResult>, NzbResolutionCache.Candidate>(taskCandidates);
        var localStarts = new Dictionary<string, DateTimeOffset>(startsAt);
        var localRanks = new Dictionary<string, int>(rankIndex);
        _ = Task.Run(async () =>
        {
            foreach (var t in pending)
            {
                NzbResolutionCache.Candidate? candidate = null;
                string failReason = reason;
                try
                {
                    var r = await t.ConfigureAwait(false);
                    candidate = r.Candidate;
                }
                catch (Exception e)
                {
                    // Fall back to the task→candidate map so the entry still shows up
                    // — previously this was silently dropped.
                    localTaskCandidates.TryGetValue(t, out candidate);
                    failReason = $"Pre-verify task faulted: {e.GetType().Name}: {e.Message}";
                }
                if (candidate is null) continue;
                RecordAttempt(clickId, candidate, contentType, requestedTitle,
                    localRanks.GetValueOrDefault(candidate.NzbUrl, 0),
                    WatchdogEntry.Outcome.Cancelled, failReason, localStarts, isWinner: false,
                    contentGroupKey: contentGroupKey);
            }
        });
    }

    // After a BudgetTimeout the queue item keeps processing in case the player re-clicks.
    // If no click touches it for OrphanIdleThreshold, remove it so we don't burn provider
    // bandwidth downloading a release the player has clearly given up on.
    private void ScheduleOrphanCleanup(Guid nzoId)
    {
        var manager = queueManager;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(OrphanIdleThreshold).ConfigureAwait(false);

                if (!_playLastSeen.TryGetValue(nzoId, out var lastSeen))
                    return; // already cleaned up (success path / earlier cleanup)

                var idle = DateTimeOffset.UtcNow - lastSeen;
                if (idle < OrphanIdleThreshold)
                {
                    // Re-clicked recently — keep the item, check again later.
                    ScheduleOrphanCleanup(nzoId);
                    return;
                }

                await using var ctx = new DavDatabaseContext();
                var freshDbClient = new DavDatabaseClient(ctx);
                var stillPending = await ctx.QueueItems.AsNoTracking()
                    .AnyAsync(q => q.Id == nzoId).ConfigureAwait(false);
                if (!stillPending)
                {
                    _playLastSeen.TryRemove(nzoId, out _);
                    return;
                }

                Log.Information(
                    "Removing orphan play-driven queue item {NzoId} after {IdleSeconds}s with no re-click",
                    nzoId, (int)idle.TotalSeconds);
                await manager.RemoveQueueItemsAsync(new List<Guid> { nzoId }, freshDbClient, CancellationToken.None)
                    .ConfigureAwait(false);
                _playLastSeen.TryRemove(nzoId, out _);
            }
            catch (Exception e)
            {
                Log.Debug(e, "Orphan cleanup for {NzoId} failed", nzoId);
                _playLastSeen.TryRemove(nzoId, out _);
            }
        });
    }

    private async Task RemoveAbortedQueueItemAsync(Guid nzoId)
    {
        _playLastSeen.TryRemove(nzoId, out _);
        try
        {
            await using var ctx = new DavDatabaseContext();
            var freshClient = new DavDatabaseClient(ctx);
            await queueManager.RemoveQueueItemsAsync(new List<Guid> { nzoId }, freshClient, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Log.Debug(e, "Stall-failover abort cleanup failed for {NzoId}", nzoId);
        }
    }

    private void RecordAttempt(
        Guid clickId,
        NzbResolutionCache.Candidate c,
        string contentType,
        string requestedTitle,
        int rankIndex,
        WatchdogEntry.Outcome outcome,
        string? failReason,
        Dictionary<string, DateTimeOffset> startsAt,
        bool isWinner,
        string? contentGroupKey,
        string? providerHost = null)
    {
        var attemptedAt = startsAt.GetValueOrDefault(c.NzbUrl, DateTimeOffset.UtcNow);
        watchdogLog.Record(new WatchdogEntry
        {
            ClickId = clickId,
            AttemptedAt = attemptedAt,
            ContentType = contentType,
            RequestedTitle = requestedTitle,
            CandidateTitle = c.Title,
            IndexerName = c.IndexerName,
            Size = c.Size,
            RankIndex = rankIndex,
            Result = outcome,
            FailReason = failReason,
            DurationMs = (int)Math.Max(0, (DateTimeOffset.UtcNow - attemptedAt).TotalMilliseconds),
            IsWinner = isWinner,
            ProviderHost = providerHost,
            ContentGroupKey = contentGroupKey,
        });
    }

    private string AllConfiguredProvidersDisplay()
    {
        var hosts = configManager.GetUsenetProviderConfig().Providers
            .Select(p => p.Host)
            .Where(h => !string.IsNullOrEmpty(h))
            .Distinct()
            .ToList();
        return hosts.Count == 0 ? "—" : string.Join(", ", hosts);
    }

    private static WatchdogEntry.Outcome MapCommitReason(CommitReason r) => r switch
    {
        CommitReason.Completed => WatchdogEntry.Outcome.QueueCompleted,
        CommitReason.QueueFailed => WatchdogEntry.Outcome.QueueFailed,
        CommitReason.EnqueueFailed => WatchdogEntry.Outcome.EnqueueFailed,
        CommitReason.BudgetTimeout => WatchdogEntry.Outcome.BudgetTimeout,
        CommitReason.Cancelled => WatchdogEntry.Outcome.Cancelled,
        CommitReason.Aborted => WatchdogEntry.Outcome.Cancelled,
        _ => WatchdogEntry.Outcome.QueueFailed,
    };

    private static string? CommitReasonToMessage(CommitReason r) => r switch
    {
        CommitReason.Completed => null,
        CommitReason.QueueFailed => "Queue processing marked the item as Failed",
        CommitReason.EnqueueFailed => "Could not enqueue the NZB (DB or fetch error)",
        CommitReason.BudgetTimeout => "Queue still processing when total budget elapsed",
        CommitReason.Cancelled => "Client disconnected or request cancelled",
        CommitReason.Aborted => "Aborted: no progress within the stall window — trying the next candidate",
        _ => null,
    };

    private enum CommitReason
    {
        Completed,
        QueueFailed,
        EnqueueFailed,
        BudgetTimeout,
        Cancelled,
        Aborted,
    }

    private async Task<PreVerifyResult> PreVerifyAsync(
        NzbResolutionCache.Candidate candidate,
        string verifyMode,
        int verifySampleCount,
        bool detectSidecarSubs,
        CancellationToken ct)
    {
        try
        {
            var shouldDetectSidecarSubs = detectSidecarSubs && string.IsNullOrWhiteSpace(candidate.Subs);
            var preflighted = preflightCache.Get(candidate.NzbUrl);
            if (preflighted is { Verdict: PlaybackFastVerifier.Verdict.Available, NzbBytes: { } cachedBytes })
            {
                return new PreVerifyResult(candidate, cachedBytes, preflighted.Verdict, preflighted.ResponderHost,
                    shouldDetectSidecarSubs && await HasSidecarSubtitlesAsync(cachedBytes).ConfigureAwait(false));
            }

            var pvTimer = Stopwatch.StartNew();
            var nzbBytes = await FetchNzbBytesAsync(candidate, ct).ConfigureAwait(false);
            var msFetch = pvTimer.ElapsedMilliseconds;
            if (nzbBytes is null)
                return new PreVerifyResult(candidate, null, PlaybackFastVerifier.Verdict.Dead, null, false);

            pvTimer.Restart();
            using var verifyStream = new MemoryStream(nzbBytes, writable: false);
            // Playback selection is user-initiated: its probes compete as High at the
            // provider connection gate, unlike background verification.
            var outcome = await fastVerifier
                .VerifyAsync(verifyStream, verifyMode, verifySampleCount, ct,
                    priority: SemaphorePriority.High)
                .ConfigureAwait(false);
            Log.Debug("play-timing preverify {Indexer} fetch={Fetch}ms verify={Verify}ms verdict={Verdict}",
                candidate.IndexerName, msFetch, pvTimer.ElapsedMilliseconds, outcome.Verdict);
            return new PreVerifyResult(candidate, nzbBytes, outcome.Verdict, outcome.ResponderHost,
                shouldDetectSidecarSubs && await HasSidecarSubtitlesAsync(nzbBytes).ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
            return new PreVerifyResult(candidate, null, PlaybackFastVerifier.Verdict.Timeout, null, false);
        }
        catch (Exception e) when (!e.IsCancellationException())
        {
            Log.Debug(e, "Pre-verify failed for {Url}", candidate.NzbUrl);
            return new PreVerifyResult(candidate, null, PlaybackFastVerifier.Verdict.Dead, null, false);
        }
    }

    // Parse the already-fetched NZB to see whether the release ships a sidecar subtitle
    // file (.srt/.ass/...). No network — operates on bytes pre-verify already holds, and
    // only runs when subtitle preference is enabled.
    private static async Task<bool> HasSidecarSubtitlesAsync(byte[] nzbBytes)
    {
        try
        {
            using var stream = new MemoryStream(nzbBytes, writable: false);
            var nzb = await NzbDocument.LoadAsync(stream).ConfigureAwait(false);
            return nzb.Files.Any(f => SubtitlePreference.IsSubtitleFile(f.GetSubjectFileName()));
        }
        catch
        {
            return false; // malformed NZB → no subtitle signal; never fail playback over this
        }
    }

    private async Task<byte[]?> FetchNzbBytesAsync(NzbResolutionCache.Candidate c, CancellationToken ct)
    {
        var preflighted = preflightCache.Get(c.NzbUrl);
        if (preflighted?.NzbBytes is { } cachedBytes) return cachedBytes;

        var skipTlsVerification = configManager.GetIndexerConfig().ShouldSkipTlsVerification(c.IndexerName);
        return await nzbFetchCoalescer.GetOrFetchAsync(c.NzbUrl, c.ProxyUrl, skipTlsVerification, async innerCt =>
        {
            try
            {
                // Throttle NZB downloads to respect each indexer's configured rate limit
                // (MaxRequestsPerMinute). Candidates from a saturated indexer wait their turn while
                // candidates from other indexers in the same batch proceed in parallel.
                var indexer = configManager.GetIndexerConfig().Indexers
                    .FirstOrDefault(x => x.Name == c.IndexerName);
                if (indexer is not null)
                {
                    var hitCheck = await hitTracker
                        .CheckAsync(c.IndexerName, IndexerApiHit.HitType.Download, indexer.DownloadLimit, indexer.HitLimitResetTime, innerCt)
                        .ConfigureAwait(false);
                    if (hitCheck is { Allowed: false })
                    {
                        Log.Information("NZB download skipped for {Indexer}: {Reason}",
                            c.IndexerName, IndexerHitTracker.FormatSkipReason(hitCheck, IndexerApiHit.HitType.Download));
                        return null;
                    }
                    await rateLimiter.WaitAsync(c.IndexerName, indexer.MaxRequestsPerMinute, innerCt).ConfigureAwait(false);
                }

                using var req = new HttpRequestMessage(HttpMethod.Get, c.NzbUrl);
                req.Headers.TryAddWithoutValidation("User-Agent", c.IndexerUserAgent);
                var client = ProxyHttpClientPool.GetClient(c.ProxyUrl, skipTlsVerification);
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(innerCt);
                cts.CancelAfter(NzbFetchTimeout);
                using var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode) return null;
                byte[] bytes;
                try
                {
                    bytes = await HttpContentReadUtil
                        .ReadBoundedAsync(resp.Content, NzbFetchLimits.MaxResponseBytes, cts.Token)
                        .ConfigureAwait(false);
                }
                catch (NzbResponseTooLargeException e)
                {
                    Log.Warning(
                        "NZB fetch rejected oversized response from {Url}. Limit: {Limit} bytes. Reason: {Reason}",
                        c.NzbUrl, e.MaxBytes, e.Message);
                    Log.Debug(e, "NZB fetch oversized response stack");
                    return null;
                }
                _ = hitTracker.RecordAsync(c.IndexerName, IndexerApiHit.HitType.Download, CancellationToken.None);
                return bytes;
            }
            catch (Exception e) when (!e.IsCancellationException())
            {
                Log.Debug("NZB fetch failed for {Url}: {Message}", c.NzbUrl, e.Message);
                return null;
            }
        }, ct).ConfigureAwait(false);
    }

    private async Task<(IActionResult? Result, CommitReason Reason, Guid? NewlyEnqueuedNzoId)> CommitAsync(
        string nzbToken,
        PreVerifyResult preVerify,
        DateTimeOffset deadline,
        CancellationToken ct)
    {
        var c = preVerify.Candidate;
        var commitTimer = Stopwatch.StartNew();
        var nzbBytes = preVerify.NzbBytes!;
        var safeTitle = SanitizeFileName(c.Title);
        var fileName = $"{safeTitle}.nzb";

        var cacheEntry = cache.Get(nzbToken);
        var category = StringUtil.EmptyToNull(cacheEntry?.Type)
                       ?? configManager.GetManualUploadCategory();
        var contentGroupKey = cacheEntry is null ? null : VariantResolver.BuildContentGroupKey(cacheEntry);

        Guid nzoId;
        Guid? newlyEnqueuedNzoId = null;
        try
        {
            var existingQueue = await dbClient.Ctx.QueueItems.AsNoTracking()
                .Where(q =>
                    (q.FileName == fileName && q.Category == category)
                    || (contentGroupKey != null && q.ContentGroupKey == contentGroupKey))
                .OrderByDescending(q => q.CreatedAt)
                .Select(q => (Guid?)q.Id)
                .FirstOrDefaultAsync(ct).ConfigureAwait(false);

            if (existingQueue.HasValue)
            {
                nzoId = existingQueue.Value;
            }
            else if (await FindCompletedReleaseAsync(fileName, category, ct).ConfigureAwait(false) is { } completedId)
            {
                nzoId = completedId;
            }
            else
            {
                using var buffer = new MemoryStream(nzbBytes, writable: false);
                var addFileRequest = new AddFileRequest
                {
                    FileName = fileName,
                    ContentType = "application/x-nzb",
                    NzbFileStream = buffer,
                    Category = category,
                    Priority = QueueItem.PriorityOption.Force,
                    PostProcessing = QueueItem.PostProcessingOption.None,
                    IndexerName = c.IndexerName,
                    ContentGroupKey = contentGroupKey,
                    CancellationToken = ct,
                };
                var addFileController = new AddFileController(HttpContext, dbClient, queueManager, configManager, websocketManager);
                var addResponse = await addFileController.AddFileAsync(addFileRequest).ConfigureAwait(false);
                if (addResponse.NzoIds.Count == 0) return (null, CommitReason.EnqueueFailed, null);
                nzoId = Guid.Parse(addResponse.NzoIds[0]);
                newlyEnqueuedNzoId = nzoId;
                // Tag this queue item as profile-flow so the queue processor doesn't
                // emit a redundant watchdog completion entry — we already log every
                // attempt explicitly via RecordAttempt above.
                sourceTracker.MarkAsProfileFlow(nzoId);
                // Mark this queue item as play-owned so orphan cleanup can identify it.
                _playLastSeen[nzoId] = DateTimeOffset.UtcNow;
            }
        }
        catch (OperationCanceledException)
        {
            return (null, CommitReason.Cancelled, null);
        }
        catch (Exception e)
        {
            Log.Debug(e, "Enqueue failed for {Url}", c.NzbUrl);
            return (null, CommitReason.EnqueueFailed, null);
        }

        // If this click joined an existing play-owned queue item, refresh its
        // last-seen timestamp so orphan cleanup waits for our polling to finish.
        if (newlyEnqueuedNzoId is null && _playLastSeen.ContainsKey(nzoId))
            _playLastSeen[nzoId] = DateTimeOffset.UtcNow;

        var stallFailover = configManager.IsGrabStallFailoverEnabled()
                        && configManager.IsPlaybackWatchdogEnabled()
                        && newlyEnqueuedNzoId.HasValue;
        var stallWindow = TimeSpan.FromSeconds(configManager.GetGrabStallFailoverWindowSeconds());
        var maxPerCandidate = TimeSpan.FromSeconds(configManager.GetGrabStallFailoverCeilingSeconds());
        var candidateStart = DateTimeOffset.UtcNow;
        var lastProgress = -1;
        var lastProgressAt = candidateStart;

        while (DateTimeOffset.UtcNow < deadline)
        {
            if (ct.IsCancellationRequested) return (null, CommitReason.Cancelled, newlyEnqueuedNzoId);

            HistoryItem? history;
            try
            {
                history = await dbClient.Ctx.HistoryItems.AsNoTracking()
                    .FirstOrDefaultAsync(x => x.Id == nzoId, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { return (null, CommitReason.Cancelled, newlyEnqueuedNzoId); }

            if (history is not null)
            {
                _playLastSeen.TryRemove(nzoId, out _);
                if (history.DownloadStatus != HistoryItem.DownloadStatusOption.Completed)
                {
                    Log.Debug("Candidate {Url} processing failed: {Msg}", c.NzbUrl, history.FailMessage);
                    return (null, CommitReason.QueueFailed, newlyEnqueuedNzoId);
                }

                var redirect = await BuildRedirectForHistoryItemAsync(nzoId, cacheEntry, ct).ConfigureAwait(false);
                if (redirect is null) return (null, CommitReason.QueueFailed, newlyEnqueuedNzoId);

                if (newlyEnqueuedNzoId.HasValue && contentGroupKey is not null && variantResolver.IsEnabled)
                {
                    var keyCopy = contentGroupKey;
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await using var bgCtx = new DavDatabaseContext();
                            var bgClient = new DavDatabaseClient(bgCtx);
                            await variantResolver.EnforceCapAsync(bgClient, websocketManager, keyCopy, CancellationToken.None)
                                .ConfigureAwait(false);
                        }
                        catch (Exception e)
                        {
                            Log.Debug(e, "Variants: background cap enforcement failed for group {Group}", keyCopy);
                        }
                    });
                }

                Log.Debug("play-timing commit {Indexer} newEnqueue={NewEnqueue} waited={Waited}ms",
                    c.IndexerName, newlyEnqueuedNzoId.HasValue, commitTimer.ElapsedMilliseconds);
                return (redirect, CommitReason.Completed, newlyEnqueuedNzoId);
            }

            if (stallFailover)
            {
                var inProg = queueManager.FindInProgressQueueItem(nzoId);
                var isMine = inProg is not null;
                var inProgPct = inProg?.ProgressPercentage;
                var now = DateTimeOffset.UtcNow;
                if (!isMine)
                    lastProgressAt = now;
                else if (inProgPct is { } pct && pct != lastProgress)
                {
                    lastProgress = pct;
                    lastProgressAt = now;
                }
                if ((isMine && now - lastProgressAt >= stallWindow) || now - candidateStart >= maxPerCandidate)
                    return (null, CommitReason.Aborted, newlyEnqueuedNzoId);
            }

            // Refresh while actively polling so cleanup doesn't kill an item we're waiting on.
            if (_playLastSeen.ContainsKey(nzoId))
                _playLastSeen[nzoId] = DateTimeOffset.UtcNow;

            try { await Task.Delay(PollInterval, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { return (null, CommitReason.Cancelled, newlyEnqueuedNzoId); }
        }

        // Budget exhausted — caller is expected to schedule orphan cleanup so the
        // queue item doesn't keep downloading a release the player gave up on.
        return (null, CommitReason.BudgetTimeout, newlyEnqueuedNzoId);
    }

    private async Task<Guid?> FindCompletedReleaseAsync(string fileName, string category, CancellationToken ct)
    {
        var brokenIds = negativeCache.SnapshotBrokenHistoryItems();
        var query = dbClient.Ctx.HistoryItems.AsNoTracking()
            .Where(h => h.FileName == fileName
                        && h.Category == category
                        && h.DownloadStatus == HistoryItem.DownloadStatusOption.Completed);
        if (brokenIds.Count > 0)
            query = query.Where(h => !brokenIds.Contains(h.Id));
        return await query
            .OrderByDescending(h => h.CreatedAt)
            .Select(h => (Guid?)h.Id)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
    }

    // Before returning a transient error, re-check whether ANY prior or concurrent download
    // completed for any candidate in this group. Catches the race where another click — or a
    // sonarr/radarr backfill — finished while we were still processing this one. Falls back
    // to a structured error response with Retry-After so clients like Infuse retry instead
    // of surfacing "demux instantly error" on the first failed read.
    private async Task<IActionResult> ResolveExistingOrErrorAsync(
        NzbResolutionCache.Entry entry,
        int statusCode,
        string message,
        int retryAfterSeconds,
        CancellationToken ct)
    {
        if (!ct.IsCancellationRequested)
        {
            try
            {
                var existing = await TryResolveExistingAsync(entry, ct).ConfigureAwait(false);
                if (existing is not null) return existing;

                if (variantResolver.IsEnabled)
                {
                    var fallback = await variantResolver.TryFallbackAfterFailureAsync(dbClient.Ctx, entry, ct)
                        .ConfigureAwait(false);
                    if (fallback is not null)
                    {
                        var redirect = await BuildRedirectForHistoryItemAsync(fallback.Row.HistoryItemId, entry, ct)
                            .ConfigureAwait(false);
                        if (redirect is not null)
                        {
                            Response.Headers["X-Variants-Fallback"] = "1";
                            return redirect;
                        }
                    }
                }
            }
            catch (OperationCanceledException) { /* fall through to error */ }
        }
        Response.Headers["Retry-After"] = retryAfterSeconds.ToString();
        return StatusCode(statusCode, message);
    }

    private async Task<Guid?> FindInFlightQueueItemAsync(NzbResolutionCache.Entry entry, CancellationToken ct)
    {
        var fileNames = entry.Candidates
            .Select(c => $"{SanitizeFileName(c.Title)}.nzb")
            .Distinct()
            .ToList();
        var contentGroupKey = VariantResolver.BuildContentGroupKey(entry);
        if (fileNames.Count == 0 && contentGroupKey is null) return null;

        return await dbClient.Ctx.QueueItems.AsNoTracking()
            .Where(q => fileNames.Contains(q.FileName)
                        || (contentGroupKey != null && q.ContentGroupKey == contentGroupKey))
            .OrderByDescending(q => q.CreatedAt)
            .Select(q => (Guid?)q.Id)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
    }

    // Match against ANY candidate in this group — the winning fallback can land on a
    // release at any position in the ranked list, so matching only by Primary.Title would
    // miss prior clicks that resolved to a different release variant.
    private async Task<IActionResult?> TryResolveExistingAsync(NzbResolutionCache.Entry entry, CancellationToken ct)
    {
        var fileNames = entry.Candidates
            .Select(c => $"{SanitizeFileName(c.Title)}.nzb")
            .Distinct()
            .ToList();
        if (fileNames.Count == 0) return null;

        var brokenIds = negativeCache.SnapshotBrokenHistoryItems();
        var query = dbClient.Ctx.HistoryItems.AsNoTracking()
            .Where(h => fileNames.Contains(h.FileName) && h.DownloadStatus == HistoryItem.DownloadStatusOption.Completed);
        if (brokenIds.Count > 0)
            query = query.Where(h => !brokenIds.Contains(h.Id));

        var matchIds = await query
            .OrderByDescending(h => h.CreatedAt)
            .Select(h => h.Id)
            .ToListAsync(ct).ConfigureAwait(false);

        foreach (var id in matchIds)
        {
            if (ct.IsCancellationRequested) return null;
            var redirect = await BuildRedirectForHistoryItemAsync(id, entry, ct).ConfigureAwait(false);
            if (redirect is not null) return redirect;
        }
        return null;
    }

    private async Task<IActionResult?> BuildRedirectForHistoryItemAsync(
        Guid historyItemId, NzbResolutionCache.Entry? entry, CancellationToken ct)
    {
        var video = await FindBestVideoAsync(historyItemId, entry, ct).ConfigureAwait(false);
        if (video is null) return null;
        var ext = Path.GetExtension(video.Name).TrimStart('.').ToLowerInvariant();
        var redirect = await BuildRedirectAsync(video.Id, ext, ct).ConfigureAwait(false);
        _ = variantResolver.MarkPlayedAsync(historyItemId, CancellationToken.None);
        return redirect;
    }

    private async Task<IActionResult?> WaitForInFlightAsync(Guid nzoId, NzbResolutionCache.Entry entry, CancellationToken ct)
    {
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(configManager.GetPlayTotalBudgetSeconds());
        _playLastSeen[nzoId] = DateTimeOffset.UtcNow;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (ct.IsCancellationRequested) return null;
            HistoryItem? history;
            try
            {
                history = await dbClient.Ctx.HistoryItems.AsNoTracking()
                    .FirstOrDefaultAsync(h => h.Id == nzoId, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { return null; }

            if (history is not null)
            {
                _playLastSeen.TryRemove(nzoId, out _);
                if (history.DownloadStatus != HistoryItem.DownloadStatusOption.Completed) return null;
                return await BuildRedirectForHistoryItemAsync(nzoId, entry, ct).ConfigureAwait(false);
            }

            if (_playLastSeen.ContainsKey(nzoId))
                _playLastSeen[nzoId] = DateTimeOffset.UtcNow;
            try { await Task.Delay(PollInterval, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { return null; }
        }
        return null;
    }

    private async Task<DavItem?> FindLargestVideoAsync(Guid historyItemId, CancellationToken ct)
        => await FindBestVideoAsync(historyItemId, entry: null, ct).ConfigureAwait(false);

    private async Task<DavItem?> FindBestVideoAsync(Guid historyItemId, NzbResolutionCache.Entry? entry, CancellationToken ct)
    {
        var files = await dbClient.Ctx.Items.AsNoTracking()
            .Where(x => x.HistoryItemId == historyItemId)
            .Where(x => x.Type == DavItem.ItemType.UsenetFile)
            .ToListAsync(ct).ConfigureAwait(false);

        var videos = files
            .Where(x => ContentTypeUtil.GetContentType(x.Name).StartsWith("video/", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (videos.Count == 0) return null;

        int? season = null;
        int? episode = null;
        if (entry is not null && TryParseSeasonEpisode(entry.Type, entry.Id, out var reqSeason, out var reqEpisode))
        {
            season = reqSeason;
            episode = reqEpisode;
        }

        var best = VideoSelection.SelectBest(videos, season, episode, entry?.Primary.Title);

        Log.Debug("play selection for {HistoryItemId}: chose {Name} among {Count} videos",
            historyItemId, best?.Name ?? "<none>", videos.Count);
        var skippedDecoys = videos.Where(v => !ReferenceEquals(v, best) && VideoSelection.IsProbablyDecoy(v.Name)).ToList();
        if (skippedDecoys.Count > 0)
        {
            Log.Information(
                "play selection for {HistoryItemId}: skipped {Count} probable decoy file(s): {Names}",
                historyItemId, skippedDecoys.Count, string.Join(", ", skippedDecoys.Select(v => v.Name)));
        }

        return best;
    }

    private static bool TryParseSeasonEpisode(string? type, string? id, out int season, out int episode)
    {
        season = 0;
        episode = 0;
        if (type != "series" || string.IsNullOrEmpty(id)) return false;
        var parts = id.Split(':');
        if (parts.Length < 3) return false;
        return int.TryParse(parts[^2], out season) && int.TryParse(parts[^1], out episode);
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var clean = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        return string.IsNullOrEmpty(clean) ? "untitled" : clean;
    }

    private async Task<IActionResult> BuildRedirectAsync(Guid davItemId, string extension, CancellationToken ct)
    {
        var baseUrl = HttpContext.GetPublicBaseUrl(configManager.GetBaseUrl());
        var path = DatabaseStoreSymlinkFile.GetTargetPath(davItemId, "", '/').TrimStart('/');
        var dlKey = GetWebdavItemRequest.GenerateDownloadKey(configManager.GetStrmKey(), path);
        return Redirect($"{baseUrl}/view/{path}?downloadKey={dlKey}&extension={extension}");
    }

    private readonly record struct PreVerifyResult(
        NzbResolutionCache.Candidate Candidate,
        byte[]? NzbBytes,
        PlaybackFastVerifier.Verdict Verdict,
        string? ResponderHost,
        bool HasSidecarSubs);

    // A candidate "has subtitles" if the indexer declared subs OR the NZB ships a sidecar
    // subtitle file. Used to bias which verified candidate gets committed during failover.
    private static bool HasSubtitles(PreVerifyResult r) =>
        !string.IsNullOrWhiteSpace(r.Candidate.Subs) || r.HasSidecarSubs;
}
