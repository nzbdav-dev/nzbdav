using NzbWebDAV.Clients.Usenet.Concurrency;
using NzbWebDAV.Clients.Usenet.Contexts;
using NzbWebDAV.Clients.Usenet.Models;
using NzbWebDAV.Config;
using NzbWebDAV.Extensions;
using UsenetSharp.Models;

namespace NzbWebDAV.Clients.Usenet;

/// <summary>
/// This client is only responsible for limiting download operations (BODY/ARTICLE)
/// to the configured number of maximum download connections.
/// </summary>
/// <param name="usenetClient"></param>
public class DownloadingNntpClient : WrappingNntpClient
{
    private readonly ConfigManager _configManager;
    private readonly PrioritizedSemaphore _semaphore;

    public DownloadingNntpClient(INntpClient usenetClient, ConfigManager configManager) : base(usenetClient)
    {
        var maxDownloadConnections = configManager.GetMaxDownloadConnections();
        var streamingPriority = configManager.GetStreamingPriority();
        _configManager = configManager;
        _semaphore = new PrioritizedSemaphore(maxDownloadConnections, maxDownloadConnections, streamingPriority);
        configManager.OnConfigChanged += OnConfigChanged;
    }

    private void OnConfigChanged(object? sender, ConfigManager.ConfigEventArgs e)
    {
        if (e.ChangedConfig.ContainsKey("usenet.max-download-connections"))
        {
            var maxDownloadConnections = _configManager.GetMaxDownloadConnections();
            _semaphore.UpdateMaxAllowed(maxDownloadConnections);
        }

        if (e.ChangedConfig.ContainsKey("usenet.streaming-priority"))
        {
            var streamingPriority = _configManager.GetStreamingPriority();
            _semaphore.UpdatePriorityOdds(streamingPriority);
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
        await AcquireExclusiveConnectionAsync(onConnectionReadyAgain, cancellationToken).ConfigureAwait(false);
        return await base.DecodedBodyAsync(segmentId, OnConnectionReadyAgain, cancellationToken).ConfigureAwait(false);

        void OnConnectionReadyAgain(ArticleBodyResult articleBodyResult)
        {
            _semaphore.Release();
            onConnectionReadyAgain?.Invoke(articleBodyResult);
        }
    }

    public override async Task<UsenetDecodedArticleResponse> DecodedArticleAsync(SegmentId segmentId,
        Action<ArticleBodyResult>? onConnectionReadyAgain, CancellationToken cancellationToken)
    {
        await AcquireExclusiveConnectionAsync(onConnectionReadyAgain, cancellationToken).ConfigureAwait(false);
        return await base.DecodedArticleAsync(segmentId, OnConnectionReadyAgain, cancellationToken)
            .ConfigureAwait(false);

        void OnConnectionReadyAgain(ArticleBodyResult articleBodyResult)
        {
            _semaphore.Release();
            onConnectionReadyAgain?.Invoke(articleBodyResult);
        }
    }

    private async Task AcquireExclusiveConnectionAsync(Action<ArticleBodyResult>? onConnectionReadyAgain,
        CancellationToken cancellationToken)
    {
        try
        {
            await AcquireExclusiveConnectionAsync(cancellationToken);
        }
        catch
        {
            onConnectionReadyAgain?.Invoke(ArticleBodyResult.NotRetrieved);
            throw;
        }
    }

    private Task AcquireExclusiveConnectionAsync(CancellationToken cancellationToken)
    {
        var downloadPriorityContext = cancellationToken.GetContext<DownloadPriorityContext>();
        var semaphorePriority = downloadPriorityContext == DownloadPriorityContext.Low
            ? SemaphorePriority.Low
            : SemaphorePriority.High;
        return _semaphore.WaitAsync(semaphorePriority, cancellationToken);
    }

    public override async Task<UsenetExclusiveConnection> AcquireExclusiveConnectionAsync
    (
        string segmentId,
        CancellationToken cancellationToken
    )
    {
        await AcquireExclusiveConnectionAsync(cancellationToken).ConfigureAwait(false);
        return new UsenetExclusiveConnection(_ => _semaphore.Release());
    }

    public override Task<UsenetDecodedBodyResponse> DecodedBodyAsync(SegmentId segmentId,
        UsenetExclusiveConnection exclusiveConnection, CancellationToken cancellationToken)
    {
        var onConnectionReadyAgain = exclusiveConnection.OnConnectionReadyAgain;
        return base.DecodedBodyAsync(segmentId, onConnectionReadyAgain, cancellationToken);
    }

    public override Task<UsenetDecodedArticleResponse> DecodedArticleAsync(SegmentId segmentId,
        UsenetExclusiveConnection exclusiveConnection, CancellationToken cancellationToken)
    {
        var onConnectionReadyAgain = exclusiveConnection.OnConnectionReadyAgain;
        return base.DecodedArticleAsync(segmentId, onConnectionReadyAgain, cancellationToken);
    }

    public override void Dispose()
    {
        _configManager.OnConfigChanged -= OnConfigChanged;
        base.Dispose();
        GC.SuppressFinalize(this);
    }
}