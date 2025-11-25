using Microsoft.AspNetCore.Mvc;
using NzbWebDAV.Clients.RadarrSonarr;

namespace NzbWebDAV.Api.Controllers.TestArrConnection;

[ApiController]
[Route("api/test-arr-connection")]
public class TestArrConnectionController() : BaseApiController
{
    private static async Task<TestArrConnectionResponse> TestArrConnection(TestArrConnectionRequest request)
    {
        try
        {
            var client = new ArrClient(request.Host, request.ApiKey);
            var apiInfo = await client.GetApiInfo().ConfigureAwait(false);
            return new TestArrConnectionResponse
            {
                Status = true,
                Connected = apiInfo.Current?.Length > 0
            };
        }
        catch (Exception e)
        {
            return new TestArrConnectionResponse
            {
                Status = true,
                Connected = false,
                Error = e.Message
            };
        }
    }

    protected override async Task<IActionResult> HandleRequest()
    {
        var request = new TestArrConnectionRequest(HttpContext);
        var response = await TestArrConnection(request).ConfigureAwait(false);
        return Ok(response);
    }
}