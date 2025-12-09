using NzbWebDAV.Clients.Usenet.Models;
using NzbWebDAV.Streams;
using Usenet.Nzb;
using UsenetSharp.Models;

namespace NzbWebDAV.Clients.Usenet;

public interface INntpClient : IDisposable
{
    // core methods
    Task ConnectAsync(
        string host, int port, bool useSsl, CancellationToken cancellationToken);

    Task<UsenetResponse> AuthenticateAsync(
        string user, string pass, CancellationToken cancellationToken);

    Task<UsenetStatResponse> StatAsync(
        SegmentId segmentId, CancellationToken cancellationToken);

    Task<UsenetHeadResponse> HeadAsync(
        SegmentId segmentId, CancellationToken cancellationToken);

    Task<UsenetDecodedBodyResponse> DecodedBodyAsync(
        SegmentId segmentId, CancellationToken cancellationToken);

    Task<UsenetDecodedBodyResponse> DecodedBodyAsync(
        SegmentId segmentId, Action<ArticleBodyResult>? onConnectionReadyAgain, CancellationToken cancellationToken);

    Task<UsenetDecodedArticleResponse> DecodedArticleAsync(
        SegmentId segmentId, CancellationToken cancellationToken);

    Task<UsenetDecodedArticleResponse> DecodedArticleAsync(
        SegmentId segmentId, Action<ArticleBodyResult>? onConnectionReadyAgain, CancellationToken cancellationToken);

    Task<UsenetDateResponse> DateAsync(
        CancellationToken cancellationToken);

    // helpers
    Task<UsenetYencHeader> GetYencHeadersAsync(
        string segmentId, CancellationToken ct);

    Task<long> GetFileSizeAsync(
        NzbFile file, CancellationToken ct);

    Task<NzbFileStream> GetFileStream(
        NzbFile nzbFile, int concurrentConnections, CancellationToken ct);

    NzbFileStream GetFileStream(
        NzbFile nzbFile, long fileSize, int concurrentConnections);

    NzbFileStream GetFileStream(
        string[] segmentIds, long fileSize, int concurrentConnections);

    Task CheckAllSegmentsAsync(
        IEnumerable<string> segmentIds, int concurrency, IProgress<int>? progress, CancellationToken cancellationToken);
}