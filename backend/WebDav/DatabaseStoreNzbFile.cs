using Microsoft.AspNetCore.Http;
using NzbWebDAV.Clients.Usenet;
using NzbWebDAV.Config;
using NzbWebDAV.Database;
using NzbWebDAV.Database.Models;
using NzbWebDAV.Streams;
using NzbWebDAV.WebDav.Base;

namespace NzbWebDAV.WebDav;

public class DatabaseStoreNzbFile(
    DavItem davNzbFile,
    HttpContext httpContext,
    DavDatabaseClient dbClient,
    INntpClient usenetClient,
    ConfigManager configManager
) : BaseStoreStreamFile(httpContext)
{
    public DavItem DavItem => davNzbFile;
    public override string Name => davNzbFile.Name;
    public override string UniqueKey => davNzbFile.Id.ToString();
    public override long FileSize => davNzbFile.FileSize!.Value;
    public override DateTime CreatedAt => davNzbFile.CreatedAt;
    public override Guid? NzbBlobId => davNzbFile.NzbBlobId;

    protected override async Task<Stream> GetStreamAsync(CancellationToken cancellationToken)
    {
        // store the DavItem being accessed in the http context
        httpContext.Items["DavItem"] = davNzbFile;

        var id = davNzbFile.Id;
        var file = await dbClient.GetDavNzbFileAsync(davNzbFile, cancellationToken).ConfigureAwait(false);
        if (file is null) throw new FileNotFoundException($"Could not find nzb file with id: {id}");
        return GetStream(file);
    }

    private NzbFileStream GetStream(DavNzbFile nzbFile)
    {
        return usenetClient.GetFileStream(nzbFile.SegmentIds, FileSize, configManager.GetArticleBufferSize());
    }
}