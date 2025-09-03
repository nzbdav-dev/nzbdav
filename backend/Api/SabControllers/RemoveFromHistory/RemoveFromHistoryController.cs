using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
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
        await using var transaction = await dbClient.Ctx.Database.BeginTransactionAsync();
        await dbClient.RemoveHistoryItemsAsync(request.NzoIds, request.DeleteCompletedFiles, request.CancellationToken);
        await dbClient.Ctx.SaveChangesAsync(request.CancellationToken);
        await transaction.CommitAsync(request.CancellationToken);
        _ = websocketManager.SendMessage(WebsocketTopic.HistoryItemRemoved, string.Join(",", request.NzoIds));
        return new RemoveFromHistoryResponse() { Status = true };
    }

    protected override async Task<IActionResult> Handle()
    {
        var request = await RemoveFromHistoryRequest.New(httpContext);
        return Ok(await RemoveFromHistory(request));
    }
}