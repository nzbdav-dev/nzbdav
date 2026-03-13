using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NzbWebDAV.Config;

namespace NzbWebDAV.Api.SabControllers.GetStatus;

public class GetStatusController(
    HttpContext httpContext,
    ConfigManager configManager
) : SabApiController.BaseController(httpContext, configManager)
{
    protected override Task<IActionResult> Handle()
    {
        var response = new GetStatusResponse() { Status = true };
        return Task.FromResult<IActionResult>(Ok(response));
    }
}