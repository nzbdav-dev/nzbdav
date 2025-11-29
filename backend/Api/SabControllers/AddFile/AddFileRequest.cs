using Microsoft.AspNetCore.Http;
using NzbWebDAV.Config;
using NzbWebDAV.Database.Models;
using NzbWebDAV.Extensions;

namespace NzbWebDAV.Api.SabControllers.AddFile;

public class AddFileRequest()
{
    public string FileName { get; init; }
    public string? MimeType { get; init; }
    public string NzbFileContents { get; init; }
    public string Category { get; init; }
    public QueueItem.PriorityOption Priority { get; init; }
    public QueueItem.PostProcessingOption PostProcessing { get; init; }
    public DateTime? PauseUntil { get; init; }
    public CancellationToken CancellationToken { get; init; }

    public static async Task<AddFileRequest> New(HttpContext context, ConfigManager configManager)
    {
        var file =
            context.Request.Form.Files["nzbFile"] ??
            context.Request.Form.Files["name"] ??
            throw new BadHttpRequestException("Invalid nzbFile/name param");

        using var streamReader = new StreamReader(file.OpenReadStream());

        return new AddFileRequest()
        {
            FileName = file.FileName,
            MimeType = file.ContentType,
            NzbFileContents = await streamReader.ReadToEndAsync(context.RequestAborted).ConfigureAwait(false),
            Category = context.GetQueryParam("cat") ?? configManager.GetManualUploadCategory(),
            Priority = MapPriorityOption(context.GetQueryParam("priority")),
            PostProcessing = MapPostProcessingOption(context.GetQueryParam("pp")),
            CancellationToken = context.RequestAborted
        };
    }

    protected static QueueItem.PriorityOption MapPriorityOption(string? priority)
    {
        return priority switch
        {
            "-100" => QueueItem.PriorityOption.Normal,
            "-3" => QueueItem.PriorityOption.Duplicate,
            "-2" => QueueItem.PriorityOption.Paused,
            "-1" => QueueItem.PriorityOption.Low,
            "0" => QueueItem.PriorityOption.Normal,
            "1" => QueueItem.PriorityOption.High,
            "2" => QueueItem.PriorityOption.Force,
            null => QueueItem.PriorityOption.Normal,
            _ => throw new BadHttpRequestException("Invalid priority")
        };
    }

    protected static QueueItem.PostProcessingOption MapPostProcessingOption(string? postProcessing)
    {
        return postProcessing switch
        {
            "-1" => QueueItem.PostProcessingOption.None,
            "0" => QueueItem.PostProcessingOption.None,
            "1" => QueueItem.PostProcessingOption.Repair,
            "2" => QueueItem.PostProcessingOption.RepairUnpack,
            "3" => QueueItem.PostProcessingOption.RepairUnpackDelete,
            null => QueueItem.PostProcessingOption.None,
            _ => throw new BadHttpRequestException("Invalid pp param")
        };
    }
}