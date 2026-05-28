using Microsoft.AspNetCore.Mvc;
using NzbWebDAV.Clients.Usenet;
using NzbWebDAV.Exceptions;
using NzbWebDAV.Extensions;
using Serilog;

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
        catch (CouldNotConnectToUsenetException e)
        {
            var inner = e.InnerException?.Message ?? e.Message;
            Log.Warning("Test connection failed for {Host}:{Port} (ssl={UseSsl}, user={User}): connect error: {Error}",
                request.Host, request.Port, request.UseSsl, request.User, inner);
            return new TestUsenetConnectionResponse { Status = true, Connected = false };
        }
        catch (CouldNotLoginToUsenetException e)
        {
            var inner = e.InnerException?.Message ?? e.Message;
            Log.Warning("Test connection failed for {Host}:{Port} (ssl={UseSsl}, user={User}): login error: {Error}",
                request.Host, request.Port, request.UseSsl, request.User, inner);
            return new TestUsenetConnectionResponse { Status = true, Connected = false };
        }
        catch (Exception e) when (!e.IsCancellationException())
        {
            Log.Warning(e, "Test connection failed for {Host}:{Port} (ssl={UseSsl}, user={User}): unexpected error",
                request.Host, request.Port, request.UseSsl, request.User);
            throw;
        }
    }

    protected override async Task<IActionResult> HandleRequest()
    {
        var request = new TestUsenetConnectionRequest(HttpContext);
        var response = await TestUsenetConnection(request).ConfigureAwait(false);
        return Ok(response);
    }
}