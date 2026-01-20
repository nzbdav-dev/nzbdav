using System.Xml;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NzbWebDAV.Api.SabControllers.GetQueue;
using NzbWebDAV.Config;
using NzbWebDAV.Database;
using NzbWebDAV.Database.Models;
using NzbWebDAV.Extensions;
using NzbWebDAV.Queue;
using NzbWebDAV.Utils;
using NzbWebDAV.Websocket;

namespace NzbWebDAV.Api.SabControllers.AddFile;

public class AddFileController(
    HttpContext httpContext,
    DavDatabaseClient dbClient,
    QueueManager queueManager,
    ConfigManager configManager,
    WebsocketManager websocketManager
) : SabApiController.BaseController(httpContext, configManager)
{
    private static readonly XmlReaderSettings XmlSettings = new()
    {
        Async = true,
        DtdProcessing = DtdProcessing.Ignore
    };

    public async Task<AddFileResponse> AddFileAsync(AddFileRequest request)
    {
        var id = Guid.NewGuid();

        // write the file to the blob-store
        await using var stream = request.NzbFileStream;
        await BlobStore.WriteBlob(id, stream);

        // save the queue item to the database
        QueueItem? queueItem;
        try
        {
            // compute the total segment bytes
            await using var nzbFileStream = BlobStore.ReadBlob(id);
            var totalSegmentBytes = ComputeTotalSegmentBytes(nzbFileStream);

            // create the queue item record
            queueItem = new QueueItem
            {
                Id = id,
                CreatedAt = DateTime.Now,
                FileName = request.FileName,
                JobName = FilenameUtil.GetJobName(request.FileName),
                NzbFileSize = stream.Length,
                TotalSegmentBytes = totalSegmentBytes,
                Category = request.Category,
                Priority = request.Priority,
                PostProcessing = request.PostProcessing,
                PauseUntil = request.PauseUntil
            };

            // save
            dbClient.Ctx.QueueItems.Add(queueItem);
            await dbClient.Ctx.SaveChangesAsync(request.CancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // in case of any errors writing to the database
            // delete the nzb file blob
            BlobStore.Delete(id);
            throw;
        }

        // inform the frontend that a new item was added to the queue
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
        var request = await AddFileRequest.New(httpContext, configManager).ConfigureAwait(false);
        return Ok(await AddFileAsync(request).ConfigureAwait(false));
    }

    private static long ComputeTotalSegmentBytes(Stream stream)
    {
        long totalBytes = 0;
        using var reader = XmlReader.Create(stream, XmlSettings);
        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "segment") continue;
            var bytesAttr = reader.GetAttribute("bytes");
            if (bytesAttr != null && long.TryParse(bytesAttr, out var bytes))
            {
                totalBytes += bytes;
            }
        }

        return totalBytes;
    }
}