using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NzbWebDAV.Extensions;
using NzbWebDAV.Utils;

namespace NzbWebDAV.Api.Controllers;

public abstract class BaseApiController : ControllerBase
{
    protected virtual bool RequiresAuthentication => true;
    protected abstract Task<IActionResult> HandleRequest();

    [HttpGet]
    [HttpPost]
    public async Task<IActionResult> HandleApiRequest()
    {
        try
        {
            if (RequiresAuthentication)
            {
                var apiKey = HttpContext.GetRequestApiKey();
                if (apiKey == null)
                    throw new UnauthorizedAccessException("API Key Required");
                if (apiKey != EnvironmentUtil.GetVariable("FRONTEND_BACKEND_API_KEY"))
                    throw new UnauthorizedAccessException("API Key Incorrect");
            }

            return await HandleRequest();
        }
        catch (BadHttpRequestException e)
        {
            return BadRequest(new BaseApiResponse()
            {
                Status = false,
                Error = e.Message
            });
        }
        catch (UnauthorizedAccessException e)
        {
            return Unauthorized(new BaseApiResponse()
            {
                Status = false,
                Error = e.Message
            });
        }
        catch (Exception e) when (e is not OperationCanceledException ||
                                  !HttpContext.RequestAborted.IsCancellationRequested)
        {
            return StatusCode(500, new BaseApiResponse()
            {
                Status = false,
                Error = e.Message
            });
        }
    }
}