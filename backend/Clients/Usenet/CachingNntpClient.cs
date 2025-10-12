using Microsoft.Extensions.Caching.Memory;
using NzbWebDAV.Streams;
using Usenet.Nzb;
using Usenet.Yenc;

namespace NzbWebDAV.Clients.Usenet;

public class CachingNntpClient(INntpClient client, MemoryCache cache) : WrappingNntpClient(client)
{
    private readonly INntpClient _client = client;

    private readonly MemoryCacheEntryOptions _cacheOptions = new()
    {
        Size = 1,
        SlidingExpiration = TimeSpan.FromHours(3)
    };

    public override async Task<YencHeaderStream> GetSegmentStreamAsync
    (
        string segmentId,
        bool includeHeaders,
        CancellationToken ct
    )
    {
        var cacheKey = segmentId;
        var stream = await _client.GetSegmentStreamAsync(segmentId, includeHeaders, ct);
        cache.Set(cacheKey, stream.Header, _cacheOptions);
        return stream;
    }

    public override async Task<YencHeader> GetSegmentYencHeaderAsync(string segmentId, CancellationToken ct)
    {
        var cacheKey = segmentId;
        return (await cache.GetOrCreateAsync(cacheKey, cacheEntry =>
        {
            cacheEntry.SetOptions(_cacheOptions);
            return _client.GetSegmentYencHeaderAsync(segmentId, ct);
        })!)!;
    }

    public override async Task<long> GetFileSizeAsync(NzbFile file, CancellationToken ct)
    {
        if (file.Segments.Count == 0) return 0;
        var header = await GetSegmentYencHeaderAsync(file.Segments[^1].MessageId, ct);
        return header.PartOffset + header.PartSize;
    }
}