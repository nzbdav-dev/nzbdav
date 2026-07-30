using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using NzbWebDAV.Clients.Usenet.Concurrency;
using NzbWebDAV.Clients.Usenet.Connections;
using NzbWebDAV.Clients.Usenet.Contexts;
using NzbWebDAV.Clients.Usenet.Models;
using NzbWebDAV.Exceptions;
using NzbWebDAV.Extensions;
using NzbWebDAV.Models;
using NzbWebDAV.Services.Metrics;
using NzbWebDAV.Services.StreamTrace;
using Serilog;
using UsenetSharp.Models;

namespace NzbWebDAV.Clients.Usenet;

/// <summary>
/// This client is responsible for delegating NNTP commands to a connection pool.
///   * The connection pool enforces a maximum number of allowed connections
///   * When a connection is available, the NNTP command executes immediately
///   * When a connection is not available, the NNTP command waits until a connection becomes available.
///   * When multiple commands are awaiting a connection, admission follows the
///     download-priority context on the caller's token: playback work (WebDAV reads and
///     playback verification) waits in the High lane, while queue and background
///     maintenance work waits in the Low lane. The pool's Streaming Priority odds decide
///     how the two lanes share connections while both are occupied.
/// </summary>
/// <param name="connectionPool"></param>
/// <param name="type"></param>
/// <param name="circuitBreaker"></param>
/// <param name="providerName">NNTP hostname used for connection/logging.</param>
/// <param name="metricsKey">Stable per-account metrics key (ProviderId).</param>
[SuppressMessage("ReSharper", "AccessToDisposedClosure")]
public class MultiConnectionNntpClient(
    ConnectionPool<INntpClient> connectionPool,
    ProviderType type,
    ProviderCircuitBreaker circuitBreaker,
    string providerName,
    long? byteLimit = null,
    long bytesUsedOffset = 0,
    int priority = 0,
    int? pipeliningDepth = null,
    string storageGroup = "",
    string? metricsKey = null,
    ProviderLatencyTracker? latencyTracker = null
) : NntpClient
{
    public ProviderType ProviderType { get; } = type;
    public int Priority { get; } = priority;
    public string Host { get; } = providerName;
    /// <summary>
    /// Stable per-account key for bandwidth/usage metrics. Distinct from
    /// <see cref="Host"/> so multiple accounts on the same NNTP host do not share counters.
    /// </summary>
    public string MetricsKey { get; } = string.IsNullOrEmpty(metricsKey) ? providerName : metricsKey;
    public string StorageGroup { get; } = storageGroup;

    private static readonly ConcurrentDictionary<string, int> TimeoutCounts = new();
    private static long _lastTimeoutFlushTicks = DateTime.UtcNow.Ticks;

    // Commands that transfer an article body. Only these feed the circuit breaker, so the
    // failure and success paths share one definition rather than each listing commands.
    private static bool IsBodyCommand(string name) => name is "BODY" or "ARTICLE";

    private static void IncrementTimeoutCount(string provider)
    {
        TimeoutCounts.AddOrUpdate(provider, 1, (_, existing) => existing + 1);
        MaybeFlushTimeoutCounts();
    }

    private static void MaybeFlushTimeoutCounts()
    {
        var nowTicks = DateTime.UtcNow.Ticks;
        var last = Interlocked.Read(ref _lastTimeoutFlushTicks);
        if (nowTicks - last < TimeSpan.FromSeconds(60).Ticks)
            return;
        if (Interlocked.CompareExchange(ref _lastTimeoutFlushTicks, nowTicks, last) != last)
            return;

        foreach (var key in TimeoutCounts.Keys)
        {
            if (TimeoutCounts.TryRemove(key, out var count) && count > 0)
                Log.Warning("[{ProviderName}] {Count} NNTP timeouts in the last 60 seconds", key, count);
        }
    }

    public int? ConfiguredPipeliningDepth { get; } = pipeliningDepth;
    // null or non-positive = uncapped. Routing reads these to decide whether
    // this provider should be skipped when it has exhausted its block.
    public long? ByteLimit { get; } = byteLimit;
    public long BytesUsedOffset { get; } = bytesUsedOffset;
    /// <summary>
    /// Claims the half-open probe slot as a side effect of being read. Use
    /// <see cref="GetCircuitBreakerSnapshot"/> to inspect state without altering it.
    /// </summary>
    public bool IsTripped => circuitBreaker.IsTripped;
    public ProviderCircuitBreakerSnapshot GetCircuitBreakerSnapshot() => circuitBreaker.GetSnapshot();
    public int MaxConnections => connectionPool.MaxConnections;
    public int LiveConnections => connectionPool.LiveConnections;
    public int IdleConnections => connectionPool.IdleConnections;
    public int ActiveConnections => connectionPool.ActiveConnections;
    public int AvailableConnections => connectionPool.AvailableConnections;
    public int InFlightConnections => ActiveConnections + PendingSelections;
    public ConnectionPoolChurn GetConnectionChurn() => connectionPool.GetChurn();

    /// <summary>
    /// Applies new Streaming Priority odds to this provider's connection gate without
    /// rebuilding the pool.
    /// </summary>
    public void UpdatePriorityOdds(SemaphorePriorityOdds odds) => connectionPool.UpdatePriorityOdds(odds);

    private int _pendingSelections;
    public int PendingSelections => Volatile.Read(ref _pendingSelections);
    public void ReservePending() => Interlocked.Increment(ref _pendingSelections);
    public void ReleasePending() => Interlocked.Decrement(ref _pendingSelections);

    public int UnreservedConnections => Math.Max(0, AvailableConnections - PendingSelections);
    public bool HasSpareConnection => UnreservedConnections > 0;
    /// <summary>
    /// Unreserved capacity as a fraction of the configured pool size. Used by cascade
    /// ranking so unequal MaxConnections do not outweigh configured Priority.
    /// </summary>
    public double SpareFraction =>
        (double)UnreservedConnections / Math.Max(1, MaxConnections);

    public override Task ConnectAsync(string host, int port, bool useSsl, CancellationToken cancellationToken)
    {
        throw new NotSupportedException("Please connect within the connectionFactory");
    }

    public override Task<UsenetResponse> AuthenticateAsync(string user, string pass,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException("Please authenticate within the connectionFactory");
    }

    public override Task<UsenetStatResponse> StatAsync(SegmentId segmentId, CancellationToken ct)
    {
        return RunWithConnection(
            "STAT",
            GetDownloadPriority(ct),
            (connection, _, commandCt) => connection.StatAsync(segmentId, commandCt),
            onConnectionReadyAgain: null,
            ct
        );
    }

    public override Task<UsenetHeadResponse> HeadAsync(SegmentId segmentId, CancellationToken ct)
    {
        return RunWithConnection(
            "HEAD",
            GetDownloadPriority(ct),
            (connection, _, commandCt) => connection.HeadAsync(segmentId, commandCt),
            onConnectionReadyAgain: null,
            ct
        );
    }

    public override Task<UsenetDecodedBodyResponse> DecodedBodyAsync(SegmentId segmentId, CancellationToken ct)
    {
        return RunWithConnection(
            "BODY",
            GetDownloadPriority(ct),
            (connection, onDone, commandCt) => connection.DecodedBodyAsync(segmentId, onDone, commandCt),
            onConnectionReadyAgain: null,
            ct
        );
    }

    public override Task<UsenetDecodedArticleResponse> DecodedArticleAsync
    (
        SegmentId segmentId,
        CancellationToken ct
    )
    {
        return RunWithConnection(
            "ARTICLE",
            GetDownloadPriority(ct),
            (connection, onDone, commandCt) => connection.DecodedArticleAsync(segmentId, onDone, commandCt),
            onConnectionReadyAgain: null,
            ct
        );
    }

    public override Task<UsenetDateResponse> DateAsync(CancellationToken ct)
    {
        return RunWithConnection(
            "DATE",
            SemaphorePriority.Low,
            (connection, _, commandCt) => connection.DateAsync(commandCt),
            onConnectionReadyAgain: null,
            ct
        );
    }

    public override Task<UsenetDecodedBodyResponse> DecodedBodyAsync
    (
        SegmentId segmentId,
        Action<ArticleBodyResult>? onConnectionReadyAgain,
        CancellationToken ct
    )
    {
        return RunWithConnection(
            "BODY",
            GetDownloadPriority(ct),
            (connection, onDone, commandCt) => connection.DecodedBodyAsync(segmentId, onDone, commandCt),
            onConnectionReadyAgain,
            ct
        );
    }

    public override async Task<UsenetDecodedBodyBatch> DecodedBodiesAsync
    (
        IReadOnlyList<SegmentId> segmentIds,
        Action<ArticleBodyResult>? onConnectionReadyAgain,
        CancellationToken ct
    )
    {
        // Streaming reads carry a per-segment deadline so a stalled provider fails over
        // instead of holding a playback stream open for UsenetSharp's ~40s read timeout.
        // It applies to issuing the batch, which is the part that waits on the provider;
        // the response streams are drained by the caller afterwards.
        var workload = DownloadWorkloadClassifier.Classify(ct);
        var operation = NntpOperation.Body;
        var streamingTimeout = ct.GetContext<StreamingTimeoutContext>();
        var retryCount = streamingTimeout?.MaxRetries ?? 1;
        while (true)
        {
            ConnectionLock<INntpClient>? connectionLock = null;
            var deferredCallback = new DeferredArticleBodyCallback();
            CancellationTokenSource? attemptCts = null;
            try
            {
                connectionLock = await AcquireConnectionLockAsync(
                        GetDownloadPriority(ct), workload, operation, ct)
                    .ConfigureAwait(false);

                var batchCt = ct;
                if (streamingTimeout != null)
                {
                    attemptCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    attemptCts.CancelAfter(streamingTimeout.PerSegmentTimeout);
                    batchCt = attemptCts.Token;
                }

                var issuedAt = Stopwatch.GetTimestamp();
                var batch = await connectionLock.Connection.DecodedBodiesAsync(
                    segmentIds, deferredCallback.Invoke, batchCt).ConfigureAwait(false);

                var wrapped = new Task<UsenetDecodedBodyResponse>[batch.Responses.Count];
                for (var i = 0; i < batch.Responses.Count; i++)
                    wrapped[i] = RecordSuccessfulResponseAsync(batch.Responses[i], issuedAt, workload, operation);

                var callbackInvoked = 0;
                deferredCallback.Activate(OnConnectionReadyAgain);
                return new UsenetDecodedBodyBatch { Responses = wrapped };

                void OnConnectionReadyAgain(ArticleBodyResult result)
                {
                    if (Interlocked.Exchange(ref callbackInvoked, 1) != 0) return;
                    switch (result)
                    {
                        case ArticleBodyResult.Retrieved:
                            circuitBreaker.RecordSuccess();
                            break;
                        case ArticleBodyResult.NotFound:
                            circuitBreaker.RecordArticleNotFound();
                            break;
                        case ArticleBodyResult.Cancelled:
                            break;
                        case ArticleBodyResult.NotRetrieved:
                            // Seek/abort cancels mid-pipeline; UsenetSharp reports NotRetrieved
                            // (socket unsafe to reuse). Replace the connection but do not treat
                            // client cancellation as provider health failure.
                            LogException(connectionLock.Replace);
                            if (!ct.IsCancellationRequested)
                                circuitBreaker.RecordFailure($"pipeline-callback-{result}");
                            break;
                        default:
                            circuitBreaker.RecordFailure($"pipeline-callback-{result}");
                            LogException(connectionLock.Replace);
                            break;
                    }

                    LogException(connectionLock.Dispose);
                    LogException(() => onConnectionReadyAgain?.Invoke(result));
                }
            }
            catch (Exception e) when (
                streamingTimeout != null
                && e.IsCancellationException()
                && !ct.IsCancellationRequested)
            {
                // Per-segment deadline fired while the caller is still reading. The
                // connection has an in-flight pipeline → replace it and try again, so a
                // single slow provider does not decide what the stream delivers.
                deferredCallback.Discard();
                LogException(() => connectionLock?.Replace());
                LogException(() => connectionLock?.Dispose());
                if (retryCount > 0)
                {
                    Log.Debug(
                        "Streaming timeout executing pipelined nntp BODY commands after {Timeout}s. Retrying with a new connection ({Retries} left).",
                        streamingTimeout.PerSegmentTimeout.TotalSeconds, retryCount);
                    retryCount--;
                    continue;
                }

                circuitBreaker.RecordFailure("streaming-timeout-pipelined-BODY");
                Log.Warning(
                    "Streaming timeout executing pipelined nntp BODY commands after {Timeout}s. No retries left.",
                    streamingTimeout.PerSegmentTimeout.TotalSeconds);
                LogException(() => onConnectionReadyAgain?.Invoke(ArticleBodyResult.NotRetrieved));
                throw new TimeoutException(
                    "Timeout executing pipelined nntp BODY commands after " +
                    $"{streamingTimeout.MaxRetries + 1} attempts.");
            }
            catch (Exception e) when (e.IsCancellationException(ct))
            {
                deferredCallback.Discard();
                LogException(() => connectionLock?.Replace());
                LogException(() => connectionLock?.Dispose());
                LogException(() => onConnectionReadyAgain?.Invoke(ArticleBodyResult.NotRetrieved));
                throw;
            }
            catch (Exception e) when (e.TryGetCausingException(out UsenetArticleNotFoundException? _))
            {
                // Permanently missing / invalid segment ids are not connection failures.
                deferredCallback.Discard();
                LogException(() => connectionLock?.Dispose());
                LogException(() => onConnectionReadyAgain?.Invoke(ArticleBodyResult.NotRetrieved));
                throw;
            }
            catch (Exception e)
            {
                deferredCallback.Discard();
                var wasReused = connectionLock?.WasReused ?? false;
                if (!wasReused)
                    circuitBreaker.RecordFailure($"pipeline-setup-{e.GetType().Name}");
                LogException(() => connectionLock?.Replace());
                LogException(() => connectionLock?.Dispose());

                // A pooled connection may have been closed server-side while idle;
                // its failure says nothing about provider health. Drain and retry.
                if (wasReused)
                {
                    Log.Debug(e,
                        "Pooled connection for provider {Provider} failed pipelined NNTP BODY commands. Retrying with another connection.",
                        providerName);
                    continue;
                }

                if (retryCount > 0)
                {
                    Log.Debug(e,
                        "Error executing pipelined NNTP BODY commands for provider {Provider}. Retrying with a new connection.",
                        providerName);
                    retryCount--;
                    continue;
                }

                e.LogWarningKnownOrStack(
                    "Error executing pipelined NNTP BODY commands for provider {Provider}.",
                    providerName);
                LogException(() => onConnectionReadyAgain?.Invoke(ArticleBodyResult.NotRetrieved));
                throw;
            }
            finally
            {
                attemptCts?.Dispose();
            }
        }
    }

    public override Task<UsenetDecodedArticleResponse> DecodedArticleAsync
    (
        SegmentId segmentId,
        Action<ArticleBodyResult>? onConnectionReadyAgain,
        CancellationToken ct
    )
    {
        return RunWithConnection(
            "ARTICLE",
            GetDownloadPriority(ct),
            (connection, onDone, commandCt) => connection.DecodedArticleAsync(segmentId, onDone, commandCt),
            onConnectionReadyAgain,
            ct
        );
    }

    private async Task<T> RunWithConnection<T>
    (
        string name,
        SemaphorePriority priority,
        Func<INntpClient, Action<ArticleBodyResult>, CancellationToken, Task<T>> command,
        Action<ArticleBodyResult>? onConnectionReadyAgain,
        CancellationToken ct,
        int retryCount = 1
    ) where T : UsenetResponse
    {
        var workload = DownloadWorkloadClassifier.Classify(ct);
        var operation = LatencyNames.FromCommandName(name);
        var streamingTimeout = ct.GetContext<StreamingTimeoutContext>();
        if (streamingTimeout != null)
            retryCount = streamingTimeout.MaxRetries;

        while (true)
        {
            ConnectionLock<INntpClient>? connectionLock = null;
            try
            {
                connectionLock = await AcquireConnectionLockAsync(priority, workload, operation, ct)
                    .ConfigureAwait(false);
            }
            catch (Exception e) when (e.IsCancellationException(ct))
            {
                LogException(() => connectionLock?.Dispose());
                LogException(() => onConnectionReadyAgain?.Invoke(ArticleBodyResult.NotRetrieved));
                throw;
            }
            catch (Exception e)
            {
                circuitBreaker.RecordFailure($"get-connection-{e.GetType().Name}");
                LogException(() => connectionLock?.Replace());
                LogException(() => connectionLock?.Dispose());
                if (retryCount > 0)
                {
                    Log.Debug(e, "Error getting connection-lock for provider {Provider}. Retrying with a new connection.", providerName);
                    retryCount--;
                    continue;
                }

                e.LogWarningKnownOrStack("Error getting connection-lock for provider {Provider}.", providerName);
                LogException(() => onConnectionReadyAgain?.Invoke(ArticleBodyResult.NotRetrieved));
                throw;
            }

            T? result;
            var deferredCallback = new DeferredArticleBodyCallback();
            CancellationTokenSource? attemptCts = null;
            try
            {
                var commandCt = ct;
                if (streamingTimeout != null)
                {
                    attemptCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    attemptCts.CancelAfter(streamingTimeout.PerSegmentTimeout);
                    commandCt = attemptCts.Token;
                }

                var responseStarted = Stopwatch.GetTimestamp();
                result = await command(connectionLock.Connection, deferredCallback.Invoke, commandCt)
                    .ConfigureAwait(false);
                if (result?.Success ?? false)
                {
                    latencyTracker?.Record(
                        MetricsKey,
                        LatencyPhase.Response,
                        workload,
                        operation,
                        Stopwatch.GetElapsedTime(responseStarted));
                }
            }
            catch (Exception e) when (
                streamingTimeout != null
                && e.IsCancellationException()
                && !ct.IsCancellationRequested)
            {
                // Per-segment CancelAfter fired while the caller is still alive.
                // The connection has an in-flight command → NotRetrieved (replace).
                // Do not invoke onConnectionReadyAgain on retry: the outer download
                // permit stays held across attempts (same pattern as other retries).
                deferredCallback.Discard();
                LogException(() => connectionLock?.Replace());
                LogException(() => connectionLock?.Dispose());
                if (retryCount > 0)
                {
                    Log.Debug(
                        "Streaming timeout executing nntp {Command} command after {Timeout}s. Retrying with a new connection ({Retries} left).",
                        name, streamingTimeout.PerSegmentTimeout.TotalSeconds, retryCount);
                    retryCount--;
                    continue;
                }

                // Exhausted the streaming-timeout retry budget — count toward the
                // breaker once per segment (not per attempt) so chronically-slow
                // providers still trip without over-counting a single segment.
                circuitBreaker.RecordFailure($"streaming-timeout-{name}");
                Log.Warning(
                    "Streaming timeout executing nntp {Command} command after {Timeout}s. No retries left.",
                    name, streamingTimeout.PerSegmentTimeout.TotalSeconds);
                LogException(() => onConnectionReadyAgain?.Invoke(ArticleBodyResult.NotRetrieved));
                throw new TimeoutException(
                    $"Timeout executing nntp {name} command after {streamingTimeout.MaxRetries + 1} attempts.");
            }
            catch (Exception e) when (e.IsCancellationException(ct))
            {
                deferredCallback.Discard();
                LogException(() => connectionLock?.Replace());
                LogException(() => connectionLock?.Dispose());
                LogException(() => onConnectionReadyAgain?.Invoke(ArticleBodyResult.NotRetrieved));
                throw;
            }
            catch (Exception e) when (e.TryGetCausingException(out UsenetArticleNotFoundException? _))
            {
                deferredCallback.Discard();
                LogException(() => connectionLock?.Dispose());
                LogException(() => onConnectionReadyAgain?.Invoke(ArticleBodyResult.NotRetrieved));
                throw;
            }
            catch (Exception e) when (IsBodyCommand(name) && e.TryGetCausingException(out TimeoutException? _))
            {
                // Read-timeout on BODY/ARTICLE means the provider stopped responding
                // mid-command. A fresh socket to the same provider is unlikely to fare
                // any better, and burning another timeout retrying here just doubles
                // the wait before MultiProviderNntpClient can fall over to the next
                // provider. Replace the socket (the read may have left partial bytes
                // on the wire) and propagate so the outer provider loop moves on.
                IncrementTimeoutCount(providerName);
                deferredCallback.Discard();
                circuitBreaker.RecordFailure($"read-timeout-{name}");
                LogException(() => connectionLock?.Replace());
                LogException(() => connectionLock?.Dispose());
                LogException(() => onConnectionReadyAgain?.Invoke(ArticleBodyResult.NotRetrieved));
                throw;
            }
            catch (Exception e)
            {
                deferredCallback.Discard();
                var wasReused = connectionLock?.WasReused ?? false;
                // stat, head, and date do not feed the circuit breaker. The success path
                // already skips them so a failure recorded here would never be reset.
                if (!wasReused && IsBodyCommand(name))
                    circuitBreaker.RecordFailure($"cmd-setup-{name}-{e.GetType().Name}");
                LogException(() => connectionLock?.Replace());
                LogException(() => connectionLock?.Dispose());

                // A pooled connection may have been closed server-side while idle;
                // its failure says nothing about provider health. Drain and retry.
                if (wasReused)
                {
                    Log.Debug(e, "Pooled connection for provider {Provider} failed nntp {Command} command. Retrying with another connection.", providerName, name);
                    continue;
                }

                if (retryCount > 0)
                {
                    Log.Debug(e, "Error executing nntp {Command} command for provider {Provider}. Retrying with a new connection.", name, providerName);
                    retryCount--;
                    continue;
                }

                e.LogWarningKnownOrStack(
                    "Error executing nntp {Command} command for provider {Provider}.", name, providerName);
                LogException(() => onConnectionReadyAgain?.Invoke(ArticleBodyResult.NotRetrieved));
                throw;
            }
            finally
            {
                attemptCts?.Dispose();
            }

            // stat, head, and date — do not feed the circuit breaker.
            // STAT/HEAD/DATE successes were resetting BODY failure streaks and
            // preventing trips under mixed traffic (STAT-ok/BODY-fail providers).
            if (!IsBodyCommand(name))
            {
                // Once latched the breaker is no longer tracking a streak, it is waiting
                // for proof the provider answers at all. A provider that sees little body
                // traffic would otherwise stay latched with nothing able to close it.
                if (circuitBreaker.IsLatched)
                    circuitBreaker.RecordSuccess();
                deferredCallback.Discard();
                LogException(() => connectionLock?.Dispose());
            }

            // body and article
            else if ((result?.Success ?? false) == false)
            {
                circuitBreaker.RecordArticleNotFound();
                deferredCallback.Discard();
                LogException(() => connectionLock?.Dispose());
                LogException(() => onConnectionReadyAgain?.Invoke(ArticleBodyResult.NotRetrieved));
            }
            else
            {
                var callbackInvoked = 0;
                deferredCallback.Activate(articleBodyResult =>
                {
                    if (Interlocked.Exchange(ref callbackInvoked, 1) != 0) return;

                    if (articleBodyResult == ArticleBodyResult.NotRetrieved)
                    {
                        LogException(() => connectionLock?.Replace());
                        // Client abort (seek) must not trip the provider circuit breaker.
                        if (!ct.IsCancellationRequested)
                            circuitBreaker.RecordFailure($"body-callback-{name}-NotRetrieved");
                    }
                    else if (articleBodyResult == ArticleBodyResult.Retrieved)
                    {
                        circuitBreaker.RecordSuccess();
                    }
                    else if (articleBodyResult == ArticleBodyResult.NotFound)
                    {
                        circuitBreaker.RecordArticleNotFound();
                    }

                    LogException(() => connectionLock?.Dispose());
                    LogException(() => onConnectionReadyAgain?.Invoke(articleBodyResult));
                });
            }

            return result!;
        }
    }

    public override IAsyncEnumerable<PipelinedStatResult> StatsPipelinedAsync(
        IReadOnlyList<string> segmentIds, int depth, CancellationToken cancellationToken)
        => RunPipelinedStatAsync(
            c => c.StatsPipelinedAsync(segmentIds, depth, cancellationToken),
            cancellationToken);

    public override IAsyncEnumerable<PipelinedBodyResult> DecodedBodiesPipelinedAsync(
        IReadOnlyList<string> segmentIds, int depth, CancellationToken cancellationToken)
        => RunPipelinedAsync(
            c => c.DecodedBodiesPipelinedAsync(segmentIds, depth, cancellationToken),
            NntpOperation.PipelinedBody,
            cancellationToken);

    public override IAsyncEnumerable<PipelinedArticleResult> DecodedArticlesPipelinedAsync(
        IReadOnlyList<string> segmentIds, int depth, CancellationToken cancellationToken)
        => RunPipelinedAsync(
            c => c.DecodedArticlesPipelinedAsync(segmentIds, depth, cancellationToken),
            NntpOperation.PipelinedArticle,
            cancellationToken);

    /// <summary>
    /// STAT pipeline lease: inherits download priority from the caller's token (High for
    /// playback verification, Low for hosted health), no circuit-breaker updates (matching
    /// single StatAsync). Still replaces the connection on hard failure because UsenetSharp
    /// poisons it mid-batch. Acquisition goes through the shared helper so the pool wait
    /// is instrumented and arbitrated like every other command.
    /// </summary>
    private async IAsyncEnumerable<PipelinedStatResult> RunPipelinedStatAsync(
        Func<INntpClient, IAsyncEnumerable<PipelinedStatResult>> batchFactory,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var workload = DownloadWorkloadClassifier.Classify(cancellationToken);
        var operation = NntpOperation.PipelinedStat;
        var connectionLock = await AcquireConnectionLockAsync(
                GetDownloadPriority(cancellationToken), workload, operation, cancellationToken)
            .ConfigureAwait(false);
        var completed = false;
        try
        {
            await using var enumerator = batchFactory(connectionLock.Connection)
                .GetAsyncEnumerator(cancellationToken);
            while (true)
            {
                PipelinedStatResult current;
                try
                {
                    var moveStarted = Stopwatch.GetTimestamp();
                    if (!await enumerator.MoveNextAsync().ConfigureAwait(false))
                    {
                        completed = true;
                        break;
                    }

                    current = enumerator.Current;
                    latencyTracker?.Record(
                        MetricsKey,
                        LatencyPhase.Response,
                        workload,
                        operation,
                        Stopwatch.GetElapsedTime(moveStarted));
                }
                catch (Exception e) when (!e.IsCancellationException())
                {
                    // Do not RecordFailure — STAT must not feed the breaker — but the
                    // connection is poisoned and must not return to the pool.
                    connectionLock.Replace();
                    throw;
                }

                yield return current;
            }
        }
        finally
        {
            if (!completed) connectionLock.Replace();
            connectionLock.Dispose();
        }
    }

    private async IAsyncEnumerable<T> RunPipelinedAsync<T>(
        Func<INntpClient, IAsyncEnumerable<T>> batchFactory,
        NntpOperation operation,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var workload = DownloadWorkloadClassifier.Classify(cancellationToken);
        var priority = GetDownloadPriority(cancellationToken);
        var connectionLock = await AcquireConnectionLockAsync(
                priority, workload, operation, cancellationToken)
            .ConfigureAwait(false);
        var completed = false;
        try
        {
            await using var enumerator = batchFactory(connectionLock.Connection)
                .GetAsyncEnumerator(cancellationToken);
            while (true)
            {
                T current;
                try
                {
                    var moveStarted = Stopwatch.GetTimestamp();
                    if (!await enumerator.MoveNextAsync().ConfigureAwait(false))
                    {
                        completed = true;
                        break;
                    }

                    current = enumerator.Current;
                    latencyTracker?.Record(
                        MetricsKey,
                        LatencyPhase.Response,
                        workload,
                        operation,
                        Stopwatch.GetElapsedTime(moveStarted));
                }
                catch (Exception e) when (!e.IsCancellationException())
                {
                    circuitBreaker.RecordFailure($"pipelined-enum-{e.GetType().Name}");
                    connectionLock.Replace();
                    throw;
                }

                circuitBreaker.RecordSuccess();
                yield return current;
            }
        }
        finally
        {
            if (!completed) connectionLock.Replace();
            connectionLock.Dispose();
        }
    }

    private static SemaphorePriority GetDownloadPriority(CancellationToken ct)
    {
        return ct.GetContext<DownloadPriorityContext>()?.Priority ?? SemaphorePriority.Low;
    }

    /// <summary>
    /// Borrows a pooled connection, attributing the wait to latency histograms and (when
    /// active) the current stream-trace range so pool saturation is distinguishable from
    /// provider response time.
    /// </summary>
    private async Task<ConnectionLock<INntpClient>> AcquireConnectionLockAsync(
        SemaphorePriority priority,
        DownloadWorkload workload,
        NntpOperation operation,
        CancellationToken ct)
    {
        var traceRange = MultiProviderNntpClient.CurrentStreamTraceRange;
        var started = Stopwatch.GetTimestamp();
        var connectionLock = await connectionPool.GetConnectionLockAsync(priority, ct)
            .ConfigureAwait(false);
        var elapsed = Stopwatch.GetElapsedTime(started);
        latencyTracker?.Record(MetricsKey, LatencyPhase.PoolWait, workload, operation, elapsed);
        StreamTrace.TryConnectionAcquired(traceRange, elapsed, connectionLock.WasReused);
        return connectionLock;
    }

    private async Task<UsenetDecodedBodyResponse> RecordSuccessfulResponseAsync(
        Task<UsenetDecodedBodyResponse> responseTask,
        long issuedAt,
        DownloadWorkload workload,
        NntpOperation operation)
    {
        var response = await responseTask.ConfigureAwait(false);
        if (response.Success)
        {
            latencyTracker?.Record(
                MetricsKey,
                LatencyPhase.Response,
                workload,
                operation,
                Stopwatch.GetElapsedTime(issuedAt));
        }
        return response;
    }

    private static void LogException(Action? action)
    {
        try
        {
            action?.Invoke();
        }
        catch (Exception e)
        {
            Log.Warning(e, "Unhandled exception");
        }
    }

    public override void Dispose()
    {
        connectionPool.Dispose();
        GC.SuppressFinalize(this);
    }
}
