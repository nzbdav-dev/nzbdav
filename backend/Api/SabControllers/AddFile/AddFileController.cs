using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NzbWebDAV.Api.SabControllers.GetQueue;
using NzbWebDAV.Config;
using NzbWebDAV.Database;
using NzbWebDAV.Database.Models;
using NzbWebDAV.Extensions;
using NzbWebDAV.Queue;
using NzbWebDAV.Websocket;
using Usenet.Nzb;

namespace NzbWebDAV.Api.SabControllers.AddFile;

public class AddFileController(
    HttpContext httpContext,
    DavDatabaseClient dbClient,
    QueueManager queueManager,
    ConfigManager configManager,
    WebsocketManager websocketManager
) : SabApiController.BaseController(httpContext, configManager)
{
    public async Task<AddFileResponse> AddFileAsync(AddFileRequest request)
    {
        // load the document
        var nzbFileContents = NormalizeNzbContents(request.NzbFileContents);
        var documentBytes = Encoding.UTF8.GetBytes(nzbFileContents);
        using var memoryStream = new MemoryStream(documentBytes);
        var document = await NzbDocument.LoadAsync(memoryStream).ConfigureAwait(false);

        // add the queueItem to the database
        var queueItem = new QueueItem
        {
            Id = Guid.NewGuid(),
            CreatedAt = DateTime.Now,
            FileName = request.FileName,
            JobName = Path.GetFileNameWithoutExtension(request.FileName),
            NzbFileSize = documentBytes.Length,
            TotalSegmentBytes = document.Files.SelectMany(x => x.Segments).Select(x => x.Size).Sum(),
            Category = request.Category,
            Priority = request.Priority,
            PostProcessing = request.PostProcessing,
            PauseUntil = request.PauseUntil
        };
        var queueNzbContents = new QueueNzbContents()
        {
            Id = queueItem.Id,
            NzbContents = nzbFileContents,
        };
        dbClient.Ctx.QueueItems.Add(queueItem);
        dbClient.Ctx.QueueNzbContents.Add(queueNzbContents);
        await dbClient.Ctx.SaveChangesAsync(request.CancellationToken).ConfigureAwait(false);
        var message = GetQueueResponse.QueueSlot.FromQueueItem(queueItem).ToJson();
        _ = websocketManager.SendMessage(WebsocketTopic.QueueItemAdded, message);

        // awaken the queue if it is sleeping
        queueManager.AwakenQueue();

        // return response
        return new AddFileResponse()
        {
            Status = true,
            NzoIds = [queueItem.Id.ToString()],
        };
    }

    protected override async Task<IActionResult> Handle()
    {
        var request = await AddFileRequest.New(httpContext).ConfigureAwait(false);
        return Ok(await AddFileAsync(request).ConfigureAwait(false));
    }

    private static string NormalizeNzbContents(string nzbContents)
    {
        return nzbContents
            .Replace("https://www.newzbin.com/DTD/2003/nzb", "http://www.newzbin.com/DTD/2003/nzb")
            .Replace("date=\"\"", "date=\"0\"");
    }
}