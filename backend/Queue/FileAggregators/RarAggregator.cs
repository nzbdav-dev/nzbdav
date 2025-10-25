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
        var fileSegments = processorResults
            .OfType<RarProcessor.Result>()
            .SelectMany(x => x.StoredFileSegments)
            .OrderBy(x => x.PartNumber)
            .ToList();

        ProcessArchive(fileSegments);
    }

    private void ProcessArchive(List<RarProcessor.StoredFileSegment> fileSegments)
    {
        var archiveFiles = new Dictionary<string, List<RarProcessor.StoredFileSegment>>();
        foreach (var fileSegment in fileSegments)
        {
            if (!archiveFiles.ContainsKey(fileSegment.PathWithinArchive))
                archiveFiles.Add(fileSegment.PathWithinArchive, []);

            archiveFiles[fileSegment.PathWithinArchive].Add(fileSegment);
        }

        foreach (var archiveFile in archiveFiles)
        {
            var pathWithinArchive = archiveFile.Key;
            var fileParts = archiveFile.Value.ToArray();
            var aesParams = fileParts.Select(x => x.AesParams).FirstOrDefault(x => x != null);
            var fileSize = aesParams?.DecodedSize ?? fileParts.Sum(x => x.ByteRangeWithinPart.Count);
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
                fileSize: fileSize,
                type: DavItem.ItemType.MultipartFile,
                releaseDate: fileParts.First().ReleaseDate,
                lastHealthCheck: checkedFullHealth ? DateTimeOffset.UtcNow : null
            );

            var davMultipartFile = new DavMultipartFile()
            {
                Id = davItem.Id,
                Metadata = new DavMultipartFile.Meta()
                {
                    AesParams = aesParams,
                    FileParts = fileParts.Select(x => new DavMultipartFile.FilePart()
                    {
                        SegmentIds = x.NzbFile.GetSegmentIds(),
                        SegmentIdByteRange = LongRange.FromStartAndSize(0, x.PartSize),
                        FilePartByteRange = x.ByteRangeWithinPart
                    }).ToArray(),
                }
            };

            dbClient.Ctx.Items.Add(davItem);
            dbClient.Ctx.MultipartFiles.Add(davMultipartFile);
        }
    }
}