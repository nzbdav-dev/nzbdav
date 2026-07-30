using System.Diagnostics;
using NzbWebDAV.Clients.Usenet.Concurrency;
using NzbWebDAV.Clients.Usenet.Contexts;
using NzbWebDAV.Clients.Usenet.Models;
using NzbWebDAV.Config;
using NzbWebDAV.Extensions;
using NzbWebDAV.Services.Metrics;
using UsenetSharp.Models;

namespace NzbWebDAV.Clients.Usenet;

/// <summary>
/// This client limits download operations (BODY/ARTICLE) and individual STAT/HEAD
/// requests to the configured maximum download/queue connections via prioritized
/// semaphores.
/// </summary>
/// <param name="usenetClient"></param>
public class DownloadingNntpClient : WrappingNntpClient
{
    private readonly ConfigManager _configManager;
    private readonly PrioritizedSemaphore _streamingSemaphore;
    private readonly PrioritizedSemaphore _queueSemaphore;
    private readonly ProviderLatencyTracker? _latencyTracker;

    public DownloadingNntpClient(
        INntpClient usenetClient,
        ConfigManager configManager,
        ProviderLatencyTracker? latencyTracker = null) : base(usenetClient)
    {
        var maxDownloadConnections = configManager.GetMaxDownloadConnections();
        var maxQueueConnections = configManager.GetMaxQueueConnections();
        var streamingPriority = configManager.GetStreamingPriority();
        _configManager = configManager;
        _latencyTracker = latencyTracker;
        _streamingSemaphore = new PrioritizedSemaphore(maxDownloadConnections, maxDownloadConnections, streamingPriority);
        _queueSemaphore = new PrioritizedSemaphore(
            maxQueueConnections,
            maxQueueConnections,
            new SemaphorePriorityOdds { HighPriorityOdds = 80 });
        configManager.OnConfigChanged += OnConfigChanged;
    }

    public override int PipeliningDepth =>
        _configManager.IsPipeliningEnabled() ? _configManager.GetPipeliningDepth() : 0;

    private void OnConfigChanged(object? sender, ConfigManager.ConfigEventArgs e)
    {
        if (e.ChangedConfig.ContainsKey(ConfigKeys.UsenetMaxDownloadConnections)
            || e.ChangedConfig.ContainsKey(ConfigKeys.UsenetProviders))
            _streamingSemaphore.UpdateMaxAllowed(_configManager.GetMaxDownloadConnections());

        if (e.ChangedConfig.ContainsKey(ConfigKeys.UsenetMaxQueueConnections)
            || e.ChangedConfig.ContainsKey(ConfigKeys.UsenetMaxQueueConnectionsPreset)
            || e.ChangedConfig.ContainsKey(ConfigKeys.UsenetMaxDownloadConnections)
            || e.ChangedConfig.ContainsKey(ConfigKeys.UsenetProviders))
            _queueSemaphore.UpdateMaxAllowed(_configManager.GetMaxQueueConnections());

        if (e.ChangedConfig.ContainsKey(ConfigKeys.UsenetStreamingPriority))
            _streamingSemaphore.UpdatePriorityOdds(_configManager.GetStreamingPriority());
    }

    public override async Task<UsenetStatResponse> StatAsync(SegmentId segmentId, CancellationToken cancellationToken)
    {
        // STAT is request/response — release in finally (unlike BODY, whose permit
        // is released by the streaming completion callback).
        var semaphore = await AcquireExclusiveConnectionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await base.StatAsync(segmentId, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            semaphore.Release();
        }
    }

    public override async Task<UsenetHeadResponse> HeadAsync(SegmentId segmentId, CancellationToken cancellationToken)
    {
        var semaphore = await AcquireExclusiveConnectionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await base.HeadAsync(segmentId, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            semaphore.Release();
        }
    }

    public override Task<UsenetDecodedBodyResponse> DecodedBodyAsync(SegmentId segmentId,
        CancellationToken cancellationToken)
    {
        return DecodedBodyAsync(segmentId, onConnectionReadyAgain: null, cancellationToken);
    }

    public override Task<UsenetDecodedArticleResponse> DecodedArticleAsync(SegmentId segmentId,
        CancellationToken cancellationToken)
    {
        return DecodedArticleAsync(segmentId, onConnectionReadyAgain: null, cancellationToken);
    }

