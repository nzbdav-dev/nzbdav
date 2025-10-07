using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;
using NWebDav.Server.Stores;
using NzbWebDAV.Config;
using NzbWebDAV.Extensions;
using NzbWebDAV.Par2Recovery;
using NzbWebDAV.WebDav;

namespace NzbWebDAV.Api.Controllers.GetWebdavItem;

[ApiController]
[Route("view/{*path}")]
public class ListWebdavDirectoryController(DatabaseStore store, ConfigManager configManager) : ControllerBase
{
    private static readonly FileExtensionContentTypeProvider MimeTypeProvider = new();

    private async Task<Stream> GetWebdavItem(GetWebdavItemRequest request)
    {
        var item = await store.GetItemAsync(request.Item, HttpContext.RequestAborted);
        if (item is null) throw new BadHttpRequestException("The file does not exist.");
        if (item is IStoreCollection) throw new BadHttpRequestException("The file does not exist.");

        // handle par2 preview
        if (Path.GetExtension(item.Name).ToLower() == ".par2" && configManager.IsPreviewPar2FilesEnabled())
            return await GetPar2PreviewStream(item);

        // get the file stream and set the file-size in header
        var stream = await item.GetReadableStreamAsync(HttpContext.RequestAborted);
        var fileSize = stream.Length;

        // set the content-typ header
        Response.Headers["Content-Type"] = GetContentType(item.Name);
        Response.Headers["Accept-Ranges"] = "bytes";

        if (request.RangeStart is not null)
        {
            // compute
            var end = request.RangeEnd ?? (fileSize - 1);
            var chunkSize = 1 + end - request.RangeStart!.Value;

            // seek
            stream.Seek(request.RangeStart.Value, SeekOrigin.Begin);
            if (request.RangeEnd is not null) stream = stream.LimitLength(chunkSize);

            // set response headers
            Response.Headers["Content-Range"] = $"bytes {request.RangeStart}-{end}/{fileSize}";
            Response.Headers["Content-Length"] = chunkSize.ToString();
            Response.StatusCode = 206;
        }
        else
        {
            Response.Headers["Content-Length"] = fileSize.ToString();
        }

        return stream;
    }

    [HttpGet]
    public async Task HandleRequest()
    {
        try
        {
            var request = new GetWebdavItemRequest(HttpContext);
            await using var response = await GetWebdavItem(request);
            await response.CopyToAsync(Response.Body, bufferSize: 1024, HttpContext.RequestAborted);
        }
        catch (UnauthorizedAccessException)
        {
            Response.StatusCode = 401;
        }
    }

    private static string GetContentType(string item)
    {
        var extension = Path.GetExtension(item).ToLower();
        return extension == ".mkv" ? "video/webm"
            : extension == ".rclonelink" ? "text/plain"
            : extension == ".nfo" ? "text/plain"
            : MimeTypeProvider.TryGetContentType(Path.GetFileName(item), out var mimeType) ? mimeType
            : "application/octet-stream";
    }

    private async Task<Stream> GetPar2PreviewStream(IStoreItem item)
    {
        Response.Headers.ContentType = "text/plain";
        await using var stream = await item.GetReadableStreamAsync(HttpContext.RequestAborted);
        var fileDescriptors = await Par2.ReadFileDescriptions(stream, HttpContext.RequestAborted).GetAllAsync();
        return new MemoryStream(Encoding.UTF8.GetBytes(fileDescriptors.ToIndentedJson()));
    }
}