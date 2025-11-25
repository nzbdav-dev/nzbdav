using Microsoft.AspNetCore.Mvc;
using NzbWebDAV.Config;
using NzbWebDAV.Database;
using NzbWebDAV.Tasks;
using NzbWebDAV.Websocket;

namespace NzbWebDAV.Api.Controllers.ConvertStrmToSymlinks;

[ApiController]
[Route("api/convert-strm-to-symlinks")]
public class ConvertStrmToSymlinks(
    ConfigManager configManager,
    DavDatabaseClient dbClient,
    WebsocketManager websocketManager
) : BaseApiController
{
    protected override async Task<IActionResult> HandleRequest()
    {
        var task = new StrmToSymlinksTask(configManager, dbClient, websocketManager);
        var executed = await task.Execute().ConfigureAwait(false);
        return Ok(executed);
    }
}