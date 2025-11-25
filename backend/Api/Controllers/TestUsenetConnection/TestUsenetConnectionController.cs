using Microsoft.AspNetCore.Mvc;
using NzbWebDAV.Clients.Usenet;
using NzbWebDAV.Exceptions;

namespace NzbWebDAV.Api.Controllers.TestUsenetConnection;

[ApiController]
[Route("api/test-usenet-connection")]
public class TestUsenetConnectionController() : BaseApiController
{
    private async Task<TestUsenetConnectionResponse> TestUsenetConnection(TestUsenetConnectionRequest request)
    {
        try
        {
            await UsenetStreamingClient.CreateNewConnection(request.ToConnectionDetails(), HttpContext.RequestAborted).ConfigureAwait(false);
            return new TestUsenetConnectionResponse { Status = true, Connected = true };
        }
        catch (CouldNotConnectToUsenetException)
        {
            return new TestUsenetConnectionResponse { Status = true, Connected = false };
        }
        catch (CouldNotLoginToUsenetException)
        {
            return new TestUsenetConnectionResponse { Status = true, Connected = false };
        }
    }

    protected override async Task<IActionResult> HandleRequest()
    {
        var request = new TestUsenetConnectionRequest(HttpContext);
        var response = await TestUsenetConnection(request).ConfigureAwait(false);
        return Ok(response);
    }
}