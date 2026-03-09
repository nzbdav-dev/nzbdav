using Microsoft.AspNetCore.Mvc;
using NzbWebDAV.Clients.Rclone;

namespace NzbWebDAV.Api.Controllers.TestRcloneConnection;

[ApiController]
[Route("api/test-rclone-connection")]
public class TestRcloneConnectionController() : BaseApiController
{
    private static async Task<TestRcloneConnectionResponse> TestRcloneConnection(TestRcloneConnectionRequest request)
    {
        try
        {
            var result = await RcloneClient
                .TestConnection(request.Host, request.User, request.Pass)
                .ConfigureAwait(false);

            return new TestRcloneConnectionResponse
            {
                Status = true,
                Connected = result.Success,
                Error = result.Error
            };
        }
        catch (Exception e)
        {
            return new TestRcloneConnectionResponse
            {
                Status = true,
                Connected = false,
                Error = e.Message
            };
        }
    }

    protected override async Task<IActionResult> HandleRequest()
    {
        var request = new TestRcloneConnectionRequest(HttpContext);
        var response = await TestRcloneConnection(request).ConfigureAwait(false);
        return Ok(response);
    }
}