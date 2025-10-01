using NzbWebDAV.Database;
using NzbWebDAV.Database.Models;
using NzbWebDAV.Extensions;
using NzbWebDAV.Queue.FileProcessors;

namespace NzbWebDAV.Queue.FileAggregators;

public class FileAggregator(DavDatabaseClient dbClient, DavItem mountDirectory) : BaseAggregator
{
    protected override DavDatabaseClient DBClient => dbClient;
    protected override DavItem MountDirectory => mountDirectory;

    public override void UpdateDatabase(List<BaseProcessor.Result> processorResults)
    {
        foreach (var processorResult in processorResults)
        {
            if (processorResult is not FileProcessor.Result result) continue;
            if (result.FileName == "") continue; // skip files whose name we can't determine

            var davItem = DavItem.New(
                id: Guid.NewGuid(),
                parent: mountDirectory,
                name: result.FileName,
                fileSize: result.FileSize,
                type: DavItem.ItemType.NzbFile
            );

            var davNzbFile = new DavNzbFile()
            {
                Id = davItem.Id,
                SegmentIds = result.NzbFile.GetSegmentIds(),
            };

            dbClient.Ctx.Items.Add(davItem);
            dbClient.Ctx.NzbFiles.Add(davNzbFile);
        }
    }
}