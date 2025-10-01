using NzbWebDAV.Database;
using NzbWebDAV.Database.Models;
using NzbWebDAV.Extensions;
using NzbWebDAV.Queue.FileProcessors;
using NzbWebDAV.Utils;

namespace NzbWebDAV.Queue.FileAggregators;

public class RarAggregator(DavDatabaseClient dbClient, DavItem mountDirectory) : BaseAggregator
{
    protected override DavDatabaseClient DBClient => dbClient;
    protected override DavItem MountDirectory => mountDirectory;

    public override void UpdateDatabase(List<BaseProcessor.Result> processorResults)
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
                    archiveFiles.Add(fileSegment.PathWithinArchive, []);

                archiveFiles[fileSegment.PathWithinArchive].Add(new DavRarFile.RarPart()
                {
                    SegmentIds = archivePart.NzbFile.GetSegmentIds(),
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
            var parentDirectory = EnsureExtractPath(pathWithinArchive);
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
}