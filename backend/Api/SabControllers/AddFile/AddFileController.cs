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
            // backup the nzb file if enabled
            if (configManager.IsNzbBackupEnabled())
            {
                var backupLocation = configManager.GetNzbBackupLocation();
                if (backupLocation != null)
                {
                    await BackupNzbAsync(id, request.FileName, request.Category, backupLocation);
                }
            }

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
                NzbFileSize = nzbFileStream.Length,
                TotalSegmentBytes = totalSegmentBytes,
                Category = request.Category,
                Priority = request.Priority,
                PostProcessing = request.PostProcessing,
                PauseUntil = request.PauseUntil
            };

            // record the original NZB filename so it can be served at download time
            var nzbName = new NzbName
            {
                Id = id,
                FileName = request.FileName
            };

            // save
            dbClient.Ctx.QueueItems.Add(queueItem);
            dbClient.Ctx.NzbNames.Add(nzbName);
            await dbClient.Ctx.SaveChangesAsync(request.CancellationToken).ConfigureAwait(false);
            _ = DavDatabaseContext.RcloneVfsForget(["/nzbs"]);
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
        queueManager.AwakenQueue(request.PauseUntil);

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

    private static async Task BackupNzbAsync(Guid id, string fileName, string category, string backupLocation)
    {
        try
        {
            if (!Directory.Exists(backupLocation))
                Directory.CreateDirectory(backupLocation);

            var destDir = Path.Combine(backupLocation, category);
            if (!Directory.Exists(destDir))
                Directory.CreateDirectory(destDir);

            var baseName = Path.GetFileNameWithoutExtension(fileName);
            var ext = Path.GetExtension(fileName);
            if (string.IsNullOrEmpty(ext)) ext = ".nzb";

            var destPath = Path.Combine(destDir, $"{baseName}{ext}");
            var counter = 2;
            while (System.IO.File.Exists(destPath))
            {
                destPath = Path.Combine(destDir, $"{baseName} ({counter}){ext}");
                counter++;
            }

            await using var src = BlobStore.ReadBlob(id);
            await using var dst = System.IO.File.Create(destPath);
            await src.CopyToAsync(dst);
        }
        catch (Exception e)
        {
            throw new Exception($"Could not save nzb to `{backupLocation}`", e);
        }
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