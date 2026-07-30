using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using NzbWebDAV.Clients.RadarrSonarr;
using NzbWebDAV.Clients.RadarrSonarr.BaseModels;
using NzbWebDAV.Clients.Usenet;
using NzbWebDAV.Clients.Usenet.Contexts;
using NzbWebDAV.Config;
using NzbWebDAV.Database;
using NzbWebDAV.Database.Models;
using NzbWebDAV.Exceptions;
using NzbWebDAV.Extensions;
using NzbWebDAV.Queue;
using NzbWebDAV.Queue.PostProcessors;
using NzbWebDAV.Utils;
using NzbWebDAV.Websocket;
using Serilog;

namespace NzbWebDAV.Services;

/// <summary>
/// This service monitors for health checks
/// </summary>
public class HealthCheckService : BackgroundService
{
    private const int MaximumMissingSegmentIds = 100_000;

    // Files at or below this many segments are checked in full, before any aging taper.
    public const int SampleFloor = 8000;

    // A release keeps full depth for its first year, then tapers until it stops aging at ten.
    private const double FullDepthDays = 365;
    private const double MinDepthDays = 3650;

    private readonly ConfigManager _configManager;
    private readonly INntpClient _usenetClient;
    private readonly WebsocketManager _websocketManager;
    private readonly BenchmarkGate _benchmarkGate;
    private readonly StreamingFailureTracker _failureTracker;
    private readonly QueueManager _queueManager;

    private static readonly HashSet<string> _missingSegmentIds = [];
    private static readonly Queue<string> _missingSegmentOrder = [];

