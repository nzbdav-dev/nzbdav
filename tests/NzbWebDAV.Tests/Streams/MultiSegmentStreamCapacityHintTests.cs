using NzbWebDAV.Clients.Usenet;
using NzbWebDAV.Clients.Usenet.Models;
using NzbWebDAV.Streams;
using NzbWebDAV.Tests.Fakes;
using UsenetSharp.Models;
using UsenetSharp.Streams;

namespace NzbWebDAV.Tests.Streams;

public class MultiSegmentStreamCapacityHintTests
{
    [Fact]
    public async Task GetYencHeadersAsync_BeforeDrain_PreservesAllDecodedBytes_ForCachedStream()
    {
        var payload = Enumerable.Range(0, 4096).Select(i => (byte)(i % 251)).ToArray();

        await using var cached = new CachedYencStream(
            new UsenetYencHeader
            {
                FileName = "a.bin",
                FileSize = payload.Length,
                LineLength = 128,
                PartNumber = 1,
                TotalParts = 1,
                PartOffset = 0,
                PartSize = payload.Length,
            },
            new MemoryStream(payload, writable: false));

        var header = await cached.GetYencHeadersAsync();
        Assert.NotNull(header);
        Assert.Equal(payload.Length, header!.PartSize);
        using var ms = new MemoryStream();
        await cached.CopyToAsync(ms);
        Assert.Equal(payload, ms.ToArray());
    }

    [Theory]
    [InlineData(524_000, 525_000, 100, 100, true)]
    [InlineData(710_000, 716_716, 100, 100, true)]
    [InlineData(500_000, 5_000_000, 10, 10, false)]
    [InlineData(100_000, 50_000, 1, 1, true)]
    [InlineData(100_000, 150_000, 1, 1, false)]
    [InlineData(100_000, 100_000, 5, 10, false)] // totalParts < remainingParts
    public void IsPlausiblePartSize_MatchesAverageDerivedBound(
        long estimate, long partSize, int totalParts, int remainingParts, bool expected)
    {
        Assert.Equal(
            expected,
            MultiSegmentStream.IsPlausiblePartSize(partSize, totalParts, remainingParts, estimate));
    }

    [Theory]
    [InlineData(525_000L, 525_000)]
    [InlineData(0L, 0)]
    [InlineData(-1L, 0)]
    public void ToCapacity_ClampsInvalidSizes(long value, int expected)
    {
        Assert.Equal(expected, MultiSegmentStream.ToCapacity(value));
    }

    [Fact]
    public async Task Drain_UsesYencPartSizeWhenEstimateUndershoots()
    {
        // Multi-part file so IsPlausiblePartSize uses the average-derived upper bound.
        // Estimate undershoots the full-part size that the header reports.
        const int estimate = 524_000;
        const int actual = 525_000;
        const int parts = 10;
        var segments = new Dictionary<string, byte[]>();
        for (var i = 0; i < parts - 1; i++)
            segments[$"seg-{i}"] = Enumerable.Repeat((byte)(i + 1), actual).ToArray();
        segments[$"seg-{parts - 1}"] = Enumerable.Repeat((byte)99, estimate).ToArray();

        var client = new FakeNntpClient(segments, useCachedYencStreams: true);

        await using var stream = MultiSegmentStream.Create(
            segments.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray().AsMemory(),
            client,
            articleBufferSize: 2,
            estimatedSegmentSize: estimate,
            failFastOnFirstSegment: false,
            usePipelinedBodyRequests: false,
            CancellationToken.None,
            fileName: "hint.bin");

        Assert.True(MultiSegmentStream.IsPlausiblePartSize(actual, parts, parts, estimate));

        using var output = new MemoryStream();
        await stream.CopyToAsync(output);
        var expected = segments.Keys.OrderBy(k => k, StringComparer.Ordinal)
            .SelectMany(k => segments[k])
            .ToArray();
        Assert.Equal(expected, output.ToArray());
    }

    [Fact]
    public async Task Drain_PrefersImportedExactSizeOverConflictingHeader()
    {
        const int exact = 1000;
        var bytes = Enumerable.Repeat((byte)9, exact).ToArray();
        var client = new FakeNntpClient(
            new Dictionary<string, byte[]> { ["seg-0"] = bytes },
            useCachedYencStreams: true);

        await using var stream = MultiSegmentStream.Create(
            new[] { "seg-0" }.AsMemory(),
            client,
            articleBufferSize: 1,
            estimatedSegmentSize: 500,
            failFastOnFirstSegment: true,
            usePipelinedBodyRequests: false,
            CancellationToken.None,
            fileName: "exact.bin",
            exactSegmentSizes: new long[] { exact });

        using var output = new MemoryStream();
        await stream.CopyToAsync(output);
        Assert.Equal(exact, output.Length);
        Assert.Equal(bytes, output.ToArray());
    }

    [Fact]
    public async Task Drain_FallsBackSafely_WhenHeaderIsImplausible()
    {
        const int size = 2048;
        var bytes = Enumerable.Repeat((byte)3, size).ToArray();
        // Header PartSize matches payload (FakeNntpClient); estimate equals actual so
        // either path rents enough. Cover helper rejection separately.
        var client = new FakeNntpClient(
            new Dictionary<string, byte[]> { ["seg-0"] = bytes },
            useCachedYencStreams: true);

        await using var stream = MultiSegmentStream.Create(
            new[] { "seg-0" }.AsMemory(),
            client,
            articleBufferSize: 1,
            estimatedSegmentSize: size,
            failFastOnFirstSegment: true,
            usePipelinedBodyRequests: false,
            CancellationToken.None,
            fileName: "fallback.bin");

        using var output = new MemoryStream();
        await stream.CopyToAsync(output);
        Assert.Equal(bytes, output.ToArray());
        Assert.Equal(0, MultiSegmentStream.ToCapacity(Array.MaxLength + 1L));
        Assert.False(MultiSegmentStream.IsPlausiblePartSize(0, 10, 10, 1000));
        Assert.False(MultiSegmentStream.IsPlausiblePartSize(-1, 10, 10, 1000));
        Assert.False(MultiSegmentStream.IsPlausiblePartSize(5_000_000, 10, 10, 500_000));
    }

