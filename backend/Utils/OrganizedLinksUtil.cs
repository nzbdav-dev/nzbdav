using NzbWebDAV.Config;
using NzbWebDAV.Database.Models;
using NzbWebDAV.Extensions;

namespace NzbWebDAV.Utils;

/// <summary>
/// Note: In this class, a `Link` refers to either a symlink or strm file.
/// </summary>
public static class OrganizedLinksUtil
{
    private static readonly Dictionary<Guid, string> Cache = new();

    /// <summary>
    /// Searches organized media library for a symlink or strm pointing to the given target
    /// </summary>
    /// <param name="targetDavItem">The given target</param>
    /// <param name="configManager">The application config</param>
    /// <returns>The path to a symlink or strm in the organized media library that points to the given target.</returns>
    public static string? GetLink(DavItem targetDavItem, ConfigManager configManager)
    {
        return !TryGetLinkFromCache(targetDavItem, configManager, out var linkFromCache)
            ? SearchForLink(targetDavItem, configManager)
            : linkFromCache;
    }

    /// <summary>
    /// Enumerates all DavItemLinks within the organized media library that point to nzbdav dav-items.
    /// </summary>
    /// <param name="configManager">The application config</param>
    /// <returns>All DavItemLinks within the organized media library that point to nzbdav dav-items.</returns>
    public static IEnumerable<DavItemLink> GetLibraryDavItemLinks(ConfigManager configManager)
    {
        var libraryRoot = configManager.GetLibraryDir()!;
        var allSymlinksAndStrms = SymlinkAndStrmUtil.GetAllSymlinksAndStrms(libraryRoot);
        return GetDavItemLinks(allSymlinksAndStrms, configManager);
    }

    private static bool TryGetLinkFromCache
    (
        DavItem targetDavItem,
        ConfigManager configManager,
        out string? linkFromCache
    )
    {
        return Cache.TryGetValue(targetDavItem.Id, out linkFromCache)
               && Verify(linkFromCache, targetDavItem, configManager);
    }

    private static bool Verify(string linkFromCache, DavItem targetDavItem, ConfigManager configManager)
    {
        var mountDir = configManager.GetRcloneMountDir();
        var fileInfo = new FileInfo(linkFromCache);
        var symlinkOrStrmInfo = SymlinkAndStrmUtil.GetSymlinkOrStrmInfo(fileInfo);
        if (symlinkOrStrmInfo == null) return false;
        var davItemLink = GetDavItemLink(symlinkOrStrmInfo, mountDir);
        return davItemLink?.DavItemId == targetDavItem.Id;
    }

    private static string? SearchForLink(DavItem targetDavItem, ConfigManager configManager)
    {
        string? result = null;
        foreach (var davItemLink in GetLibraryDavItemLinks(configManager))
        {
            Cache[targetDavItem.Id] = davItemLink.LinkPath;
            if (davItemLink.DavItemId == targetDavItem.Id)
                result = davItemLink.LinkPath;
        }

        return result;
    }

    private static IEnumerable<DavItemLink> GetDavItemLinks
    (
        IEnumerable<SymlinkAndStrmUtil.ISymlinkOrStrmInfo> symlinkOrStrmInfos,
        ConfigManager configManager
    )
    {
        var mountDir = configManager.GetRcloneMountDir();
        return symlinkOrStrmInfos
            .Select(x => GetDavItemLink(x, mountDir))
            .Where(x => x != null)
            .Select(x => x!.Value);
    }

    private static DavItemLink? GetDavItemLink
    (
        SymlinkAndStrmUtil.ISymlinkOrStrmInfo symlinkOrStrmInfo,
        string mountDir
    )
    {
        return symlinkOrStrmInfo switch
        {
            SymlinkAndStrmUtil.SymlinkInfo symlinkInfo => GetDavItemLink(symlinkInfo, mountDir),
            SymlinkAndStrmUtil.StrmInfo strmInfo => GetDavItemLink(strmInfo),
            _ => throw new Exception("Unknown link type")
        };
    }

    private static DavItemLink? GetDavItemLink(SymlinkAndStrmUtil.SymlinkInfo symlinkInfo, string mountDir)
    {
        var targetPath = symlinkInfo.TargetPath;
        if (!targetPath.StartsWith(mountDir)) return null;
        targetPath = targetPath.RemovePrefix(mountDir);
        targetPath = targetPath.StartsWith('/') ? targetPath : $"/{targetPath}";
        if (!targetPath.StartsWith("/.ids")) return null;
        var guid = Path.GetFileNameWithoutExtension(targetPath);
        return new DavItemLink()
        {
            LinkPath = symlinkInfo.SymlinkPath,
            DavItemId = Guid.Parse(guid),
            SymlinkOrStrmInfo = symlinkInfo
        };
    }

    private static DavItemLink? GetDavItemLink(SymlinkAndStrmUtil.StrmInfo strmInfo)
    {
        var targetUrl = strmInfo.TargetUrl;
        var absolutePath = new Uri(targetUrl).AbsolutePath;
        if (!absolutePath.StartsWith("/view/.ids")) return null;
        var guid = Path.GetFileNameWithoutExtension(absolutePath);
        return new DavItemLink()
        {
            LinkPath = strmInfo.StrmPath,
            DavItemId = Guid.Parse(guid),
            SymlinkOrStrmInfo = strmInfo
        };
    }

    public struct DavItemLink
    {
        public string LinkPath; // Path to either a symlink or strm file.
        public Guid DavItemId;
        public SymlinkAndStrmUtil.ISymlinkOrStrmInfo SymlinkOrStrmInfo;
    }
}