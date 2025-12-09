using NzbWebDAV.Clients.Usenet.Models;
using UsenetSharp.Models;

namespace NzbWebDAV.Clients.Usenet;

public class WrappingNntpClient(INntpClient client) : INntpClient
{
    private INntpClient _client = client;

    public Task ConnectAsync(string host, int port, bool useSsl, CancellationToken cancellationToken)
    {
        return _client.ConnectAsync(host, port, useSsl, cancellationToken);
    }

    public Task<UsenetResponse> AuthenticateAsync(string user, string pass, CancellationToken cancellationToken)
    {
        return _client.AuthenticateAsync(user, pass, cancellationToken);
    }

    public Task<UsenetStatResponse> StatAsync(SegmentId segmentId, CancellationToken cancellationToken)
    {
        return _client.StatAsync(segmentId, cancellationToken);
    }

    public Task<UsenetHeadResponse> HeadAsync(SegmentId segmentId, CancellationToken cancellationToken)
    {
        return _client.HeadAsync(segmentId, cancellationToken);
    }

    public Task<UsenetDecodedBodyResponse> DecodedBodyAsync(SegmentId segmentId, CancellationToken cancellationToken)
    {
        return _client.DecodedBodyAsync(segmentId, cancellationToken);
    }

    public Task<UsenetDecodedArticleResponse> DecodedArticleAsync(SegmentId segmentId,
        CancellationToken cancellationToken)
    {
        return _client.DecodedArticleAsync(segmentId, cancellationToken);
    }

    public Task<UsenetDateResponse> DateAsync(CancellationToken cancellationToken)
    {
        return _client.DateAsync(cancellationToken);
    }

    public Task WaitForReadyAsync(CancellationToken cancellationToken)
    {
        return _client.WaitForReadyAsync(cancellationToken);
    }

    public Task<UsenetDecodedBodyResponse> DecodedBodyAsync(SegmentId segmentId,
        Action<ArticleBodyResult>? onConnectionReadyAgain, CancellationToken cancellationToken)
    {
        return _client.DecodedBodyAsync(segmentId, onConnectionReadyAgain, cancellationToken);
    }

    public Task<UsenetDecodedArticleResponse> DecodedArticleAsync(SegmentId segmentId,
        Action<ArticleBodyResult>? onConnectionReadyAgain, CancellationToken cancellationToken)
    {
        return _client.DecodedArticleAsync(segmentId, onConnectionReadyAgain, cancellationToken);
    }

    protected void ReplaceUnderlyingClient(INntpClient client)
    {
        var old = _client;
        _client = client;
        if (old is IDisposable disposable)
            disposable.Dispose();
    }

    public void Dispose()
    {
        _client.Dispose();
        GC.SuppressFinalize(this);
    }
}