    public override async Task<UsenetDecodedBodyResponse> DecodedBodyAsync(SegmentId segmentId,
        Action<ArticleBodyResult>? onConnectionReadyAgain, CancellationToken cancellationToken)
    {
        var semaphore = await AcquireExclusiveConnectionAsync(onConnectionReadyAgain, cancellationToken).ConfigureAwait(false);
        return await base.DecodedBodyAsync(segmentId, OnConnectionReadyAgain, cancellationToken).ConfigureAwait(false);

        void OnConnectionReadyAgain(ArticleBodyResult articleBodyResult)
        {
            semaphore.Release();
            onConnectionReadyAgain?.Invoke(articleBodyResult);
        }
    }

    public override async Task<UsenetDecodedBodyBatch> DecodedBodiesAsync(
        IReadOnlyList<SegmentId> segmentIds,
        Action<ArticleBodyResult>? onConnectionReadyAgain,
        CancellationToken cancellationToken)
    {
        var semaphore = await AcquireExclusiveConnectionAsync(onConnectionReadyAgain, cancellationToken).ConfigureAwait(false);
        return await base.DecodedBodiesAsync(
            segmentIds, OnConnectionReadyAgain, cancellationToken).ConfigureAwait(false);

        void OnConnectionReadyAgain(ArticleBodyResult articleBodyResult)
        {
            semaphore.Release();
            onConnectionReadyAgain?.Invoke(articleBodyResult);
        }
    }

    public override async Task<UsenetDecodedArticleResponse> DecodedArticleAsync(SegmentId segmentId,
        Action<ArticleBodyResult>? onConnectionReadyAgain, CancellationToken cancellationToken)
    {
        var semaphore = await AcquireExclusiveConnectionAsync(onConnectionReadyAgain, cancellationToken).ConfigureAwait(false);
        return await base.DecodedArticleAsync(segmentId, OnConnectionReadyAgain, cancellationToken)
            .ConfigureAwait(false);

        void OnConnectionReadyAgain(ArticleBodyResult articleBodyResult)
        {
            semaphore.Release();
            onConnectionReadyAgain?.Invoke(articleBodyResult);
        }
    }

    private async Task<PrioritizedSemaphore> AcquireExclusiveConnectionAsync(Action<ArticleBodyResult>? onConnectionReadyAgain,
        CancellationToken cancellationToken)
    {
        try
        {
            return await AcquireExclusiveConnectionAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            onConnectionReadyAgain?.Invoke(ArticleBodyResult.NotRetrieved);
            throw;
        }
    }

    // Picks the semaphore (and its priority) a download should acquire from.
    // QueueDownloadContext always uses the shared queue semaphore: primary workers
    // take the High lane (preferred admission), secondaries take Low. Streaming
    // (DownloadPriorityContext High) uses the streaming semaphore. Default /
    // background health STAT work uses the queue semaphore Low lane.
    private (PrioritizedSemaphore Semaphore, SemaphorePriority Priority) SelectSemaphore(CancellationToken cancellationToken)
    {
        var queueContext = cancellationToken.GetContext<QueueDownloadContext>();
        if (queueContext is not null)
        {
            var queuePriority = queueContext.IsPrimary
                ? SemaphorePriority.High
                : SemaphorePriority.Low;
            return (_queueSemaphore, queuePriority);
        }

        var context = cancellationToken.GetContext<DownloadPriorityContext>();
        var priority = context?.Priority ?? SemaphorePriority.Low;
        var semaphore = priority == SemaphorePriority.High
            ? context?.StreamSemaphore ?? _streamingSemaphore
            : _queueSemaphore;
        return (semaphore, priority);
    }

    private async Task WaitForSemaphoreAsync(
        PrioritizedSemaphore semaphore,
        SemaphorePriority priority,
        CancellationToken cancellationToken)
    {
        var workload = DownloadWorkloadClassifier.Classify(cancellationToken);
        var waitTimer = Stopwatch.StartNew();
        await semaphore.WaitAsync(priority, cancellationToken).ConfigureAwait(false);
        var elapsed = waitTimer.Elapsed;
        _latencyTracker?.Record(
            providerKey: null,
            LatencyPhase.PermitWait,
            workload,
            NntpOperation.Admission,
            elapsed);

        var queueContext = cancellationToken.GetContext<QueueDownloadContext>();
        if (queueContext is not null)
            queueContext.RecordSemaphoreWait(elapsed.TotalMilliseconds > 0
                ? (long)elapsed.TotalMilliseconds
                : 0);
    }