    public HealthCheckService
    (
        ConfigManager configManager,
        UsenetStreamingClient usenetClient,
        WebsocketManager websocketManager,
        BenchmarkGate benchmarkGate,
        StreamingFailureTracker failureTracker,
        QueueManager queueManager
    )
    {
        _configManager = configManager;
        _usenetClient = usenetClient;
        _websocketManager = websocketManager;
        _benchmarkGate = benchmarkGate;
        _failureTracker = failureTracker;
        _queueManager = queueManager;

        _configManager.OnConfigChanged += (_, configEventArgs) =>
        {
            // when provider settings change, clear the missing segments cache
            if (!configEventArgs.ChangedConfig.ContainsKey(ConfigKeys.UsenetProviders)) return;
            lock (_missingSegmentIds)
            {
                _missingSegmentIds.Clear();
                _missingSegmentOrder.Clear();
            }
        };
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // pause verification while a connection speed-test is running
                if (_benchmarkGate.IsPaused)
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken).ConfigureAwait(false);
                    continue;
                }

                // if the repair-job is disabled, then don't do anything
                if (!_configManager.IsRepairJobEnabled())
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken).ConfigureAwait(false);
                    continue;
                }

                // Defer new background library health checks while the NZB queue
                // is actively processing. An already-running check finishes normally;
                // queue-item article validation inside QueueItemProcessor is separate.
                if (_queueManager.HasActiveQueueItems)
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken).ConfigureAwait(false);
                    continue;
                }

                // get concurrency (capped to avoid saturating the NNTP pool)
                var concurrency = _configManager.GetHealthCheckConcurrency();
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);

                // get the davItem to health-check
                await using var dbContext = new DavDatabaseContext();
                var dbClient = new DavDatabaseClient(dbContext);
                var currentDateTime = DateTimeOffset.UtcNow;
                var davItem = await GetHealthCheckQueueItems(dbClient)
                    .Where(x => x.NextHealthCheck == null || x.NextHealthCheck < currentDateTime)
                    .FirstOrDefaultAsync(cts.Token).ConfigureAwait(false);

                // if there is no item to health-check, don't do anything
                if (davItem == null)
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), cts.Token).ConfigureAwait(false);
                    continue;
                }

                // perform the health check
                await PerformHealthCheck(davItem, dbClient, concurrency, cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (SigtermUtil.IsSigtermTriggered())
            {
                // OperationCanceledException is expected on sigterm
                return;
            }
            catch (Exception e)
            {
                if (e.TryGetKnownErrorMessage(out var reason))
                {
                    Log.Warning("Background health check deferred. Reason: {Reason}", reason);
                    Log.Debug(e, "Background health check known failure stack");
                }
                else
                {
                    Log.Error(e, "Unexpected error performing background health checks: {Message}", e.Message);
                }

                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken).ConfigureAwait(false);
            }
        }
    }

    public static IOrderedQueryable<DavItem> GetHealthCheckQueueItems(DavDatabaseClient dbClient)
    {
        // Non-null NextHealthCheck first (includes urgent UnixEpoch from dynamic repair),
        // then ascending NextHealthCheck, then newest releases.
        return GetHealthCheckQueueItemsQuery(dbClient)
            .OrderBy(x => x.NextHealthCheck == null ? 1 : 0)
            .ThenBy(x => x.NextHealthCheck)
            .ThenByDescending(x => x.ReleaseDate)
            .ThenBy(x => x.Id);
    }

    public static IQueryable<DavItem> GetHealthCheckQueueItemsQuery(DavDatabaseClient dbClient)
    {
        // History-linked files are skipped for routine STAT checks so they do not race SAB
        // post-processing. UnixEpoch is the playback-triggered urgent sentinel from
        // ExceptionMiddleware and intentionally overrides only that exclusion.
        return dbClient.Ctx.Items
            .Where(x => x.Type == DavItem.ItemType.UsenetFile)
            .Where(x =>
                x.HistoryItemId == null ||
                x.NextHealthCheck == DateTimeOffset.UnixEpoch);
    }

    private async Task PerformHealthCheck
    (
        DavItem davItem,
        DavDatabaseClient dbClient,
        int concurrency,
        CancellationToken ct
    )
    {
        // Urgent sentinel set by ExceptionMiddleware when streaming confirms a permanent failure.
        // Skip the STAT-only recheck and repair immediately: STAT can pass while BODY returns 430
        // (see nzbdav-dev#209), and structurally corrupt archives can have every article present.
        var isUrgentRepair = davItem.NextHealthCheck == DateTimeOffset.UnixEpoch;

        // Attribution for latency histograms — does not change pool admission priority.
        using var maintenanceScope = ct.SetContext(MaintenanceDownloadContext.Instance);

        try
        {
            if (isUrgentRepair)
            {
                Log.Information("Performing urgent dynamic repair for {FilePath}", davItem.Path);
                await HandleUrgentRepair(davItem, dbClient, ct).ConfigureAwait(false);
                return;
            }

            // update the release date, if null
            var segments = await GetAllSegments(davItem, dbClient, ct).ConfigureAwait(false);
            if (davItem.ReleaseDate == null) await UpdateReleaseDate(davItem, segments, ct).ConfigureAwait(false);

            // sample large files to reduce NNTP load while keeping head/tail/stride coverage
            var totalSegments = segments.Count;
            var age = _configManager.IsHealthCheckAgingEnabled() && davItem.ReleaseDate is { } posted
                ? DateTimeOffset.UtcNow - posted
                : (TimeSpan?)null;
            var sampled = SampleSegments(segments, _configManager.GetHealthCheckDepth(), age);

            // setup progress tracking
            var progressHook = new Progress<int>();
            var debounce = DebounceUtil.CreateDebounce(TimeSpan.FromMilliseconds(200));
            progressHook.ProgressChanged += (_, progress) =>
            {
                var message = $"{davItem.Id}|{progress}";
                debounce(() => _websocketManager.SendMessage(WebsocketTopic.HealthItemProgress, message));
            };

            // perform health check
            var progress = progressHook.ToPercentage(sampled.Count);
            await ArticleExistenceChecker.CheckAsync(_usenetClient, sampled, concurrency, progress, ct)
                .ConfigureAwait(false);
            _ = _websocketManager.SendMessage(WebsocketTopic.HealthItemProgress, $"{davItem.Id}|100");
            _ = _websocketManager.SendMessage(WebsocketTopic.HealthItemProgress, $"{davItem.Id}|done");

            // update the database.
            // the next check is scheduled so the interval doubles with the item's age since release.
            // clamp to a minimum interval: a null release-date (zero-segment item) or a future-dated
            // article header would otherwise schedule the item in the past and hot-loop the service.
            var utcNow = DateTimeOffset.UtcNow;
            davItem.LastHealthCheck = utcNow;
            davItem.NextHealthCheck = ComputeNextHealthCheck(davItem.ReleaseDate, utcNow);
            _failureTracker.ClearFailure(davItem.Id);
            var healthyMessage = sampled.Count < totalSegments
                ? $"File is healthy (sampled {sampled.Count}/{totalSegments} segments)."
                : "File is healthy.";
            await RecordHealthResult(
                dbClient, davItem,
                HealthCheckResult.HealthResult.Healthy,
                HealthCheckResult.RepairAction.None,
                healthyMessage, ct).ConfigureAwait(false);
        }
        catch (UsenetArticleNotFoundException e)
        {
            _ = _websocketManager.SendMessage(WebsocketTopic.HealthItemProgress, $"{davItem.Id}|100");
            _ = _websocketManager.SendMessage(WebsocketTopic.HealthItemProgress, $"{davItem.Id}|done");
            if (FilenameUtil.IsImportantFileType(davItem.Name))
            {
                lock (_missingSegmentIds)
                {
                    if (_missingSegmentIds.Add(e.SegmentId))
                        _missingSegmentOrder.Enqueue(e.SegmentId);
                    while (_missingSegmentIds.Count > MaximumMissingSegmentIds)
                        _missingSegmentIds.Remove(_missingSegmentOrder.Dequeue());
                }
            }

            // when usenet article is missing, perform repairs
            await Repair(davItem, dbClient, ct).ConfigureAwait(false);
        }
        catch (UsenetUnexpectedResponseException e)
        {
            // Connection-level STAT failures (e.g. buffered 400 goodbye) must not trigger
            // repair or leave NextHealthCheck unset — defer and surface ActionNeeded.
            _ = _websocketManager.SendMessage(WebsocketTopic.HealthItemProgress, $"{davItem.Id}|100");
            _ = _websocketManager.SendMessage(WebsocketTopic.HealthItemProgress, $"{davItem.Id}|done");
            var utcNow = DateTimeOffset.UtcNow;
            davItem.LastHealthCheck = utcNow;
            davItem.NextHealthCheck = utcNow + TimeSpan.FromDays(1);
            await RecordHealthResult(
                dbClient, davItem,
                HealthCheckResult.HealthResult.Unhealthy,
                HealthCheckResult.RepairAction.ActionNeeded,
                $"Unexpected NNTP response during health check: {e.Message}", ct).ConfigureAwait(false);
        }
        catch (Exception e) when (e.IsTransientTransportException())
        {
            // STAT/read timeouts and socket/IO failures must not dump stacks or trigger Arr repair —
            // defer and surface ActionNeeded with a single human-readable Warning.
            _ = _websocketManager.SendMessage(WebsocketTopic.HealthItemProgress, $"{davItem.Id}|100");
            _ = _websocketManager.SendMessage(WebsocketTopic.HealthItemProgress, $"{davItem.Id}|done");
            var utcNow = DateTimeOffset.UtcNow;
            davItem.LastHealthCheck = utcNow;
            davItem.NextHealthCheck = utcNow + TimeSpan.FromDays(1);
            e.TryGetKnownErrorMessage(out var reason);
            Log.Warning(
                "NNTP transport failure during health check for {Path}. Deferred next check. Reason: {Reason}",
                davItem.Path, reason);
            Log.Debug(e, "Health check transport failure stack for {Path}", davItem.Path);
            await RecordHealthResult(
                dbClient, davItem,
                HealthCheckResult.HealthResult.Unhealthy,
                HealthCheckResult.RepairAction.ActionNeeded,
                FormatTransportFailureHealthMessage(reason), ct).ConfigureAwait(false);
        }
        catch (Exception e) when (!e.IsCancellationException(ct))
        {
            await DeferHealthCheck(davItem, dbClient, e, ct).ConfigureAwait(false);
        }
    }

    private async Task DeferHealthCheck(
        DavItem davItem,
        DavDatabaseClient dbClient,
        Exception exception,
        CancellationToken ct)
    {
        var isKnownFailure = exception.TryGetKnownErrorMessage(out var reason);
        var utcNow = DateTimeOffset.UtcNow;
        davItem.LastHealthCheck = utcNow;
        davItem.NextHealthCheck = ComputeFailureNextHealthCheck(utcNow, isKnownFailure);

        _ = _websocketManager.SendMessage(WebsocketTopic.HealthItemProgress, $"{davItem.Id}|100");
        _ = _websocketManager.SendMessage(WebsocketTopic.HealthItemProgress, $"{davItem.Id}|done");

        if (isKnownFailure)
        {
            Log.Warning(
                "Health check failed for {Path}. Deferred next check until {NextHealthCheck}. Reason: {Reason}",
                davItem.Path, davItem.NextHealthCheck, reason);
            Log.Debug(exception, "Health check known failure stack for {Path}", davItem.Path);
        }
        else
        {
            Log.Error(
                exception,
                "Unexpected error during health check for {Path}. Deferred next check until {NextHealthCheck}",
                davItem.Path, davItem.NextHealthCheck);
        }

        try
        {
            await RecordHealthResult(
                dbClient, davItem,
                HealthCheckResult.HealthResult.Unhealthy,
                HealthCheckResult.RepairAction.ActionNeeded,
                isKnownFailure
                    ? $"Health check deferred: {reason}"
                    : $"Unexpected error during health check: {exception.Message}",
                ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception persistenceException)
        {
            Log.Error(
                persistenceException,
                "Could not record deferred health-check result for {Path}; retrying schedule persistence",
                davItem.Path);

            foreach (var entry in dbClient.Ctx.ChangeTracker.Entries<HealthCheckResult>()
                         .Where(x => x.State == EntityState.Added))
                entry.State = EntityState.Detached;

            try
            {
                await dbClient.Ctx.SaveChangesAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception scheduleException)
            {
                Log.Error(
                    scheduleException,
                    "Could not persist deferred health-check schedule for {Path}",
                    davItem.Path);
            }
        }
    }

    /// <summary>
    /// Health-result message for deferred NNTP transport failures (timeouts, socket/IO).
    /// </summary>
    internal static string FormatTransportFailureHealthMessage(string reason) =>
        $"NNTP transport failure during health check: {reason}";

    /// <summary>
    /// Schedules a failed health check far enough in the future to avoid starving
    /// other items, while allowing known provider/configuration failures more time
    /// to be corrected than unexpected application failures.
    /// </summary>
    public static DateTimeOffset ComputeFailureNextHealthCheck(
        DateTimeOffset utcNow,
        bool knownFailure) =>
        utcNow + (knownFailure ? TimeSpan.FromDays(1) : TimeSpan.FromHours(1));

    /// <summary>
    /// Schedules the next health check so the interval doubles with the item's age since release,
    /// floored at one hour from <paramref name="utcNow"/> so null or future release dates cannot
    /// schedule the item in the past and hot-loop the service.
    /// </summary>
    public static DateTimeOffset ComputeNextHealthCheck(DateTimeOffset? releaseDate, DateTimeOffset utcNow)
    {
        var minimumNextHealthCheck = utcNow + TimeSpan.FromHours(1);
        var nextHealthCheck = releaseDate + 2 * (utcNow - releaseDate);
        return nextHealthCheck == null || nextHealthCheck < minimumNextHealthCheck
            ? minimumNextHealthCheck
            : nextHealthCheck.Value;
    }

    /// <summary>
    /// How many segments to STAT for one file. Files up to the floor are checked in full,
    /// larger ones are sampled based on their size, and an optional age scales the result
    /// down from there. <see cref="HealthCheckDepth.Complete"/> skips all of it.
    /// </summary>
    public static int SampleTarget(int segmentCount, HealthCheckDepth depth, TimeSpan? age = null)
    {
        if (depth == HealthCheckDepth.Complete) return segmentCount;
        var multiplier = CurveMultiplier(depth);
        var curve = Math.Max(SampleFloor, multiplier * Math.Sqrt((double)SampleFloor * segmentCount));
        return (int)Math.Min(segmentCount, curve * AgeWeight(age));
    }

    /// <summary>
    /// What each depth multiplies the sampling curve by. Complete has no multiplier because
    /// it never reaches the curve.
    /// </summary>
    private static double CurveMultiplier(HealthCheckDepth depth) => depth switch
    {
        HealthCheckDepth.Standard => 0.5,
        HealthCheckDepth.Enhanced => 1.0,
        HealthCheckDepth.Deep => 2.0,
        _ => throw new ArgumentOutOfRangeException(nameof(depth), depth, "Depth has no curve multiplier."),
    };

    /// <summary>
    /// Scales coverage down as a release ages with the same square root curve used for size.
    /// A post that has survived its first year is far less likely to be broken, so it
    /// gets a lighter check. The taper stops at ten years so nothing decays toward zero.
    /// </summary>
    private static double AgeWeight(TimeSpan? age)
    {
        if (age is not { } elapsed) return 1.0;
        return Math.Sqrt(FullDepthDays / Math.Clamp(elapsed.TotalDays, FullDepthDays, MinDepthDays));
    }

    /// <summary>
    /// Returns a stratified sample of <paramref name="segments"/>: first 100, last 100, and
    /// evenly spaced middle segments, sized by <see cref="SampleTarget"/>.
    /// </summary>
    public static List<string> SampleSegments(
        List<string> segments,
        HealthCheckDepth depth = ConfigManager.DefaultHealthCheckDepth,
        TimeSpan? age = null)
    {
        var count = segments.Count;
        var target = SampleTarget(count, depth, age);
        if (count <= target) return segments;

        const int headCount = 100;
        const int tailCount = 100;

        var result = new HashSet<int>();

        for (var i = 0; i < Math.Min(headCount, count); i++)
            result.Add(i);

        for (var i = Math.Max(0, count - tailCount); i < count; i++)
            result.Add(i);

        // Spread `target` picks across the file with a running remainder, so the sample lands
        // on the target instead of on the nearest whole number stride.
        var carry = 0L;
        for (var i = 0; i < count; i++)
        {
            carry += target;
            if (carry < count) continue;
            carry -= count;
            result.Add(i);
        }

        return result.OrderBy(i => i).Select(i => segments[i]).ToList();
    }

    private async Task UpdateReleaseDate(DavItem davItem, List<string> segments, CancellationToken ct)
    {
        var firstSegmentId = StringUtil.EmptyToNull(segments.FirstOrDefault());
        if (firstSegmentId == null) return;
        var articleHeadersResponse = await _usenetClient.HeadAsync(firstSegmentId, ct).ConfigureAwait(false);
        if (articleHeadersResponse.ArticleHeaders is not { } articleHeaders)
            throw new UsenetUnexpectedResponseException(
                firstSegmentId, articleHeadersResponse.ResponseMessage);
        davItem.ReleaseDate = articleHeaders.Date;
    }

    private async Task<List<string>> GetAllSegments(DavItem davItem, DavDatabaseClient dbClient, CancellationToken ct)
    {
        if (davItem.SubType == DavItem.ItemSubType.NzbFile)
        {
            var nzbFile = await dbClient.GetDavNzbFileAsync(davItem, ct).ConfigureAwait(false);
            return nzbFile?.SegmentIds?.ToList() ?? [];
        }

        if (davItem.SubType == DavItem.ItemSubType.RarFile)
        {
            var rarFile = await dbClient.GetDavRarFileAsync(davItem, ct).ConfigureAwait(false);
            return rarFile?.RarParts?.SelectMany(x => x.SegmentIds)?.ToList() ?? [];
        }

        if (davItem.SubType == DavItem.ItemSubType.MultipartFile)
        {
            var multipartFile = await dbClient.GetDavMultipartFileAsync(davItem, ct).ConfigureAwait(false);
            return multipartFile?.Metadata?.FileParts?.SelectMany(x => x.SegmentIds)?.ToList() ?? [];
        }

        return [];
    }

    /// <summary>
    /// How an urgent (streaming-triggered) repair should proceed given the streaming-failure threshold.
    /// </summary>
    public enum UrgentRepairDisposition
    {
        /// <summary>Call Repair() with today's behavior (Arr search or orphan delete).</summary>
        RepairNormally,
        /// <summary>Clear the urgent flag and wait for more streaming failures.</summary>
        Defer,
        /// <summary>Force the repair-delete path even for library-linked items.</summary>
        ForceDelete,
    }

    /// <summary>
    /// Decides urgent-repair disposition from failure count and auto-remove config.
    /// Threshold 0 preserves immediate repair; otherwise all streaming-triggered actions wait
    /// until the configured number of consecutive failures.
    /// </summary>
    public static UrgentRepairDisposition GetUrgentRepairDisposition(
        int threshold,
        int failureCount,
        bool hasLibraryLink,
        bool autoRemoveUnlinkedOnly)
    {
        if (threshold <= 0)
            return UrgentRepairDisposition.RepairNormally;

        if (failureCount < threshold)
            return UrgentRepairDisposition.Defer;

        if (hasLibraryLink && autoRemoveUnlinkedOnly)
            return UrgentRepairDisposition.RepairNormally;

        return UrgentRepairDisposition.ForceDelete;
    }

    /// <summary>
    /// Outcome of consulting Arr instances for a library-linked unhealthy item.
    /// </summary>
    public enum ArrLinkedRepairDecision
    {
        /// <summary>An Arr instance owned the file and remove-and-search succeeded.</summary>
        RemoveAndSearchSucceeded,
        /// <summary>
        /// At least one Arr instance was unreachable/unusable and no instance confirmed the link
        /// as an orphan — leave the DavItem in place.
        /// </summary>
        DeferUnreachable,
        /// <summary>Ownership was fully determined: no Arr media-item; safe to delete.</summary>
        DeleteConfirmedOrphan,
    }

    /// <summary>
    /// Consults Arr clients to decide whether a library-linked unhealthy item should trigger
    /// remove-and-search, be deferred while an instance is down, or be deleted as a confirmed orphan.
    /// Extracted so the unreachable-instance fail-safe can be unit-tested without a full Repair harness.
    /// </summary>
    internal static async Task<ArrLinkedRepairDecision> DecideArrLinkedRepairAsync(
        IEnumerable<ArrClient> arrClients,
        string symlinkOrStrmPath,
        CancellationToken ct)
    {
        // Track whether we could fully determine ownership. If any instance was unreachable,
        // errored, or returned an unusable root folder, a later "no owner found" is unreliable
        // and we must not delete a link one of those instances may own. This is independent of
        // ownerConfirmedOrphan below, which is a definite answer and deletes regardless.
        var anInstanceFailed = false;
        var ownerConfirmedOrphan = false;

        foreach (var arrClient in arrClients)
        {
            ct.ThrowIfCancellationRequested();

            List<ArrRootFolder> rootFolders;
            try
            {
                rootFolders = await arrClient.GetRootFolders().ConfigureAwait(false);
            }
            catch (Exception e)
            {
                anInstanceFailed = true;
                LogArrRepairFailure(
                    e,
                    "Health-check repair: could not query root folders from {Host}",
                    arrClient.Host);
                continue;
            }

            // Skip null/empty paths: StartsWith(null) throws, and StartsWith("") matches everything.
            // A null/empty path is a malformed response we can't rule this instance in or out
            // with, so if nothing else matches, treat it like an unreachable instance rather
            // than falling through to a delete this instance may not have sanctioned.
            if (!rootFolders.Any(x => !string.IsNullOrEmpty(x.Path) && symlinkOrStrmPath.StartsWith(x.Path)))
            {
                if (rootFolders.Any(x => string.IsNullOrEmpty(x.Path))) anInstanceFailed = true;
                continue;
            }

            bool removedAndSearched;
            try
            {
                removedAndSearched = await arrClient.RemoveAndSearch(symlinkOrStrmPath).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                anInstanceFailed = true;
                LogArrRepairFailure(
                    e,
                    "Health-check repair: remove-and-search failed on {Host}",
                    arrClient.Host);
                continue;
            }

            if (removedAndSearched)
                return ArrLinkedRepairDecision.RemoveAndSearchSucceeded;

            // The owning instance was reached and reports no corresponding media-item, so this
            // link is a confirmed orphan. Record that ownership was determined and break; the
            // delete below then proceeds even if some other instance was unreachable.
            ownerConfirmedOrphan = true;
            break;
        }

        if (anInstanceFailed && !ownerConfirmedOrphan)
            return ArrLinkedRepairDecision.DeferUnreachable;

        return ArrLinkedRepairDecision.DeleteConfirmedOrphan;
    }

    private static void LogArrRepairFailure(Exception exception, string messageTemplate, string host)
    {
        if (exception.TryGetCausingException<HttpRequestException>(out var httpException) &&
            httpException is not null)
        {
            var reason = httpException.StatusCode is { } statusCode
                ? $"HTTP {(int)statusCode} ({statusCode})"
                : httpException.Message;
            Log.Warning(messageTemplate + ". Reason: {Reason}", host, reason);
            Log.Debug(exception, "Health-check repair Arr HTTP failure stack");
            return;
        }

        if (exception.TryGetKnownErrorMessage(out var knownReason))
        {
            Log.Warning(messageTemplate + ". Reason: {Reason}", host, knownReason);
            Log.Debug(exception, "Health-check repair Arr known failure stack");
            return;
        }

        Log.Warning(exception, messageTemplate, host);
    }

    private async Task HandleUrgentRepair(DavItem davItem, DavDatabaseClient dbClient, CancellationToken ct)
    {
        var threshold = _configManager.GetAutoRemoveAfterFailures();
        var failureCount = _failureTracker.GetFailureCount(davItem.Id);
        var unlinkedOnly = _configManager.IsAutoRemoveUnlinkedOnly();
        var hasLibraryLink = OrganizedLinksUtil.GetLink(davItem, _configManager) != null;
        var disposition = GetUrgentRepairDisposition(threshold, failureCount, hasLibraryLink, unlinkedOnly);

        if (disposition == UrgentRepairDisposition.Defer)
        {
            var utcNow = DateTimeOffset.UtcNow;
            davItem.LastHealthCheck = utcNow;
            davItem.NextHealthCheck = utcNow + TimeSpan.FromHours(1);
            await RecordHealthResult(
                dbClient, davItem,
                HealthCheckResult.HealthResult.Unhealthy,
                HealthCheckResult.RepairAction.ActionNeeded,
                string.Join(" ", [
                    "File failed during streaming.",
                    $"Streaming failure count: {failureCount}/{threshold}.",
                    "Repair and replacement deferred until the failure threshold is reached."
                ]), ct).ConfigureAwait(false);
            return;
        }

        await Repair(
            davItem,
            dbClient,
            ct,
            forceDelete: disposition == UrgentRepairDisposition.ForceDelete,
            streamingFailureCount: failureCount).ConfigureAwait(false);
    }

    private async Task Repair(
        DavItem davItem,
        DavDatabaseClient dbClient,
        CancellationToken ct,
        bool forceDelete = false,
        int? streamingFailureCount = null)
    {
        try
        {
            // if the file pattern has been marked as ignored,
            // then don't bother trying to repair it. We can simply delete it.
            var blocklistedFiles = _configManager.GetBlocklistedFiles();
            if (BlocklistedFilePostProcessor.MatchesAnyPattern(davItem.Name, blocklistedFiles))
            {
                DeletionAuditLog.Record(
                    "health-repair",
                    davItem,
                    "health validation failed; filename matches blocklist pattern");
                dbClient.Ctx.Items.Remove(davItem);
                _failureTracker.ClearFailure(davItem.Id);
                await RecordHealthResult(
                    dbClient, davItem,
                    HealthCheckResult.HealthResult.Unhealthy,
                    HealthCheckResult.RepairAction.Deleted,
                    string.Join(" ", [
                        "File failed health validation.",
                        "Filename pattern is marked in settings as an ignored (unwanted) file.",
                        "Deleted file."
                    ]), ct).ConfigureAwait(false);
                return;
            }

            // if the unhealthy item is unlinked/orphaned, or auto-remove force-delete is requested,
            // then we can simply delete it (and the library link when present).
            var symlinkOrStrmPath = OrganizedLinksUtil.GetLink(davItem, _configManager);
            if (symlinkOrStrmPath == null || forceDelete)
            {
                if (forceDelete && symlinkOrStrmPath != null)
                    await Task.Run(() => File.Delete(symlinkOrStrmPath)).ConfigureAwait(false);

                var auditReason = forceDelete
                    ? streamingFailureCount is > 0
                        ? $"auto-removed after repeated streaming failures (count={streamingFailureCount})"
                        : "auto-removed after repeated streaming failures"
                    : "health validation failed; orphaned (no library symlink/strm)";
                DeletionAuditLog.Record("health-repair", davItem, auditReason);

                dbClient.Ctx.Items.Remove(davItem);
                _failureTracker.ClearFailure(davItem.Id);

                var failureNote = streamingFailureCount is > 0
                    ? $" Streaming failure count: {streamingFailureCount}."
                    : "";
                string deleteMessage;
                if (forceDelete && symlinkOrStrmPath != null)
                {
                    var forceDeleteLinkType = symlinkOrStrmPath.ToLower().EndsWith("strm") ? "strm-file" : "symlink";
                    deleteMessage = string.Join(" ", [
                        "File failed during streaming.",
                        $"Auto-removed after repeated streaming failures.{failureNote}",
                        $"Deleted the webdav-file and {forceDeleteLinkType}."
                    ]);
                }
                else if (forceDelete)
                {
                    deleteMessage = string.Join(" ", [
                        "File failed during streaming.",
                        $"Auto-removed after repeated streaming failures.{failureNote}",
                        "Deleted file."
                    ]);
                }
                else
                {
                    deleteMessage = string.Join(" ", [
                        "File failed health validation.",
                        "Could not find corresponding symlink or strm-file within Library Dir.",
                        "Deleted file."
                    ]);
                }

                await RecordHealthResult(
                    dbClient, davItem,
                    HealthCheckResult.HealthResult.Unhealthy,
                    HealthCheckResult.RepairAction.Deleted,
                    deleteMessage, ct).ConfigureAwait(false);
                return;
            }

            // if the unhealthy item is linked within the organized media-library
            // then we must find the corresponding arr instance and trigger a new search.
            var linkType = symlinkOrStrmPath.ToLower().EndsWith("strm") ? "strm-file" : "symlink";
            var arrDecision = await DecideArrLinkedRepairAsync(
                _configManager.GetArrConfig().GetArrClients(),
                symlinkOrStrmPath,
                ct).ConfigureAwait(false);

            if (arrDecision == ArrLinkedRepairDecision.RemoveAndSearchSucceeded)
            {
                DeletionAuditLog.Record(
                    "health-repair",
                    davItem,
                    "health validation failed; Arr remove-and-search triggered");
                dbClient.Ctx.Items.Remove(davItem);
                _failureTracker.ClearFailure(davItem.Id);
                await RecordHealthResult(
                    dbClient, davItem,
                    HealthCheckResult.HealthResult.Unhealthy,
                    HealthCheckResult.RepairAction.Repaired,
                    string.Join(" ", [
                        "File failed health validation.",
                        $"Corresponding {linkType} found within Library Dir.",
                        "Triggered new Arr search."
                    ]), ct).ConfigureAwait(false);
                return;
            }

            // Ownership indeterminate (an instance could not be reached or fully queried, and no
            // instance confirmed the file as an orphan): don't delete a link it may own. Defer like
            // the catch below so the item isn't re-selected every scan cycle while the instance is down.
            if (arrDecision == ArrLinkedRepairDecision.DeferUnreachable)
            {
                var utcNow = DateTimeOffset.UtcNow;
                davItem.LastHealthCheck = utcNow;
                davItem.NextHealthCheck = utcNow + TimeSpan.FromDays(1);
                await RecordHealthResult(
                    dbClient, davItem,
                    HealthCheckResult.HealthResult.Unhealthy,
                    HealthCheckResult.RepairAction.ActionNeeded,
                    string.Join(" ", [
                        "File failed health validation.",
                        $"Corresponding {linkType} found within Library Dir,",
                        "but at least one Arr instance could not be reached or fully queried, so ownership",
                        "of the file could not be determined. Leaving the file in place rather than deleting it."
                    ]), ct).ConfigureAwait(false);
                return;
            }

            // if we could not find a corresponding arr instance
            // then we can delete both the item and the link-file.
            await Task.Run(() => File.Delete(symlinkOrStrmPath)).ConfigureAwait(false);
            DeletionAuditLog.Record(
                "health-repair",
                davItem,
                "health validation failed; library link present but no Arr media-item (confirmed orphan)");
            dbClient.Ctx.Items.Remove(davItem);
            _failureTracker.ClearFailure(davItem.Id);
            await RecordHealthResult(
                dbClient, davItem,
                HealthCheckResult.HealthResult.Unhealthy,
                HealthCheckResult.RepairAction.Deleted,
                string.Join(" ", [
                    "File failed health validation.",
                    $"Corresponding {linkType} found within Library Dir.",
                    "Could not find corresponding Radarr/Sonarr media-item to trigger a new search.",
                    $"Deleted the webdav-file and {linkType}."
                ]), ct).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            // if an error is encountered during repairs,
            // then mark the item as unhealthy, and check again in a day.
            var utcNow = DateTimeOffset.UtcNow;
            davItem.LastHealthCheck = utcNow;
            davItem.NextHealthCheck = utcNow + TimeSpan.FromDays(1);
            await RecordHealthResult(
                dbClient, davItem,
                HealthCheckResult.HealthResult.Unhealthy,
                HealthCheckResult.RepairAction.ActionNeeded,
                $"Error performing file repair: {e.Message}", ct).ConfigureAwait(false);
        }
    }


    private async Task RecordHealthResult
    (
        DavDatabaseClient dbClient,
        DavItem davItem,
        HealthCheckResult.HealthResult result,
        HealthCheckResult.RepairAction repairStatus,
        string message,
        CancellationToken ct
    )
    {
        dbClient.Ctx.HealthCheckResults.Add(SendStatus(new HealthCheckResult()
        {
            Id = Guid.NewGuid(),
            DavItemId = davItem.Id,
            Path = davItem.Path,
            CreatedAt = DateTimeOffset.UtcNow,
            Result = result,
            RepairStatus = repairStatus,
            Message = message
        }));
        await dbClient.Ctx.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private HealthCheckResult SendStatus(HealthCheckResult result)
    {
        _ = _websocketManager.SendMessage
        (
            WebsocketTopic.HealthItemStatus,
            $"{result.DavItemId}|{(int)result.Result}|{(int)result.RepairStatus}"
        );
        return result;
    }

    public static void AddMissingSegmentIds(IEnumerable<string> segmentIds)
    {
        lock (_missingSegmentIds)
        {
            foreach (var segmentId in segmentIds)
            {
                if (_missingSegmentIds.Add(segmentId))
                    _missingSegmentOrder.Enqueue(segmentId);
                while (_missingSegmentIds.Count > MaximumMissingSegmentIds)
                    _missingSegmentIds.Remove(_missingSegmentOrder.Dequeue());
            }
        }
    }

    public static void CheckCachedMissingSegmentIds(IEnumerable<string> segmentIds)
    {
        lock (_missingSegmentIds)
        {
            foreach (var segmentId in segmentIds)
                if (_missingSegmentIds.Contains(segmentId))
                    throw new UsenetArticleNotFoundException(segmentId);
        }
    }
}
