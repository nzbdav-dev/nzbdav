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
public class GetWebdavItemController(DatabaseStore store, ConfigManager configManager) : ControllerBase
{
    private static readonly FileExtensionContentTypeProvider MimeTypeProvider = new();

    private async Task<Stream> GetWebdavItem(GetWebdavItemRequest request)
    {
        var item = await store.GetItemAsync(request.Item, HttpContext.RequestAborted).ConfigureAwait(false);
        if (item is null) throw new BadHttpRequestException("The file does not exist.");
        if (item is IStoreCollection) throw new BadHttpRequestException("The file does not exist.");

        // disable compression to keep Content-Length intact for clients that need seeking
        Response.Headers["Content-Encoding"] = "identity";

        // handle par2 preview
        if (Path.GetExtension(item.Name).ToLower() == ".par2" && configManager.IsPreviewPar2FilesEnabled())
            return await GetPar2PreviewStream(item).ConfigureAwait(false);

        // get the file stream and set the file-size in header
        var stream = await item.GetReadableStreamAsync(HttpContext.RequestAborted).ConfigureAwait(false);
        var fileSize = stream.Length;

        // set the content-type and content-disposition headers
        Response.Headers["Content-Type"] = GetContentType(item.Name);
        Response.Headers["Content-Disposition"] = GetContentDisposition(item.Name);

        // disable compression to keep Content-Length intact for clients that need seeking
        Response.Headers["Content-Encoding"] = "identity";
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
            HttpContext.Items["configManager"] = configManager;
            var request = new GetWebdavItemRequest(HttpContext);
            await using var response = await GetWebdavItem(request).ConfigureAwait(false);
            await response.CopyToAsync(Response.Body, bufferSize: 1024, HttpContext.RequestAborted).ConfigureAwait(false);
        }
        catch (UnauthorizedAccessException)
        {
            Response.StatusCode = 401;
        }
    }

    [HttpHead]
    public async Task HandleHeadRequest()
    {
        try
        {
            HttpContext.Items["configManager"] = configManager;
            var request = new GetWebdavItemRequest(HttpContext);
            await using var response = await GetWebdavItem(request).ConfigureAwait(false);
            // HEAD: headers already set, body omitted
        }
        catch (UnauthorizedAccessException)
        {
            Response.StatusCode = 401;
        }
    }

    private static string GetContentType(string item)
    {
        if (item == "README") return "text/plain";
        var extension = Path.GetExtension(item).ToLower();
        return extension == ".mkv" ? "video/webm"
            : extension == ".rclonelink" ? "text/plain"
            : extension == ".nfo" ? "text/plain"
            : MimeTypeProvider.TryGetContentType(Path.GetFileName(item), out var mimeType) ? mimeType
            : "application/octet-stream";
    }

    private static string GetContentDisposition(string filename)
    {
        // Remove control characters (header safety)
        filename = new string(filename.Where(c => !char.IsControl(c)).ToArray());

        // ASCII fallback for legacy clients
        var chars = filename.Select(c => (c >= 32 && c <= 126 && c != '"' && c != '\\' && c != ';') ? c : '_');
        var ascii = new string(chars.ToArray());

        // RFC 5987 UTF-8 filename
        var utf8 = Uri.EscapeDataString(filename);

        return $"inline; filename=\"{ascii}\"; filename*=UTF-8''{utf8}";
    }

    private async Task<Stream> GetPar2PreviewStream(IStoreItem item)
    {
        Response.Headers.ContentType = "text/plain";
        await using var stream = await item.GetReadableStreamAsync(HttpContext.RequestAborted).ConfigureAwait(false);
        var fileDescriptors = await Par2.ReadFileDescriptions(stream, HttpContext.RequestAborted).GetAllAsync()
            .ConfigureAwait(false);
        return new MemoryStream(Encoding.UTF8.GetBytes(fileDescriptors.ToIndentedJson()));
    }
}