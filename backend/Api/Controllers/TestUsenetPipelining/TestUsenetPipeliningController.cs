using Microsoft.AspNetCore.Mvc;
using NzbWebDAV.Api.Controllers.TestUsenetConnection;
using NzbWebDAV.Clients.Usenet;
using NzbWebDAV.Exceptions;
using UsenetSharp.Models;

namespace NzbWebDAV.Api.Controllers.TestUsenetPipelining;

[ApiController]
[Route("api/test-usenet-pipelining")]
public class TestUsenetPipeliningController() : BaseApiController
{
    // A small probe is enough to tell whether the server tolerates pipelined STAT. We send a
    // handful of commands at once; servers that don't support pipelining stall, drop the
    // connection, or desync rather than answering all of them in order.
    private const int ProbeBatchSize = 5;

    private async Task<TestUsenetPipeliningResponse> TestPipelining(TestUsenetConnectionRequest request)
    {
        INntpClient connection;
        try
        {
            connection = await UsenetStreamingClient
                .CreateNewConnection(request.ToConnectionDetails(), HttpContext.RequestAborted)
                .ConfigureAwait(false);
        }
        catch (CouldNotConnectToUsenetException)
        {
            return new TestUsenetPipeliningResponse { Status = true, Connected = false, Supported = false };
        }
        catch (CouldNotLoginToUsenetException)
        {
            return new TestUsenetPipeliningResponse { Status = true, Connected = false, Supported = false };
        }

        try
        {
            // STAT a batch of randomly-generated, non-existent message-ids. These have no side
            // effects and every server answers each with a 430. If we get the expected number of
            // 430s back, the server accepted the whole pipelined batch and answered in order.
            var probeIds = GenerateProbeMessageIds(ProbeBatchSize);
            var results = await connection
                .StatPipelinedAsync(probeIds, HttpContext.RequestAborted)
                .ConfigureAwait(false);

            var supported = results.Count == probeIds.Count
                            && results.All(r => r.ResponseType == UsenetResponseType.NoArticleWithThatMessageId);

            return new TestUsenetPipeliningResponse { Status = true, Connected = true, Supported = supported };
        }
        catch (Exception e) when (e is not OperationCanceledException ||
                                  !HttpContext.RequestAborted.IsCancellationRequested)
        {
            // Timeout, dropped connection or protocol desync -> not safe to pipeline this provider.
            return new TestUsenetPipeliningResponse { Status = true, Connected = true, Supported = false };
        }
        finally
        {
            connection.Dispose();
        }
    }

    private static List<string> GenerateProbeMessageIds(int count)
    {
        var ids = new List<string>(count);
        for (var i = 0; i < count; i++)
            ids.Add($"nzbdav-pipelining-probe-{Guid.NewGuid():N}@nzbdav.invalid");
        return ids;
    }

    protected override async Task<IActionResult> HandleRequest()
    {
        var request = new TestUsenetConnectionRequest(HttpContext);
        var response = await TestPipelining(request).ConfigureAwait(false);
        return Ok(response);
    }
}
