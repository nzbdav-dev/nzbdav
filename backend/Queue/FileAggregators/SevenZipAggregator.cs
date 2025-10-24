using NzbWebDAV.Database;
using NzbWebDAV.Database.Models;
using NzbWebDAV.Queue.FileProcessors;
using NzbWebDAV.Utils;

namespace NzbWebDAV.Queue.FileAggregators;

public class SevenZipAggregator(
    DavDatabaseClient dbClient,
    DavItem mountDirectory,
    bool checkedFullHealth
) : BaseAggregator
{
    protected override DavDatabaseClient DBClient => dbClient;
    protected override DavItem MountDirectory => mountDirectory;

    public override void UpdateDatabase(List<BaseProcessor.Result> processorResults)
    {
        var sevenZipFiles = processorResults
            .OfType<SevenZipProcessor.Result>()
            .SelectMany(x => x.SevenZipFiles)
            .ToList();

        ProcessSevenZipFile(sevenZipFiles);
    }

    private void ProcessSevenZipFile(List<SevenZipProcessor.SevenZipFile> sevenZipFiles)
    {
        foreach (var sevenZipFile in sevenZipFiles)
        {
            var pathWithinArchive = sevenZipFile.PathWithinArchive;
            var sevenZipParts = sevenZipFile.Parts;
            var parentDirectory = EnsureExtractPath(pathWithinArchive);
            var name = Path.GetFileName(pathWithinArchive);

            // If there is only one file in the archive and the file-name is obfuscated,
            // then rename the file to the same name as the containing mount directory.
            if (sevenZipFiles.Count == 1 && ObfuscationUtil.IsProbablyObfuscated(name))
                name = mountDirectory.Name + Path.GetExtension(name);

            var davItem = DavItem.New(
                id: Guid.NewGuid(),
                parent: parentDirectory,
                name: name,
                fileSize: sevenZipParts.Sum(x => x.FilePartByteRange.Count),
                type: DavItem.ItemType.MultipartFile,
                releaseDate: sevenZipFile.ReleaseDate,
                lastHealthCheck: checkedFullHealth ? DateTimeOffset.UtcNow : null
            );

            var davMultipartFile = new DavMultipartFile()
            {
                Id = davItem.Id,
                Metadata = new DavMultipartFile.Meta()
                {
                    FileParts = sevenZipParts.ToArray()
                }
            };

            dbClient.Ctx.Items.Add(davItem);
            dbClient.Ctx.MultipartFiles.Add(davMultipartFile);
        }
    }
}