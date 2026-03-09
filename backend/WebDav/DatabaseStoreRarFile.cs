using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using NzbWebDAV.Clients.Usenet;
using NzbWebDAV.Config;
using NzbWebDAV.Database;
using NzbWebDAV.Database.Models;
using NzbWebDAV.Streams;
using NzbWebDAV.WebDav.Base;

namespace NzbWebDAV.WebDav;

public class DatabaseStoreRarFile(
    DavItem davRarFile,
    HttpContext httpContext,
    DavDatabaseClient dbClient,
    UsenetStreamingClient usenetClient,
    ConfigManager configManager
) : BaseStoreStreamFile(httpContext)
{
    public DavItem DavItem => davRarFile;
    public override string Name => davRarFile.Name;
    public override string UniqueKey => davRarFile.Id.ToString();
    public override long FileSize => davRarFile.FileSize!.Value;
    public override DateTime CreatedAt => davRarFile.CreatedAt;

    protected override async Task<Stream> GetStreamAsync(CancellationToken ct)
    {
        // store the DavItem being accessed in the http context
        httpContext.Items["DavItem"] = davRarFile;

        var id = davRarFile.Id;
        var rarFile = await dbClient.GetDavRarFileAsync(davRarFile, ct).ConfigureAwait(false);
        if (rarFile is null) throw new FileNotFoundException($"Could not find nzb file with id: {id}");
        return GetStream(rarFile);
    }

    private DavMultipartFileStream GetStream(DavRarFile rarFile)
    {
        return new DavMultipartFileStream
        (
            rarFile.ToDavMultipartFileMeta().FileParts,
            usenetClient,
            configManager.GetArticleBufferSize()
        );
    }
}