﻿using NzbWebDAV.Database;
using NzbWebDAV.Database.Models;
using NzbWebDAV.Services.FileProcessors;

namespace NzbWebDAV.Services.FileAggregators;

public class FileAggregator(DavDatabaseClient dbClient, DavItem mountDirectory) : IAggregator
{
    public void UpdateDatabase(List<BaseProcessor.Result> processorResults)
    {
        foreach (var processorResult in processorResults)
        {
            if (processorResult is not FileProcessor.Result result) continue;
            if (result.FileName == "") continue; // skip files whose name we can't determine

            // Check if file already exists
            var existingItem = dbClient.Ctx.Items
                .FirstOrDefault(x => x.ParentId == mountDirectory.Id && x.Name == result.FileName);
            if (existingItem is not null)
            {
                continue; // Skip if file already exists
            }

            var davItem = new DavItem()
            {
                Id = Guid.NewGuid(),
                CreatedAt = DateTime.Now,
                ParentId = mountDirectory.Id,
                Name = result.FileName,
                FileSize = result.FileSize,
                Type = DavItem.ItemType.NzbFile
            };

            var davNzbFile = new DavNzbFile()
            {
                Id = davItem.Id,
                SegmentIds = result.NzbFile.Segments.Select(x => x.MessageId.Value).ToArray(),
            };

            dbClient.Ctx.Items.Add(davItem);
            dbClient.Ctx.NzbFiles.Add(davNzbFile);
        }
    }
}