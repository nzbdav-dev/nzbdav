using NzbWebDAV.Database;
using NzbWebDAV.Database.Models;
using NzbWebDAV.Extensions;
using NzbWebDAV.Models;
using NzbWebDAV.Queue.FileProcessors;
using NzbWebDAV.Utils;

namespace NzbWebDAV.Queue.FileAggregators;

public class RarAggregator(DavDatabaseClient dbClient, DavItem mountDirectory, bool checkedFullHealth) : BaseAggregator
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
        var archiveFiles = new Dictionary<string, List<DavMultipartFile.FilePart>>();
        foreach (var archivePart in archiveParts)
        {
            foreach (var fileSegment in archivePart.StoredFileSegments)
            {
                if (!archiveFiles.ContainsKey(fileSegment.PathWithinArchive))
                    archiveFiles.Add(fileSegment.PathWithinArchive, []);

                archiveFiles[fileSegment.PathWithinArchive].Add(new DavMultipartFile.FilePart()
                {
                    SegmentIds = archivePart.NzbFile.GetSegmentIds(),
                    SegmentIdByteRange = LongRange.FromStartAndSize(0, archivePart.PartSize),
                    FilePartByteRange = LongRange.FromStartAndSize(fileSegment.Offset, fileSegment.ByteCount),
                });
            }
        }

        foreach (var archiveFile in archiveFiles)
        {
            var pathWithinArchive = archiveFile.Key;
            var fileParts = archiveFile.Value.ToArray();
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
                fileSize: fileParts.Sum(x => x.FilePartByteRange.Count),
                type: DavItem.ItemType.MultipartFile,
                releaseDate: archiveParts.First().ReleaseDate,
                lastHealthCheck: checkedFullHealth ? DateTimeOffset.UtcNow : null
            );

            var davMultipartFile = new DavMultipartFile()
            {
                Id = davItem.Id,
                Metadata = new DavMultipartFile.Meta()
                {
                    FileParts = fileParts,
                }
            };

            dbClient.Ctx.Items.Add(davItem);
            dbClient.Ctx.MultipartFiles.Add(davMultipartFile);
        }
    }
}