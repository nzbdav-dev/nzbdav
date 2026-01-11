using Microsoft.AspNetCore.Mvc;
using NzbWebDAV.Config;
using NzbWebDAV.Database;
using NzbWebDAV.Tasks;
using NzbWebDAV.Websocket;

namespace NzbWebDAV.Api.Controllers;

[ApiController]
[Route("api/recreate-strm-files")]
public class RecreateStrmFiles(
    ConfigManager configManager,
    DavDatabaseClient dbClient,
    WebsocketManager websocketManager
) : BaseApiController
{
    protected override async Task<IActionResult> HandleRequest()
    {
        var task = new RecreateStrmFilesTask(configManager, dbClient, websocketManager);
        var executed = await task.Execute().ConfigureAwait(false);
        return Ok(executed);
    }
}