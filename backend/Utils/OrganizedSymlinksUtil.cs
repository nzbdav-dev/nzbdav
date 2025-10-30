using NzbWebDAV.Config;
using NzbWebDAV.Database.Models;
using NzbWebDAV.Extensions;

namespace NzbWebDAV.Utils;

public static class OrganizedSymlinksUtil
{
    private static readonly Dictionary<Guid, string> Cache = new();

    /// <summary>
    /// Searches organized media library for a symlink pointing to the given target
    /// </summary>
    /// <param name="targetDavItem">The given target</param>
    /// <param name="configManager">The application config</param>
    /// <returns>The path to a symlink in the organized media library that points to the given target.</returns>
    public static string? GetSymlink(DavItem targetDavItem, ConfigManager configManager)
    {
        return !TryGetSymlinkFromCache(targetDavItem, configManager, out var symlinkFromCache)
            ? SearchForSymlink(targetDavItem, configManager)
            : symlinkFromCache;
    }

    /// <summary>
    /// Enumerates all symlinks within the organized media library that point to nzbdav dav-items.
    /// </summary>
    /// <param name="configManager">The application config</param>
    /// <returns>All symlinks within the organized media library that point to nzbdav dav-items.</returns>
    public static IEnumerable<DavItemSymlink> GetLibrarySymlinkTargets(ConfigManager configManager)
    {
        var libraryRoot = configManager.GetLibraryDir()!;
        var allSymlinks = SymlinkUtil.GetAllSymlinks(libraryRoot);
        return GetDavItemSymlinks(allSymlinks, configManager);
    }

    private static bool TryGetSymlinkFromCache
    (
        DavItem targetDavItem,
        ConfigManager configManager,
        out string? symlink
    )
    {
        return Cache.TryGetValue(targetDavItem.Id, out symlink)
               && Verify(symlink, targetDavItem, configManager);
    }

    private static bool Verify(string symlink, DavItem targetDavItem, ConfigManager configManager)
    {
        var fileInfo = new FileInfo(symlink);
        var symlinkInfo = new SymlinkUtil.SymlinkInfo()
        {
            SymlinkPath = symlink,
            TargetPath = fileInfo.LinkTarget ?? "",
        };
        return GetDavItemSymlinks([symlinkInfo], configManager)
            .Select(x => x.DavItemId)
            .FirstOrDefault() == targetDavItem.Id;
    }

    private static string? SearchForSymlink(DavItem targetDavItem, ConfigManager configManager)
    {
        string? result = null;
        foreach (var symlink in GetLibrarySymlinkTargets(configManager))
        {
            Cache[targetDavItem.Id] = symlink.SymlinkPath;
            if (symlink.DavItemId == targetDavItem.Id)
                result = symlink.SymlinkPath;
        }

        return result;
    }

    private static IEnumerable<DavItemSymlink> GetDavItemSymlinks
    (
        IEnumerable<SymlinkUtil.SymlinkInfo> symlinkInfos,
        ConfigManager configManager
    )
    {
        var mountDir = configManager.GetRcloneMountDir();
        return symlinkInfos
            .Where(x => x.TargetPath.StartsWith(mountDir))
            .Select(x => x with { TargetPath = x.TargetPath.RemovePrefix(mountDir) })
            .Select(x => x with { TargetPath = x.TargetPath.StartsWith('/') ? x.TargetPath : $"/{x.TargetPath}" })
            .Where(x => x.TargetPath.StartsWith("/.ids"))
            .Select(x => x with { TargetPath = Path.GetFileNameWithoutExtension(x.TargetPath) })
            .Select(x => new DavItemSymlink() { SymlinkPath = x.SymlinkPath, DavItemId = Guid.Parse(x.TargetPath) });
    }

    public struct DavItemSymlink
    {
        public string SymlinkPath;
        public Guid DavItemId;
    }
}