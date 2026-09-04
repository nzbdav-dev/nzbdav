namespace NzbWebDAV.Api.Controllers.TestUsenetPipelining;

public class TestUsenetPipeliningResponse : BaseApiResponse
{
    // Whether we could connect + authenticate at all (mirrors the connection test).
    public bool Connected { get; set; }

    // Whether the provider correctly handled a pipelined STAT batch (in-order, no drop/timeout).
    public bool Supported { get; set; }
}
