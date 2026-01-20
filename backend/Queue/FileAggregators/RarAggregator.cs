using System.Diagnostics.CodeAnalysis;
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
            var fileParts = SortByPartNumber(archiveFile.Value);
            var aesParams = fileParts.Select(x => x.AesParams).FirstOrDefault(x => x != null);
            var fileSize = aesParams?.DecodedSize ?? fileParts.Sum(x => x.ByteRangeWithinPart.Count);
            var parentDirectory = EnsureParentDirectory(pathWithinArchive);
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

    private static RarProcessor.StoredFileSegment[] SortByPartNumber(
        List<RarProcessor.StoredFileSegment> storedFileSegments)
    {
        // Find delta between part number from headers and filenames.
        var delta = storedFileSegments
            .Select(x => x.PartNumber)
            .Where(x => x is { PartNumberFromHeader: >= 0, PartNumberFromFilename: >= 0 })
            .Select(x => x.PartNumberFromHeader - x.PartNumberFromFilename)
            .GroupBy(x => x)
            .MaxBy(x => x.Count())?.Key;

        // Ensure there are no duplicate part numbers.
        var allPartNumbers = storedFileSegments.Select(x => GetNormalizedPartNumber(x.PartNumber, delta));
        ValidatePartNumbers(allPartNumbers);

        // Sort by part numbers and return.
        return storedFileSegments
            .OrderBy(x => GetNormalizedPartNumber(x.PartNumber, delta))
            .ToArray();
    }

    [SuppressMessage("ReSharper", "PossibleMultipleEnumeration")]
    private static void ValidatePartNumbers(IEnumerable<int> partNumbers)
    {
        var count = partNumbers.Count();
        var uniqueCount = partNumbers.Distinct().Count();
        if (count != uniqueCount)
            throw new InvalidFormatException("Rar archive has duplicate volume numbers.");
    }

    private static int GetNormalizedPartNumber(RarProcessor.PartNumber partNumber, int? delta)
    {
        if (partNumber.PartNumberFromHeader >= 0) return partNumber.PartNumberFromHeader!.Value;
        if (partNumber.PartNumberFromFilename >= 0) return partNumber.PartNumberFromFilename!.Value + (delta ?? 0);
        return -1;
    }
}