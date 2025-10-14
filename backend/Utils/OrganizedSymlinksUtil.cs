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
    public static IEnumerable<(FileInfo FileInfo, Guid Target)> GetLibrarySymlinkTargets(ConfigManager configManager)
    {
        var libraryRoot = configManager.GetLibraryDir()!;
        var allSymlinkPaths = Directory.EnumerateFileSystemEntries(libraryRoot, "*", SearchOption.AllDirectories);
        return GetSymlinkTargets(allSymlinkPaths, configManager);
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
        return GetSymlinkTargets([symlink], configManager)
            .Select(x => x.Target)
            .FirstOrDefault() == targetDavItem.Id;
    }

    private static string? SearchForSymlink(DavItem targetDavItem, ConfigManager configManager)
    {
        string? result = null;
        foreach (var symlink in GetLibrarySymlinkTargets(configManager))
        {
            Cache[targetDavItem.Id] = symlink.FileInfo.FullName;
            if (symlink.Target == targetDavItem.Id)
                result = symlink.FileInfo.FullName;
        }

        return result;
    }

    private static IEnumerable<(FileInfo FileInfo, Guid Target)> GetSymlinkTargets
    (
        IEnumerable<string> symlinkPaths,
        ConfigManager configManager
    )
    {
        var mountDir = configManager.GetRcloneMountDir();
        return symlinkPaths
            .Select(x => new FileInfo(x))
            .Where(x => x.Attributes.HasFlag(FileAttributes.ReparsePoint))
            .Select(x => (FileInfo: x, Target: x.LinkTarget))
            .Where(x => x.Target is not null)
            .Select(x => (x.FileInfo, Target: x.Target!))
            .Where(x => x.Target.StartsWith(mountDir))
            .Select(x => (x.FileInfo, Target: x.Target.RemovePrefix(mountDir)))
            .Select(x => (x.FileInfo, Target: x.Target.StartsWith("/") ? x.Target : $"/{x.Target}"))
            .Where(x => x.Target.StartsWith("/.ids"))
            .Select(x => (x.FileInfo, Target: Path.GetFileNameWithoutExtension(x.Target)))
            .Select(x => (x.FileInfo, Target: Guid.Parse(x.Target)));
    }
}