﻿using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NzbWebDAV.Config;
using NzbWebDAV.Database;
using NzbWebDAV.Database.Models;
using NzbWebDAV.Queue;
using Usenet.Nzb;

namespace NzbWebDAV.Api.SabControllers.AddFile;

public class AddFileController(
    HttpContext httpContext,
    DavDatabaseClient dbClient,
    QueueManager queueManager,
    ConfigManager configManager
) : SabApiController.BaseController(httpContext, configManager)
{
    public async Task<AddFileResponse> AddFileAsync(AddFileRequest request)
    {
        // load the document
        var documentBytes = Encoding.UTF8.GetBytes(request.NzbFileContents);
        using var memoryStream = new MemoryStream(documentBytes);
        var document = await NzbDocument.LoadAsync(memoryStream);

        // add the queueItem to the database
        var queueItem = new QueueItem
        {
            Id = Guid.NewGuid(),
            CreatedAt = DateTime.Now,
            FileName = request.FileName,
            JobName = Path.GetFileNameWithoutExtension(request.FileName),
            NzbContents = request.NzbFileContents,
            NzbFileSize = documentBytes.Length,
            TotalSegmentBytes = document.Files.SelectMany(x => x.Segments).Select(x => x.Size).Sum(),
            Category = request.Category,
            Priority = request.Priority,
            PostProcessing = request.PostProcessing,
            PauseUntil = request.PauseUntil
        };
        dbClient.Ctx.QueueItems.Add(queueItem);
        await dbClient.Ctx.SaveChangesAsync(request.CancellationToken);

        // return response
        return new AddFileResponse()
        {
            Status = true,
            NzoIds = [queueItem.Id.ToString()],
        };
    }

    protected override async Task<IActionResult> Handle()
    {
        var request = await AddFileRequest.New(httpContext);
        return Ok(await AddFileAsync(request));
    }
}