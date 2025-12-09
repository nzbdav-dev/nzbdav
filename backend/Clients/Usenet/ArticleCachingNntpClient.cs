using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using NzbWebDAV.Clients.Usenet.Models;
using NzbWebDAV.Streams;
using UsenetSharp.Models;
using UsenetSharp.Streams;

namespace NzbWebDAV.Clients.Usenet;

/// <summary>
/// This client is responsible for caching Article/Body commands to disk.
/// It is intended to be short-lived. It will delete all cached articles on disposal.
/// </summary>
/// <param name="usenetClient">The underlying client to cache.</param>
/// <param name="leaveOpen">Indicates whether disposing this client also disposes the underlying client.</param>
public class ArticleCachingNntpClient(
    INntpClient usenetClient,
    bool leaveOpen = true
) : WrappingNntpClient(usenetClient)
{
    private readonly string _cacheDir = Directory.CreateTempSubdirectory().FullName;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _pendingRequests = new();
    private readonly ConcurrentDictionary<string, CacheEntry> _cachedSegments = new();

    private record CacheEntry(
        UsenetYencHeader YencHeaders,
        bool HasArticleHeaders,
        UsenetArticleHeader? ArticleHeaders);

    public override Task<UsenetDecodedBodyResponse> DecodedBodyAsync(
        SegmentId segmentId, Action<ArticleBodyResult>? onConnectionReadyAgain, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    public override Task<UsenetDecodedArticleResponse> DecodedArticleAsync(
        SegmentId segmentId, Action<ArticleBodyResult>? onConnectionReadyAgain, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    public override async Task<UsenetDecodedBodyResponse> DecodedBodyAsync(
        SegmentId segmentId, CancellationToken cancellationToken)
    {
        var semaphore = _pendingRequests.GetOrAdd(segmentId, _ => new SemaphoreSlim(1, 1));

        await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Check if already cached
            if (_cachedSegments.TryGetValue(segmentId, out var existingEntry))
            {
                return ReadCachedBodyAsync(segmentId, existingEntry.YencHeaders);
            }

            // Fetch and cache the body
            var response = await base.DecodedBodyAsync(segmentId, cancellationToken)
                .ConfigureAwait(false);

            // Get the decoded stream
            await using var stream = response.Stream;

            // Get yenc headers before caching the decoded stream
            var yencHeaders = await stream.GetYencHeadersAsync(cancellationToken).ConfigureAwait(false);
            if (yencHeaders == null)
            {
                throw new InvalidOperationException($"Failed to read yenc headers for segment {segmentId}");
            }

            await CacheDecodedStreamAsync(segmentId, stream, cancellationToken).ConfigureAwait(false);

            // Mark as cached (body only, no article headers yet)
            _cachedSegments.TryAdd(segmentId, new CacheEntry(
                YencHeaders: yencHeaders,
                HasArticleHeaders: false,
                ArticleHeaders: null));

            // Return a new stream from the cached file
            return ReadCachedBodyAsync(segmentId, yencHeaders);
        }
        finally
        {
            semaphore.Release();
        }
    }

    public override async Task<UsenetDecodedArticleResponse> DecodedArticleAsync(
        SegmentId segmentId, CancellationToken cancellationToken)
    {
        var semaphore = _pendingRequests.GetOrAdd(segmentId, _ => new SemaphoreSlim(1, 1));

        await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Check if already cached with headers
            if (_cachedSegments.TryGetValue(segmentId, out var cacheEntry))
            {
                if (cacheEntry.HasArticleHeaders)
                {
                    // Full article is cached, read from cache
                    return ReadCachedArticleAsync(segmentId, cacheEntry.YencHeaders, cacheEntry.ArticleHeaders!);
                }
                else
                {
                    // Only body is cached, fetch article headers separately
                    var headResponse = await base.HeadAsync(segmentId, cancellationToken).ConfigureAwait(false);

                    // Update cache entry to include article headers
                    var updatedEntry = new CacheEntry(
                        YencHeaders: cacheEntry.YencHeaders,
                        HasArticleHeaders: true,
                        ArticleHeaders: headResponse.ArticleHeaders);

                    _cachedSegments.TryUpdate(segmentId, updatedEntry, cacheEntry);

                    return ReadCachedArticleAsync(segmentId, cacheEntry.YencHeaders, headResponse.ArticleHeaders!);
                }
            }

            // Fetch and cache the full article
            var response = await base.DecodedArticleAsync(segmentId, cancellationToken)
                .ConfigureAwait(false);

            // Get the decoded stream
            await using var stream = response.Stream;

            // Get yenc headers before caching the decoded stream
            var yencHeaders = await stream.GetYencHeadersAsync(cancellationToken).ConfigureAwait(false);
            if (yencHeaders == null)
            {
                throw new InvalidOperationException($"Failed to read yenc headers for segment {segmentId}");
            }

            await CacheDecodedStreamAsync(segmentId, stream, cancellationToken).ConfigureAwait(false);

            // Mark as cached with both yenc and article headers
            _cachedSegments.TryAdd(segmentId, new CacheEntry(
                YencHeaders: yencHeaders,
                HasArticleHeaders: true,
                ArticleHeaders: response.ArticleHeaders));

            // Return a new stream from the cached file
            return ReadCachedArticleAsync(segmentId, yencHeaders, response.ArticleHeaders);
        }
        finally
        {
            semaphore.Release();
        }
    }

    public virtual Task<UsenetExclusiveConnection> AcquireExclusiveConnectionAsync
    (
        string segmentId,
        CancellationToken cancellationToken
    )
    {
        throw new NotImplementedException();
    }

    public virtual Task<UsenetDecodedBodyResponse> DecodedBodyAsync
    (
        SegmentId segmentId,
        UsenetExclusiveConnection exclusiveConnection,
        CancellationToken cancellationToken
    )
    {
        throw new NotImplementedException();
    }

    public virtual Task<UsenetDecodedArticleResponse> DecodedArticleAsync
    (
        SegmentId segmentId,
        UsenetExclusiveConnection exclusiveConnection,
        CancellationToken cancellationToken
    )
    {
        throw new NotImplementedException();
    }

    public override Task<UsenetYencHeader> GetYencHeadersAsync(string segmentId, CancellationToken ct)
    {
        return _cachedSegments.TryGetValue(segmentId, out var existingEntry)
            ? Task.FromResult(existingEntry.YencHeaders)
            : base.GetYencHeadersAsync(segmentId, ct);
    }

    private async Task CacheDecodedStreamAsync(string segmentId, YencStream stream, CancellationToken cancellationToken)
    {
        var cachePath = GetCachePath(segmentId);
        await using var fileStream = new FileStream(cachePath, FileMode.Create, FileAccess.Write, FileShare.None,
            bufferSize: 81920, useAsync: true);
        await stream.CopyToAsync(fileStream, cancellationToken).ConfigureAwait(false);
    }

    private UsenetDecodedBodyResponse ReadCachedBodyAsync(string segmentId, UsenetYencHeader yencHeaders)
    {
        var cachePath = GetCachePath(segmentId);
        var fileStream = new FileStream(cachePath, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 81920, useAsync: true);

        return new UsenetDecodedBodyResponse
        {
            SegmentId = segmentId,
            ResponseCode = (int)UsenetResponseType.ArticleRetrievedBodyFollows,
            ResponseMessage = "222 - Article retrieved from file cache",
            Stream = new CachedYencStream(yencHeaders, fileStream)
        };
    }

    private UsenetDecodedArticleResponse ReadCachedArticleAsync(
        string segmentId, UsenetYencHeader yencHeaders, UsenetArticleHeader articleHeaders)
    {
        var cachePath = GetCachePath(segmentId);
        var fileStream = new FileStream(cachePath, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 81920, useAsync: true);

        return new UsenetDecodedArticleResponse
        {
            SegmentId = segmentId,
            ResponseCode = (int)UsenetResponseType.ArticleRetrievedHeadAndBodyFollow,
            ResponseMessage = "220 - Article retrieved from cache",
            ArticleHeaders = articleHeaders,
            Stream = new CachedYencStream(yencHeaders, fileStream)
        };
    }

    private string GetCachePath(string segmentId)
    {
        // Use SHA256 hash of segment ID to create a valid filename
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(segmentId));
        var filename = Convert.ToHexString(hash);
        return Path.Combine(_cacheDir, filename);
    }

    public override void Dispose()
    {
        // Dispose the underlying client
        // only when leaveOpen is false.
        if (!leaveOpen)
            base.Dispose();

        // Clean up semaphores
        foreach (var semaphore in _pendingRequests.Values)
            semaphore.Dispose();

        _pendingRequests.Clear();
        _cachedSegments.Clear();

        Task.Run(() => Directory.Delete(_cacheDir, recursive: true));
        GC.SuppressFinalize(this);
    }
}