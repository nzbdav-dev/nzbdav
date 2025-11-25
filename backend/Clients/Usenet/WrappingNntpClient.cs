using NzbWebDAV.Clients.Usenet.Models;
using NzbWebDAV.Streams;
using Usenet.Nntp.Responses;
using Usenet.Nzb;
using Usenet.Yenc;

namespace NzbWebDAV.Clients.Usenet;

public abstract class WrappingNntpClient(INntpClient client) : INntpClient
{
    protected INntpClient Client = client;

    public virtual Task<bool> ConnectAsync(string host, int port, bool useSsl, CancellationToken cancellationToken)
    {
        return Client.ConnectAsync(host, port, useSsl, cancellationToken);
    }

    public virtual Task<bool> AuthenticateAsync(string user, string pass, CancellationToken cancellationToken)
    {
        return Client.AuthenticateAsync(user, pass, cancellationToken);
    }

    public virtual Task<NntpStatResponse> StatAsync(string segmentId, CancellationToken cancellationToken)
    {
        return Client.StatAsync(segmentId, cancellationToken);
    }

    public virtual Task<NntpDateResponse> DateAsync(CancellationToken cancellationToken)
    {
        return Client.DateAsync(cancellationToken);
    }

    public Task<UsenetArticleHeaders> GetArticleHeadersAsync(string segmentId, CancellationToken cancellationToken)
    {
        return Client.GetArticleHeadersAsync(segmentId, cancellationToken);
    }

    public virtual Task<YencHeaderStream> GetSegmentStreamAsync
    (
        string segmentId,
        bool includeHeaders,
        CancellationToken cancellationToken
    )
    {
        return Client.GetSegmentStreamAsync(segmentId, includeHeaders, cancellationToken);
    }

    public virtual Task<YencHeader> GetSegmentYencHeaderAsync(string segmentId, CancellationToken cancellationToken)
    {
        return Client.GetSegmentYencHeaderAsync(segmentId, cancellationToken);
    }

    public virtual Task<long> GetFileSizeAsync(NzbFile file, CancellationToken cancellationToken)
    {
        return Client.GetFileSizeAsync(file, cancellationToken);
    }

    public virtual Task WaitForReady(CancellationToken cancellationToken)
    {
        return Client.WaitForReady(cancellationToken);
    }

    public void UpdateUnderlyingClient(INntpClient client)
    {
        var oldClient = Client;
        Client = client;
        oldClient.Dispose();
    }

    public void Dispose()
    {
        Client.Dispose();
        GC.SuppressFinalize(this);
    }
}