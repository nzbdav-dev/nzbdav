using NzbWebDAV.Clients.Usenet.Models;
using NzbWebDAV.Exceptions;
using NzbWebDAV.Models.Nzb;
using UsenetSharp.Models;

namespace NzbWebDAV.Clients.Usenet;

public static class NntpClientSegmentFallbackExtensions
{
    public static Task<UsenetHeadResponse> HeadWithFallbackAsync(
        this INntpClient usenetClient,
        string encodedSegmentId,
        CancellationToken cancellationToken
    )
    {
        return WithFallbackAsync(
            encodedSegmentId,
            (segmentId, ct) => usenetClient.HeadAsync(segmentId, ct),
            cancellationToken
        );
    }

    public static Task<UsenetDecodedArticleResponse> DecodedArticleWithFallbackAsync(
        this INntpClient usenetClient,
        string encodedSegmentId,
        CancellationToken cancellationToken
    )
    {
        return WithFallbackAsync(
            encodedSegmentId,
            (segmentId, ct) => usenetClient.DecodedArticleAsync(segmentId, ct),
            cancellationToken
        );
    }

    public static Task<UsenetDecodedBodyResponse> DecodedBodyWithFallbackAsync(
        this INntpClient usenetClient,
        string encodedSegmentId,
        CancellationToken cancellationToken
    )
    {
        return WithFallbackAsync(
            encodedSegmentId,
            (segmentId, ct) => usenetClient.DecodedBodyAsync(segmentId, ct),
            cancellationToken
        );
    }

    public static async Task<UsenetDecodedBodyResponse> DecodedBodyWithFallbackAsync(
        this INntpClient usenetClient,
        string encodedSegmentId,
        CancellationToken cancellationToken,
        Func<string, CancellationToken, Task<UsenetExclusiveConnection>> acquireExclusiveConnectionAsync
    )
    {
        UsenetArticleNotFoundException? missingArticleException = null;
        foreach (var segmentId in NzbSegmentIdSet.Decode(encodedSegmentId))
        {
            try
            {
                var exclusiveConnection = await acquireExclusiveConnectionAsync(segmentId, cancellationToken)
                    .ConfigureAwait(false);
                return await usenetClient
                    .DecodedBodyAsync(segmentId, exclusiveConnection, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (UsenetArticleNotFoundException e)
            {
                missingArticleException = e;
            }
        }

        throw missingArticleException ?? new UsenetArticleNotFoundException(encodedSegmentId);
    }

    public static async Task<T> WithFallbackAsync<T>(
        string encodedSegmentId,
        Func<string, CancellationToken, Task<T>> action,
        CancellationToken cancellationToken
    )
    {
        UsenetArticleNotFoundException? missingArticleException = null;
        foreach (var segmentId in NzbSegmentIdSet.Decode(encodedSegmentId))
        {
            try
            {
                return await action(segmentId, cancellationToken).ConfigureAwait(false);
            }
            catch (UsenetArticleNotFoundException e)
            {
                missingArticleException = e;
            }
        }

        throw missingArticleException ?? new UsenetArticleNotFoundException(encodedSegmentId);
    }
}
