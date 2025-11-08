using Microsoft.EntityFrameworkCore;
using NzbWebDAV.Api.Controllers.GetWebdavItem;
using NzbWebDAV.Config;
using NzbWebDAV.Database;
using NzbWebDAV.Database.Models;
using NzbWebDAV.Utils;
using NzbWebDAV.WebDav;
using Serilog;

namespace NzbWebDAV.Queue.PostProcessors;

public class CreateStrmFilesPostProcessor(ConfigManager configManager, DavDatabaseClient dbClient)
{
    public void CreateStrmFiles()
    {
        // Add strm files to the download dir
        var videoItems = dbClient.Ctx.ChangeTracker.Entries<DavItem>()
            .Where(x => x.State == EntityState.Added)
            .Select(x => x.Entity)
            .Where(x => x.Type != DavItem.ItemType.Directory)
            .Where(x => FilenameUtil.IsVideoFile(x.Name));
        foreach (var videoItem in videoItems)
            CreateStrmFile(videoItem);
    }

    private void CreateStrmFile(DavItem davItem)
    {
        // create necessary directories if they don't already exist
        var strmFilePath = GetStrmFilePath(davItem);
        var directoryName = Path.GetDirectoryName(strmFilePath);
        if (directoryName != null) Directory.CreateDirectory(directoryName);

        // create the strm file
        var targetUrl = GetStrmTargetUrl(davItem);
        File.WriteAllText(strmFilePath, targetUrl);
    }

    private string GetStrmFilePath(DavItem davItem)
    {
        var path = davItem.Path + ".strm";
        var parts = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return Path.Join(configManager.GetStrmCompletedDownloadDir(), Path.Join(parts[2..]));
    }

    private string GetStrmTargetUrl(DavItem davItem)
    {
        var baseUrl = configManager.GetBaseUrl();
        if (baseUrl.EndsWith('/')) baseUrl = baseUrl.TrimEnd('/');
        var pathUrl = DatabaseStoreSymlinkFile.GetTargetPath(davItem, "", '/');
        if (pathUrl.StartsWith('/')) pathUrl = pathUrl.TrimStart('/');
        var strmKey = configManager.GetStrmKey();
        var downloadKey = GetWebdavItemRequest.GenerateDownloadKey(strmKey, pathUrl);
        return $"{baseUrl}/view/{pathUrl}?downloadKey={downloadKey}";
    }
}