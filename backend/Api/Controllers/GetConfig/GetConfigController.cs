using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NzbWebDAV.Database;

namespace NzbWebDAV.Api.Controllers.GetConfig;

[ApiController]
[Route("api/get-config")]
public class GetConfigController(DavDatabaseClient dbClient) : BaseApiController
{
    private async Task<GetConfigResponse> GetConfig(GetConfigRequest request)
    {
        var configItems = await dbClient.Ctx.ConfigItems
            .Where(x => request.ConfigKeys.Contains(x.ConfigName))
            .ToListAsync(HttpContext.RequestAborted).ConfigureAwait(false);

        var response = new GetConfigResponse { ConfigItems = configItems };
        return response;
    }

    protected override async Task<IActionResult> HandleRequest()
    {
        var request = new GetConfigRequest(HttpContext);
        var response = await GetConfig(request).ConfigureAwait(false);
        return Ok(response);
    }
}