    private async Task<PrioritizedSemaphore> AcquireExclusiveConnectionAsync(CancellationToken cancellationToken)
    {
        var (semaphore, priority) = SelectSemaphore(cancellationToken);
        await WaitForSemaphoreAsync(semaphore, priority, cancellationToken).ConfigureAwait(false);
        return semaphore;
    }

    public override async Task<UsenetExclusiveConnection> AcquireExclusiveConnectionAsync
    (
        string segmentId,
        CancellationToken cancellationToken
    )
    {
        var semaphore = await AcquireExclusiveConnectionAsync(cancellationToken).ConfigureAwait(false);
        return new UsenetExclusiveConnection(_ => semaphore.Release());
    }

    public override async Task<UsenetExclusiveConnection> AcquireExclusiveConnectionAsync
    (
        IReadOnlyList<SegmentId> segmentIds,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(segmentIds);
        if (segmentIds.Count == 0)
        {
            throw new ArgumentException("At least one segment ID is required.", nameof(segmentIds));
        }

        var semaphore = await AcquireExclusiveConnectionAsync(cancellationToken).ConfigureAwait(false);
        return new UsenetExclusiveConnection(_ => semaphore.Release());
    }

    public override Task<UsenetDecodedBodyResponse> DecodedBodyAsync(SegmentId segmentId,
        UsenetExclusiveConnection exclusiveConnection, CancellationToken cancellationToken)
    {
        var onConnectionReadyAgain = exclusiveConnection.OnConnectionReadyAgain;
        return base.DecodedBodyAsync(segmentId, onConnectionReadyAgain, cancellationToken);
    }

    public override Task<UsenetDecodedBodyBatch> DecodedBodiesAsync(
        IReadOnlyList<SegmentId> segmentIds,
        UsenetExclusiveConnection exclusiveConnection,
        CancellationToken cancellationToken)
    {
        return base.DecodedBodiesAsync(
            segmentIds, exclusiveConnection.OnConnectionReadyAgain, cancellationToken);
    }

    public override Task<UsenetDecodedArticleResponse> DecodedArticleAsync(SegmentId segmentId,
        UsenetExclusiveConnection exclusiveConnection, CancellationToken cancellationToken)
    {
        var onConnectionReadyAgain = exclusiveConnection.OnConnectionReadyAgain;
        return base.DecodedArticleAsync(segmentId, onConnectionReadyAgain, cancellationToken);
    }

    public override async IAsyncEnumerable<PipelinedStatResult> StatsPipelinedAsync(
        IReadOnlyList<string> segmentIds, int depth,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var (semaphore, priority) = SelectSemaphore(cancellationToken);
        await WaitForSemaphoreAsync(semaphore, priority, cancellationToken).ConfigureAwait(false);
        try
        {
            await foreach (var result in base.StatsPipelinedAsync(segmentIds, depth, cancellationToken)
                               .WithCancellation(cancellationToken).ConfigureAwait(false))
                yield return result;
        }
        finally
        {
            semaphore.Release();
        }
    }

    public override async IAsyncEnumerable<PipelinedBodyResult> DecodedBodiesPipelinedAsync(
        IReadOnlyList<string> segmentIds, int depth,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var (semaphore, priority) = SelectSemaphore(cancellationToken);
        await WaitForSemaphoreAsync(semaphore, priority, cancellationToken).ConfigureAwait(false);
        try
        {
            await foreach (var result in base.DecodedBodiesPipelinedAsync(segmentIds, depth, cancellationToken)
                               .WithCancellation(cancellationToken).ConfigureAwait(false))
                yield return result;
        }
        finally
        {
            semaphore.Release();
        }
    }

    public override async IAsyncEnumerable<PipelinedArticleResult> DecodedArticlesPipelinedAsync(
        IReadOnlyList<string> segmentIds, int depth,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var (semaphore, priority) = SelectSemaphore(cancellationToken);
        await WaitForSemaphoreAsync(semaphore, priority, cancellationToken).ConfigureAwait(false);
        try
        {
            await foreach (var result in base.DecodedArticlesPipelinedAsync(segmentIds, depth, cancellationToken)
                               .WithCancellation(cancellationToken).ConfigureAwait(false))
                yield return result;
        }
        finally
        {
            semaphore.Release();
        }
    }

    public override void Dispose()
    {
        _configManager.OnConfigChanged -= OnConfigChanged;
        base.Dispose();
        GC.SuppressFinalize(this);
    }
}
