using Microsoft.AspNetCore.Mvc;
using NzbWebDAV.Config;
using NzbWebDAV.Database;
using NzbWebDAV.Tasks;
using NzbWebDAV.Websocket;

namespace NzbWebDAV.Api.Controllers.PurgeSampleFiles;

[ApiController]
[Route("api/purge-sample-files/dry-run")]
public class PurgeSampleFilesDryRunController(
    ConfigManager configManager,
    DavDatabaseClient dbClient,
    WebsocketManager websocketManager
) : BaseApiController
{
    protected override async Task<IActionResult> HandleRequest()
    {
        var task = new PurgeSampleFilesTask(configManager, dbClient, websocketManager, isDryRun: true);
        var executed = await task.Execute().ConfigureAwait(false);
        return Ok(executed);
    }
}