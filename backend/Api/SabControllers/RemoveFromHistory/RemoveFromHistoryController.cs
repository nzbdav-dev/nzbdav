using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NzbWebDAV.Config;
using NzbWebDAV.Database;
using NzbWebDAV.Websocket;

namespace NzbWebDAV.Api.SabControllers.RemoveFromHistory;

public class RemoveFromHistoryController(
    HttpContext httpContext,
    DavDatabaseClient dbClient,
    ConfigManager configManager,
    WebsocketManager websocketManager
) : SabApiController.BaseController(httpContext, configManager)
{
    public async Task<RemoveFromHistoryResponse> RemoveFromHistory(RemoveFromHistoryRequest request)
    {
        await dbClient.RemoveHistoryItemsAsync(request.NzoIds, request.DeleteCompletedFiles, request.CancellationToken).ConfigureAwait(false);
        try
        {
            await dbClient.Ctx.SaveChangesAsync(request.CancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Item already removed by a prior call; SAB API delete is idempotent.
        }
        _ = websocketManager.SendMessage(WebsocketTopic.HistoryItemRemoved, string.Join(",", request.NzoIds));
        return new RemoveFromHistoryResponse() { Status = true };
    }

    protected override async Task<IActionResult> Handle()
    {
        var request = await RemoveFromHistoryRequest.New(httpContext).ConfigureAwait(false);
        return Ok(await RemoveFromHistory(request).ConfigureAwait(false));
    }
}