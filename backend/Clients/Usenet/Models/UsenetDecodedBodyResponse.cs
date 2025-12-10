using UsenetSharp.Models;
using UsenetSharp.Streams;

namespace NzbWebDAV.Clients.Usenet.Models;

public record UsenetDecodedBodyResponse : UsenetResponse
{
    public required string SegmentId { get; init; }
    public required YencStream Stream { get; init; }
}