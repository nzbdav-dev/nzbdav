using NzbWebDAV.Streams;
using Usenet.Nntp.Responses;
using Usenet.Nzb;
using Usenet.Yenc;

namespace NzbWebDAV.Clients.Usenet;

public interface INntpClient: IDisposable
{
    Task<bool> ConnectAsync(string host, int port, bool useSsl, CancellationToken cancellationToken);
    Task<bool> AuthenticateAsync(string user, string pass, CancellationToken cancellationToken);
    Task<NntpStatResponse> StatAsync(string segmentId, CancellationToken cancellationToken);
    Task<YencHeaderStream> GetSegmentStreamAsync(string segmentId, CancellationToken cancellationToken);
    Task<YencHeader> GetSegmentYencHeaderAsync(string segmentId, CancellationToken cancellationToken);
    Task<long> GetFileSizeAsync(NzbFile file, CancellationToken cancellationToken);
    Task<NntpDateResponse> DateAsync(CancellationToken cancellationToken);
    Task WaitForReady(CancellationToken cancellationToken);
}