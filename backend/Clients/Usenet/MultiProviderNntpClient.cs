using System.Runtime.ExceptionServices;
using NzbWebDAV.Clients.Usenet.Connections;
using NzbWebDAV.Clients.Usenet.Models;
using NzbWebDAV.Extensions;
using NzbWebDAV.Models;
using Serilog;
using UsenetSharp.Models;

namespace NzbWebDAV.Clients.Usenet;

public class MultiProviderNntpClient(List<MultiConnectionNntpClient> providers) : NntpClient
{
    private readonly ProviderCircuitBreaker _circuitBreaker = new();
    public override Task ConnectAsync(string host, int port, bool useSsl, CancellationToken ct)
    {
        throw new NotSupportedException("Please connect within the connectionFactory");
    }

    public override Task<UsenetResponse> AuthenticateAsync(string user, string pass, CancellationToken ct)
    {
        throw new NotSupportedException("Please authenticate within the connectionFactory");
    }

    public override Task<UsenetStatResponse> StatAsync(SegmentId segmentId, CancellationToken cancellationToken)
    {
        return RunFromPoolWithBackup(x => x.StatAsync(segmentId, cancellationToken), cancellationToken);
    }

    public override Task<UsenetHeadResponse> HeadAsync(SegmentId segmentId, CancellationToken cancellationToken)
    {
        return RunFromPoolWithBackup(x => x.HeadAsync(segmentId, cancellationToken), cancellationToken);
    }

    public override Task<UsenetDecodedBodyResponse> DecodedBodyAsync
    (
        SegmentId segmentId,
        CancellationToken cancellationToken
    )
    {
        return RunFromPoolWithBackup(x => x.DecodedBodyAsync(segmentId, cancellationToken), cancellationToken);
    }

    public override Task<UsenetDecodedArticleResponse> DecodedArticleAsync
    (
        SegmentId segmentId,
        CancellationToken cancellationToken
    )
    {
        return RunFromPoolWithBackup(x => x.DecodedArticleAsync(segmentId, cancellationToken), cancellationToken);
    }

    public override Task<UsenetDateResponse> DateAsync(CancellationToken cancellationToken)
    {
        return RunFromPoolWithBackup(x => x.DateAsync(cancellationToken), cancellationToken);
    }

    public override async Task<UsenetDecodedBodyResponse> DecodedBodyAsync
    (
        SegmentId segmentId,
        Action<ArticleBodyResult>? onConnectionReadyAgain,
        CancellationToken cancellationToken
    )
    {
        UsenetDecodedBodyResponse? result;
        try
        {
            result = await RunFromPoolWithBackup(
                x => x.DecodedBodyAsync(segmentId, OnConnectionReadyAgain, cancellationToken),
                cancellationToken
            ).ConfigureAwait(false);
        }
        catch
        {
            onConnectionReadyAgain?.Invoke(ArticleBodyResult.NotRetrieved);
            throw;
        }

        if (result.ResponseType != UsenetResponseType.ArticleRetrievedBodyFollows)
            onConnectionReadyAgain?.Invoke(ArticleBodyResult.NotRetrieved);

        return result;

        void OnConnectionReadyAgain(ArticleBodyResult articleBodyResult)
        {
            if (articleBodyResult == ArticleBodyResult.Retrieved)
                onConnectionReadyAgain?.Invoke(ArticleBodyResult.Retrieved);
        }
    }

    public override async Task<UsenetDecodedArticleResponse> DecodedArticleAsync
    (
        SegmentId segmentId,
        Action<ArticleBodyResult>? onConnectionReadyAgain,
        CancellationToken cancellationToken
    )
    {
        UsenetDecodedArticleResponse? result;
        try
        {
            result = await RunFromPoolWithBackup(
                x => x.DecodedArticleAsync(segmentId, OnConnectionReadyAgain, cancellationToken),
                cancellationToken
            ).ConfigureAwait(false);
        }
        catch
        {
            onConnectionReadyAgain?.Invoke(ArticleBodyResult.NotRetrieved);
            throw;
        }

        if (result.ResponseType != UsenetResponseType.ArticleRetrievedHeadAndBodyFollow)
            onConnectionReadyAgain?.Invoke(ArticleBodyResult.NotRetrieved);

        return result;

        void OnConnectionReadyAgain(ArticleBodyResult articleBodyResult)
        {
            if (articleBodyResult == ArticleBodyResult.Retrieved)
                onConnectionReadyAgain?.Invoke(ArticleBodyResult.Retrieved);
        }
    }

    private async Task<T> RunFromPoolWithBackup<T>
    (
        Func<INntpClient, Task<T>> task,
        CancellationToken cancellationToken
    ) where T : UsenetResponse
    {
        ExceptionDispatchInfo? lastException = null;
        var orderedProviders = GetOrderedProviders();

        // First pass: skip circuit-broken providers
        // Second pass (fallback): include circuit-broken providers so we never give up entirely
        for (var pass = 0; pass < 2; pass++)
        {
            for (var i = 0; i < orderedProviders.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var (provider, providerIndex) = orderedProviders[i];
                var isLastProviderInLastPass = pass == 1 && i == orderedProviders.Count - 1;

                // First pass: skip providers with open circuits
                if (pass == 0 && _circuitBreaker.IsOpen(providerIndex))
                {
                    Log.Debug("Skipping provider {ProviderIndex} (circuit breaker open).", providerIndex);
                    continue;
                }

                if (lastException is not null)
                {
                    var msg = lastException.SourceException.Message;
                    Log.Debug($"Encountered error during NNTP Operation: `{msg}`. Trying another provider.");
                }

                try
                {
                    var result = await task.Invoke(provider).ConfigureAwait(false);

                    _circuitBreaker.RecordSuccess(providerIndex);

                    // if no article with that message-id is found, try again with the next provider.
                    if (!isLastProviderInLastPass &&
                        result.ResponseType == UsenetResponseType.NoArticleWithThatMessageId)
                        continue;

                    return result;
                }
                catch (Exception e) when (!e.IsCancellationException())
                {
                    _circuitBreaker.RecordFailure(providerIndex);
                    lastException = ExceptionDispatchInfo.Capture(e);
                }
            }

            if (pass == 0)
            {
                // Check if any providers were skipped — if not, no point in second pass
                var anySkipped = orderedProviders.Any(p => _circuitBreaker.IsOpen(p.ProviderIndex));
                if (!anySkipped)
                    break;
            }
        }

        lastException?.Throw();
        throw new Exception("There are no usenet providers configured.");
    }

    private List<(MultiConnectionNntpClient Provider, int ProviderIndex)> GetOrderedProviders()
    {
        return providers
            .Select((provider, index) => (Provider: provider, ProviderIndex: index))
            .Where(x => x.Provider.ProviderType != ProviderType.Disabled)
            .OrderBy(x => x.Provider.ProviderType)
            .ThenByDescending(x => x.Provider.AvailableConnections)
            .ToList();
    }

    public override void Dispose()
    {
        foreach (var provider in providers)
            provider.Dispose();
        GC.SuppressFinalize(this);
    }
}