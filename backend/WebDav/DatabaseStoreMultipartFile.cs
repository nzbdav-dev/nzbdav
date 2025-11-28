using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using NzbWebDAV.Clients.Usenet;
using NzbWebDAV.Clients.Usenet.Connections;
using NzbWebDAV.Config;
using NzbWebDAV.Database;
using NzbWebDAV.Database.Models;
using NzbWebDAV.Extensions;
using NzbWebDAV.Streams;
using NzbWebDAV.WebDav.Base;

namespace NzbWebDAV.WebDav;

public class DatabaseStoreMultipartFile(
    DavItem davMultipartFile,
    HttpContext httpContext,
    DavDatabaseClient dbClient,
    UsenetStreamingClient usenetClient,
    ConfigManager configManager
) : BaseStoreStreamFile
{
    public DavItem DavItem => davMultipartFile;
    public override string Name => davMultipartFile.Name;
    public override string UniqueKey => davMultipartFile.Id.ToString();
    public override long FileSize => davMultipartFile.FileSize!.Value;
    public override DateTime CreatedAt => davMultipartFile.CreatedAt;

    public override async Task<Stream> GetStreamAsync(CancellationToken ct)
    {
        // store the DavItem being accessed in the http context
        httpContext.Items["DavItem"] = davMultipartFile;

        // create streaming usage context
        var usageContext = new ConnectionUsageContext(
            ConnectionUsageType.Streaming,
            davMultipartFile.Path
        );

        // return the stream
        var id = davMultipartFile.Id;
        var multipartFile = await dbClient.Ctx.MultipartFiles.Where(x => x.Id == id).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (multipartFile is null) throw new FileNotFoundException($"Could not find nzb file with id: {id}");
        var packedStream = new DavMultipartFileStream(
            multipartFile.Metadata.FileParts,
            usenetClient,
            configManager.GetConnectionsPerStream(),
            usageContext
        );
        return multipartFile.Metadata.AesParams != null
            ? new AesDecoderStream(packedStream, multipartFile.Metadata.AesParams)
            : packedStream;
    }
}