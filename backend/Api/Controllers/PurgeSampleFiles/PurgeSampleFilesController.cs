using Microsoft.AspNetCore.Mvc;
using NzbWebDAV.Config;
using NzbWebDAV.Database;
using NzbWebDAV.Tasks;
using NzbWebDAV.Websocket;

namespace NzbWebDAV.Api.Controllers.PurgeSampleFiles;

[ApiController]
[Route("api/purge-sample-files")]
public class PurgeSampleFilesController(
    ConfigManager configManager,
    DavDatabaseClient dbClient,
    WebsocketManager websocketManager
) : BaseApiController
{
    protected override async Task<IActionResult> HandleRequest()
    {
        var task = new PurgeSampleFilesTask(configManager, dbClient, websocketManager, isDryRun: false);
        var executed = await task.Execute().ConfigureAwait(false);
        return Ok(executed);
    }
}