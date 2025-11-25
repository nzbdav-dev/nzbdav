using Microsoft.EntityFrameworkCore;
using NzbWebDAV.Api.Controllers.GetWebdavItem;
using NzbWebDAV.Config;
using NzbWebDAV.Database;
using NzbWebDAV.Database.Models;
using NzbWebDAV.Utils;
using NzbWebDAV.WebDav;

namespace NzbWebDAV.Queue.PostProcessors;

public class CreateStrmFilesPostProcessor(ConfigManager configManager, DavDatabaseClient dbClient)
{
    public async Task CreateStrmFilesAsync()
    {
        // Add strm files to the download dir
        var videoItems = dbClient.Ctx.ChangeTracker.Entries<DavItem>()
            .Where(x => x.State == EntityState.Added)
            .Select(x => x.Entity)
            .Where(x => x.Type != DavItem.ItemType.Directory)
            .Where(x => FilenameUtil.IsVideoFile(x.Name));
        foreach (var videoItem in videoItems)
            await CreateStrmFileAsync(videoItem).ConfigureAwait(false);
    }

    private async Task CreateStrmFileAsync(DavItem davItem)
    {
        // create necessary directories if they don't already exist
        var strmFilePath = GetStrmFilePath(davItem);
        var directoryName = Path.GetDirectoryName(strmFilePath);
        if (directoryName != null)
            await Task.Run(() => Directory.CreateDirectory(directoryName)).ConfigureAwait(false);

        // create the strm file
        var targetUrl = GetStrmTargetUrl(davItem);
        await File.WriteAllTextAsync(strmFilePath, targetUrl).ConfigureAwait(false);
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
        var pathUrl = DatabaseStoreSymlinkFile.GetTargetPath(davItem.Id, "", '/');
        if (pathUrl.StartsWith('/')) pathUrl = pathUrl.TrimStart('/');
        var strmKey = configManager.GetStrmKey();
        var downloadKey = GetWebdavItemRequest.GenerateDownloadKey(strmKey, pathUrl);
        var extension = Path.GetExtension(davItem.Name).ToLower().TrimStart('.');
        return $"{baseUrl}/view/{pathUrl}?downloadKey={downloadKey}&extension={extension}";
    }
}