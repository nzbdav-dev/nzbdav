using Microsoft.AspNetCore.Http;
using NzbWebDAV.Clients.Usenet;
using NzbWebDAV.Config;
using NzbWebDAV.Database;
using NzbWebDAV.Database.Models;
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

    protected override async Task<Stream> GetStreamAsync(CancellationToken cancellationToken)
    {
        // store the DavItem being accessed in the http context
        httpContext.Items["DavItem"] = davNzbFile;

        // return the stream
        var id = davNzbFile.Id;
        var file = await dbClient.GetNzbFileAsync(id, cancellationToken).ConfigureAwait(false);
        if (file is null) throw new FileNotFoundException($"Could not find nzb file with id: {id}");
        return usenetClient.GetFileStream(file.SegmentIds, FileSize, configManager.GetMaxDownloadConnections());
    }
}