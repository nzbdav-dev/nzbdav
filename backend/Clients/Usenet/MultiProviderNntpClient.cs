using System.Runtime.ExceptionServices;
using NzbWebDAV.Clients.Usenet.Connections;
using NzbWebDAV.Clients.Usenet.Models;
using NzbWebDAV.Exceptions;
using NzbWebDAV.Extensions;
using NzbWebDAV.Models;
using NzbWebDAV.Streams;
using Serilog;
using Usenet.Nntp.Responses;
using Usenet.Nzb;
using Usenet.Yenc;

namespace NzbWebDAV.Clients.Usenet;

public class MultiProviderNntpClient(List<MultiConnectionNntpClient> providers) : INntpClient
{
    public Task<bool> ConnectAsync(string host, int port, bool useSsl, CancellationToken cancellationToken)
    {
        throw new NotSupportedException("Please connect within the connectionFactory");
    }

    public Task<bool> AuthenticateAsync(string user, string pass, CancellationToken cancellationToken)
    {
        throw new NotSupportedException("Please authenticate within the connectionFactory");
    }

    public Task<NntpStatResponse> StatAsync(string segmentId, CancellationToken cancellationToken)
    {
        return RunFromPoolWithBackup(connection => connection.StatAsync(segmentId, cancellationToken),
            cancellationToken);
    }

    public Task<NntpDateResponse> DateAsync(CancellationToken cancellationToken)
    {
        return RunFromPoolWithBackup(connection => connection.DateAsync(cancellationToken), cancellationToken);
    }

    public Task<UsenetArticleHeaders> GetArticleHeadersAsync(string segmentId, CancellationToken cancellationToken)
    {
        return RunFromPoolWithBackup(connection => connection.GetArticleHeadersAsync(segmentId, cancellationToken),
            cancellationToken);
    }

    public Task<YencHeaderStream> GetSegmentStreamAsync(string segmentId, bool includeHeaders,
        CancellationToken cancellationToken)
    {
        return RunFromPoolWithBackup(
            connection => connection.GetSegmentStreamAsync(segmentId, includeHeaders, cancellationToken),
            cancellationToken);
    }

    public Task<YencHeader> GetSegmentYencHeaderAsync(string segmentId, CancellationToken cancellationToken)
    {
        return RunFromPoolWithBackup(connection => connection.GetSegmentYencHeaderAsync(segmentId, cancellationToken),
            cancellationToken);
    }

    public Task<long> GetFileSizeAsync(NzbFile file, CancellationToken cancellationToken)
    {
        return RunFromPoolWithBackup(connection => connection.GetFileSizeAsync(file, cancellationToken),
            cancellationToken);
    }

    public Task WaitForReady(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    private async Task<T> RunFromPoolWithBackup<T>
    (
        Func<INntpClient, Task<T>> task,
        CancellationToken cancellationToken
    )
    {
        ExceptionDispatchInfo? lastException = null;
        var lastSuccessfulProviderContext = cancellationToken.GetContext<LastSuccessfulProviderContext>();
        var lastSuccessfulProvider = lastSuccessfulProviderContext?.Provider;
        var orderedProviders = GetOrderedProviders(lastSuccessfulProvider);
        T? result = default;
        foreach (var provider in orderedProviders)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (lastException is not null && lastException.SourceException is not UsenetArticleNotFoundException)
            {
                var msg = lastException.SourceException.Message;
                Log.Debug($"Encountered error during NNTP Operation: `{msg}`. Trying another provider.");
            }

            try
            {
                result = await task.Invoke(provider).ConfigureAwait(false);
                if (result is NntpStatResponse r && r.ResponseType != NntpStatResponseType.ArticleExists)
                    throw new UsenetArticleNotFoundException(r.MessageId.Value);

                if (lastSuccessfulProviderContext is not null && lastSuccessfulProvider != provider)
                    lastSuccessfulProviderContext.Provider = provider;
                return result;
            }
            catch (Exception e) when (e is not OperationCanceledException and not TaskCanceledException)
            {
                lastException = ExceptionDispatchInfo.Capture(e);
            }
        }

        if (result is NntpStatResponse)
            return result;

        lastException?.Throw();
        throw new Exception("There are no usenet providers configured.");
    }

    private IEnumerable<MultiConnectionNntpClient> GetOrderedProviders(MultiConnectionNntpClient? preferredProvider)
    {
        return providers
            .Where(x => x.ProviderType != ProviderType.Disabled)
            .OrderBy(x => x.ProviderType)
            .ThenByDescending(x => x.IdleConnections)
            .ThenByDescending(x => x.RemainingSemaphoreSlots)
            .Prepend(preferredProvider)
            .Where(x => x is not null)
            .Select(x => x!)
            .Distinct();
    }

    public void Dispose()
    {
        foreach (var provider in providers)
            provider.Dispose();
        GC.SuppressFinalize(this);
    }
}