    [Theory]
    [InlineData(typeof(InvalidDataException))]
    [InlineData(typeof(IOException))]
    public async Task Drain_FallsBackToEstimate_WhenGetYencHeadersThrows(Type exceptionType)
    {
        const int size = 4096;
        var bytes = Enumerable.Repeat((byte)7, size).ToArray();
        var toThrow = (Exception)Activator.CreateInstance(exceptionType, "header parse failed")!;
        var client = new HeaderThrowingBodyClient(bytes, toThrow);

        await using var stream = MultiSegmentStream.Create(
            new[] { "seg-0" }.AsMemory(),
            client,
            articleBufferSize: 1,
            estimatedSegmentSize: size,
            failFastOnFirstSegment: true,
            usePipelinedBodyRequests: false,
            CancellationToken.None,
            fileName: "header-throw.bin");

        using var output = new MemoryStream();
        await stream.CopyToAsync(output);
        Assert.Equal(bytes, output.ToArray());
    }
}

/// <summary>
/// Returns a YencStream whose header parse throws but whose body still decodes via ReadAsync,
/// matching the ResolveDrainCapacityHintAsync fallback contract.
/// </summary>
file sealed class HeaderThrowingBodyClient(byte[] payload, Exception headerException) : NntpClient
{
    public override Task ConnectAsync(
        string host, int port, bool useSsl, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public override Task<UsenetResponse> AuthenticateAsync(
        string user, string pass, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public override Task<UsenetStatResponse> StatAsync(
        SegmentId segmentId, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public override Task<UsenetHeadResponse> HeadAsync(
        SegmentId segmentId, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public override Task<UsenetDecodedBodyResponse> DecodedBodyAsync(
        SegmentId segmentId, CancellationToken cancellationToken) =>
        DecodedBodyAsync(segmentId, null, cancellationToken);

    public override Task<UsenetDecodedBodyResponse> DecodedBodyAsync(
        SegmentId segmentId,
        Action<ArticleBodyResult>? onConnectionReadyAgain,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        onConnectionReadyAgain?.Invoke(ArticleBodyResult.Retrieved);
        return Task.FromResult(new UsenetDecodedBodyResponse
        {
            SegmentId = segmentId.ToString(),
            ResponseCode = (int)UsenetResponseType.ArticleRetrievedBodyFollows,
            ResponseMessage = "222 fake body",
            Stream = new HeaderThrowingYencStream(payload, headerException),
        });
    }

    public override Task<UsenetDecodedBodyBatch> DecodedBodiesAsync(
        IReadOnlyList<SegmentId> segmentIds,
        Action<ArticleBodyResult>? onConnectionReadyAgain,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public override Task<UsenetDecodedArticleResponse> DecodedArticleAsync(
        SegmentId segmentId, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public override Task<UsenetDecodedArticleResponse> DecodedArticleAsync(
        SegmentId segmentId,
        Action<ArticleBodyResult>? onConnectionReadyAgain,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public override Task<UsenetDateResponse> DateAsync(CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public override Task<UsenetExclusiveConnection> AcquireExclusiveConnectionAsync(
        string segmentId, CancellationToken cancellationToken) =>
        Task.FromResult(new UsenetExclusiveConnection(null));

    public override Task<UsenetExclusiveConnection> AcquireExclusiveConnectionAsync(
        IReadOnlyList<SegmentId> segmentIds, CancellationToken cancellationToken) =>
        Task.FromResult(new UsenetExclusiveConnection(null));

    public override Task<UsenetDecodedBodyResponse> DecodedBodyAsync(
        SegmentId segmentId,
        UsenetExclusiveConnection exclusiveConnection,
        CancellationToken cancellationToken) =>
        DecodedBodyAsync(segmentId, exclusiveConnection.OnConnectionReadyAgain, cancellationToken);

    public override Task<UsenetDecodedBodyBatch> DecodedBodiesAsync(
        IReadOnlyList<SegmentId> segmentIds,
        UsenetExclusiveConnection exclusiveConnection,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public override Task<UsenetDecodedArticleResponse> DecodedArticleAsync(
        SegmentId segmentId,
        UsenetExclusiveConnection exclusiveConnection,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public override void Dispose()
    {
    }
}

file sealed class HeaderThrowingYencStream : CachedYencStream
{
    private readonly Exception _headerException;

    public HeaderThrowingYencStream(byte[] payload, Exception headerException)
        : base(
            new UsenetYencHeader
            {
                FileName = "throw.bin",
                FileSize = payload.Length,
                LineLength = 128,
                PartNumber = 1,
                TotalParts = 1,
                PartOffset = 0,
                PartSize = payload.Length,
            },
            new MemoryStream(payload, writable: false))
    {
        _headerException = headerException;
    }

    public override ValueTask<UsenetYencHeader?> GetYencHeadersAsync(
        CancellationToken cancellationToken = default) =>
        throw _headerException;
}
