using NzbWebDAV.Api.Controllers.GetWebdavItem;
using NzbWebDAV.Config;
using NzbWebDAV.Database.Models;
using NzbWebDAV.WebDav;

namespace NzbWebDAV.Utils;

public static class StrmFileUtil
{

    public static async Task CreateStrmFileAsync(ConfigManager configManager, DavItem davItem)
    {
        // create necessary directories if they don't already exist
        var strmFilePath = GetStrmFilePath(configManager, davItem);
        var directoryName = Path.GetDirectoryName(strmFilePath);
        if (directoryName != null)
            await Task.Run(() => Directory.CreateDirectory(directoryName)).ConfigureAwait(false);

        // create the strm file
        var targetUrl = GetStrmTargetUrl(configManager, davItem);
        await File.WriteAllTextAsync(strmFilePath, targetUrl).ConfigureAwait(false);
    }

    public static string GetStrmFilePath(ConfigManager configManager, DavItem davItem)
    {
        var path = davItem.Path + ".strm";
        var parts = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return Path.Join(configManager.GetStrmCompletedDownloadDir(), Path.Join(parts[2..]));
    }

    public static string GetStrmTargetUrl(ConfigManager configManager, DavItem davItem)
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