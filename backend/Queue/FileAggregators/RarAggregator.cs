using NzbWebDAV.Database;
using NzbWebDAV.Database.Models;
using NzbWebDAV.Queue.FileProcessors;
using NzbWebDAV.Utils;

namespace NzbWebDAV.Queue.FileAggregators;

public class RarAggregator(DavDatabaseClient dbClient, DavItem mountDirectory) : IAggregator
{
    public void UpdateDatabase(List<BaseProcessor.Result> processorResults)
    {
        var orderedArchiveParts = processorResults
            .OfType<RarProcessor.Result>()
            .OrderBy(x => x.PartNumber)
            .ToList();

        ProcessArchive(orderedArchiveParts);
    }

    private void ProcessArchive(List<RarProcessor.Result> archiveParts)
    {
        var archiveFiles = new Dictionary<string, List<DavRarFile.RarPart>>();
        foreach (var archivePart in archiveParts)
        {
            foreach (var fileSegment in archivePart.StoredFileSegments)
            {
                if (!archiveFiles.ContainsKey(fileSegment.PathWithinArchive))
                    archiveFiles.Add(fileSegment.PathWithinArchive, new List<DavRarFile.RarPart>());

                archiveFiles[fileSegment.PathWithinArchive].Add(new DavRarFile.RarPart()
                {
                    SegmentIds = archivePart.NzbFile.Segments.Select(x => x.MessageId.Value).ToArray(),
                    PartSize = archivePart.PartSize,
                    Offset = fileSegment.Offset,
                    ByteCount = fileSegment.ByteCount,
                });
            }
        }

        foreach (var archiveFile in archiveFiles)
        {
            var pathWithinArchive = archiveFile.Key;
            var rarParts = archiveFile.Value.ToArray();
            var parentDirectory = EnsurePath(pathWithinArchive);
            var name = Path.GetFileName(pathWithinArchive);

            // If there is only one file in the archive and the file-name is obfuscated,
            // then rename the file to the same name as the containing mount directory.
            if (archiveFiles.Count == 1 && ObfuscationUtil.IsProbablyObfuscated(name))
                name = mountDirectory.Name + Path.GetExtension(name);

            var davItem = DavItem.New(
                id: Guid.NewGuid(),
                parent: parentDirectory,
                name: name,
                fileSize: rarParts.Sum(x => x.ByteCount),
                type: DavItem.ItemType.RarFile
            );

            var davRarFile = new DavRarFile()
            {
                Id = davItem.Id,
                RarParts = rarParts,
            };

            dbClient.Ctx.Items.Add(davItem);
            dbClient.Ctx.RarFiles.Add(davRarFile);
        }
    }

    private DavItem EnsurePath(string pathWithinArchive)
    {
        var pathSegments = pathWithinArchive
            .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Prepend("extracted")
            .ToArray();
        var parentDirectory = mountDirectory;
        var pathKey = "";
        for (var i = 0; i < pathSegments.Length - 1; i++)
        {
            pathKey = Path.Join(pathKey, pathSegments[i]);
            parentDirectory = EnsureDirectory(parentDirectory, pathSegments[i], pathKey);
        }

        return parentDirectory;
    }

    private readonly Dictionary<string, DavItem> _directoryCache = new();

    private DavItem EnsureDirectory(DavItem parentDirectory, string directoryName, string pathKey)
    {
        if (_directoryCache.TryGetValue(pathKey, out var cachedDirectory)) return cachedDirectory;

        var directory = DavItem.New(
            id: Guid.NewGuid(),
            parent: parentDirectory,
            name: directoryName,
            fileSize: null,
            type: DavItem.ItemType.Directory
        );
        _directoryCache.Add(pathKey, directory);
        dbClient.Ctx.Items.Add(directory);
        return directory;
    }
}