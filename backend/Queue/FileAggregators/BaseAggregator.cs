using NzbWebDAV.Database;
using NzbWebDAV.Database.Models;
using NzbWebDAV.Queue.FileProcessors;

namespace NzbWebDAV.Queue.FileAggregators;

public abstract class BaseAggregator
{
    public abstract void UpdateDatabase(List<BaseProcessor.Result> processorResults);
    protected abstract DavDatabaseClient DBClient { get; }
    protected abstract DavItem MountDirectory { get; }

    protected DavItem EnsureExtractPath(string pathWithinArchive)
    {
        var pathSegments = pathWithinArchive
            .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .ToArray();
        var parentDirectory = MountDirectory;
        var pathKey = "";
        for (var i = 0; i < pathSegments.Length - 1; i++)
        {
            pathKey = Path.Join(pathKey, pathSegments[i]);
            parentDirectory = EnsureDirectory(parentDirectory, pathSegments[i], pathKey);
        }

        return parentDirectory;
    }

    private readonly Dictionary<string, DavItem> _directoryCache = new();

    protected DavItem EnsureDirectory(DavItem parentDirectory, string directoryName, string pathKey)
    {
        if (_directoryCache.TryGetValue(pathKey, out var cachedDirectory)) return cachedDirectory;

        var directory = DavItem.New(
            id: Guid.NewGuid(),
            parent: parentDirectory,
            name: directoryName,
            fileSize: null,
            type: DavItem.ItemType.Directory,
            releaseDate: null,
            lastHealthCheck: null
        );
        _directoryCache.Add(pathKey, directory);
        DBClient.Ctx.Items.Add(directory);
        return directory;
    }
}