using NzbWebDAV.Clients.Usenet.Models;
using UsenetSharp.Models;

namespace NzbWebDAV.Clients.Usenet;

public class WrappingNntpClient(INntpClient usenetClient) : INntpClient
{
    private INntpClient _usenetClient = usenetClient;

    public virtual Task ConnectAsync(string host, int port, bool useSsl, CancellationToken cancellationToken)
    {
        return _usenetClient.ConnectAsync(host, port, useSsl, cancellationToken);
    }

    public virtual Task<UsenetResponse> AuthenticateAsync(string user, string pass, CancellationToken cancellationToken)
    {
        return _usenetClient.AuthenticateAsync(user, pass, cancellationToken);
    }

    public virtual Task<UsenetStatResponse> StatAsync(SegmentId segmentId, CancellationToken cancellationToken)
    {
        return _usenetClient.StatAsync(segmentId, cancellationToken);
    }

    public virtual Task<UsenetHeadResponse> HeadAsync(SegmentId segmentId, CancellationToken cancellationToken)
    {
        return _usenetClient.HeadAsync(segmentId, cancellationToken);
    }

    public virtual Task<UsenetDecodedBodyResponse> DecodedBodyAsync(SegmentId segmentId, CancellationToken cancellationToken)
    {
        return _usenetClient.DecodedBodyAsync(segmentId, cancellationToken);
    }

    public virtual Task<UsenetDecodedArticleResponse> DecodedArticleAsync(SegmentId segmentId,
        CancellationToken cancellationToken)
    {
        return _usenetClient.DecodedArticleAsync(segmentId, cancellationToken);
    }

    public virtual Task<UsenetDateResponse> DateAsync(CancellationToken cancellationToken)
    {
        return _usenetClient.DateAsync(cancellationToken);
    }

    public virtual Task<UsenetDecodedBodyResponse> DecodedBodyAsync(SegmentId segmentId,
        Action<ArticleBodyResult>? onConnectionReadyAgain, CancellationToken cancellationToken)
    {
        return _usenetClient.DecodedBodyAsync(segmentId, onConnectionReadyAgain, cancellationToken);
    }

    public virtual Task<UsenetDecodedArticleResponse> DecodedArticleAsync(SegmentId segmentId,
        Action<ArticleBodyResult>? onConnectionReadyAgain, CancellationToken cancellationToken)
    {
        return _usenetClient.DecodedArticleAsync(segmentId, onConnectionReadyAgain, cancellationToken);
    }

    protected void ReplaceUnderlyingClient(INntpClient usenetClient)
    {
        var old = _usenetClient;
        _usenetClient = usenetClient;
        if (old is IDisposable disposable)
            disposable.Dispose();
    }

    public virtual void Dispose()
    {
        _usenetClient.Dispose();
        GC.SuppressFinalize(this);
    }
}