using UsenetSharp.Models;
using UsenetSharp.Streams;

namespace NzbWebDAV.Clients.Usenet.Models;

public record UsenetDecodedArticleResponse : UsenetResponse
{
    public required string SegmentId { get; init; }
    public required YencStream Stream { get; init; }
    public required UsenetArticleHeader ArticleHeaders { get; init; }
}