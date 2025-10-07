using System.Text;
using Microsoft.EntityFrameworkCore;
using NzbWebDAV.Api.SabControllers.GetHistory;
using NzbWebDAV.Clients.Usenet;
using NzbWebDAV.Clients.Usenet.Connections;
using NzbWebDAV.Config;
using NzbWebDAV.Database;
using NzbWebDAV.Database.Models;
using NzbWebDAV.Extensions;
using NzbWebDAV.Queue.DeobfuscationSteps._1.FetchFirstSegment;
using NzbWebDAV.Queue.DeobfuscationSteps._2.GetPar2FileDescriptors;
using NzbWebDAV.Queue.DeobfuscationSteps._3.GetFileInfos;
using NzbWebDAV.Queue.FileAggregators;
using NzbWebDAV.Queue.FileProcessors;
using NzbWebDAV.Queue.Validators;
using NzbWebDAV.Utils;
using NzbWebDAV.Websocket;
using Serilog;
using Usenet.Nzb;

namespace NzbWebDAV.Queue;

public class QueueItemProcessor(
    QueueItem queueItem,
    DavDatabaseClient dbClient,
    UsenetStreamingClient usenetClient,
    ConfigManager configManager,
    WebsocketManager websocketManager,
    IProgress<int> progress,
    CancellationToken ct
)
{
    public async Task ProcessAsync()
    {
        // initialize
        var startTime = DateTime.Now;
        _ = websocketManager.SendMessage(WebsocketTopic.QueueItemStatus, $"{queueItem.Id}|Downloading");

        // process the job
        try
        {
            await ProcessQueueItemAsync(startTime);
        }

        // When a queue-item is removed while processing,
        // then we need to clear any db changes and finish early.
        catch (Exception e) when (e.GetBaseException() is OperationCanceledException or TaskCanceledException)
        {
            Log.Information($"Processing of queue item `{queueItem.JobName}` was cancelled.");
            dbClient.Ctx.ChangeTracker.Clear();
        }

        // when a retryable error is encountered
        // let's not remove the item from the queue
        // to give it a chance to retry. Simply
        // log the error and retry in a minute.
        catch (Exception e) when (e.IsRetryableDownloadException())
        {
            try
            {
                Log.Error($"Failed to process job, `{queueItem.JobName}` -- {e.Message} -- {e}");
                dbClient.Ctx.ChangeTracker.Clear();
                queueItem.PauseUntil = DateTime.Now.AddMinutes(1);
                dbClient.Ctx.QueueItems.Attach(queueItem);
                dbClient.Ctx.Entry(queueItem).Property(x => x.PauseUntil).IsModified = true;
                await dbClient.Ctx.SaveChangesAsync();
                _ = websocketManager.SendMessage(WebsocketTopic.QueueItemStatus, $"{queueItem.Id}|Queued");
            }
            catch (Exception ex)
            {
                Log.Error(ex, ex.Message);
            }
        }

        // when any other error is encountered,
        // we must still remove the queue-item and add
        // it to the history as a failed job.
        catch (Exception e)
        {
            try
            {
                await MarkQueueItemCompleted(startTime, error: e.Message);
            }
            catch (Exception ex)
            {
                Log.Error(e, ex.Message);
            }
        }
    }

    private async Task ProcessQueueItemAsync(DateTime startTime)
    {
        // if the mount folder already exists,
        // then we've already downloaded this nzb and we can mark the job as completed.
        var existingMountFolder = await GetMountFolder();
        var isAlreadyDownloaded = existingMountFolder is not null;
        if (isAlreadyDownloaded)
        {
            Log.Information($"Nzb `{queueItem.JobName}` is a duplicate. Skipping and marking complete.");
            await MarkQueueItemCompleted(startTime, error: null, () => existingMountFolder);
            return;
        }

        // ensure we don't use more than max-queue-connections
        var reservedConnections = configManager.GetMaxConnections() - configManager.GetMaxQueueConnections();
        using var _ = ct.SetScopedContext(new ReservedConnectionsContext(reservedConnections));

        // read the nzb document
        var documentBytes = Encoding.UTF8.GetBytes(queueItem.NzbContents);
        using var stream = new MemoryStream(documentBytes);
        var nzb = await NzbDocument.LoadAsync(stream);
        var nzbFiles = nzb.Files.Where(x => x.Segments.Count > 0).ToList();

        // part 1 -- get name and size of each nzb file
        var part1Progress = progress
            .Scale(50, 100)
            .ToPercentage(nzbFiles.Count);
        var segments = await FetchFirstSegmentsStep.FetchFirstSegments(
            nzbFiles, usenetClient, configManager, ct, part1Progress);
        var par2FileDescriptors = await GetPar2FileDescriptorsStep.GetPar2FileDescriptors(
            segments, usenetClient, ct);
        var fileInfos = GetFileInfosStep.GetFileInfos(
            segments, par2FileDescriptors);

        // part 2 -- perform file processing
        var fileProcessors = GetFileProcessors(fileInfos).ToList();
        var part2Progress = progress
            .Offset(50)
            .Scale(50, 100)
            .ToPercentage(fileProcessors.Count);
        var fileProcessingResultsAll = await fileProcessors
            .Select(x => x!.ProcessAsync())
            .WithConcurrencyAsync(configManager.GetMaxQueueConnections())
            .GetAllAsync(ct, part2Progress);
        var fileProcessingResults = fileProcessingResultsAll
            .Where(x => x is not null)
            .Select(x => x!)
            .ToList();

        // part3 -- Optionally check full article existence
        if (configManager.IsEnsureArticleExistenceEnabled())
        {
            var articlesToCheck = fileInfos
                .Where(x => FilenameUtil.IsImportantFileType(x.FileName))
                .SelectMany(x => x.NzbFile.GetSegmentIds())
                .ToList();
            var part3Progress = progress
                .Offset(100)
                .ToPercentage(articlesToCheck.Count);
            var concurrency = configManager.GetMaxQueueConnections();
            await usenetClient.CheckAllSegmentsAsync(articlesToCheck, concurrency, part3Progress, ct);
        }

        // update the database
        await MarkQueueItemCompleted(startTime, error: null, () =>
        {
            var categoryFolder = GetOrCreateCategoryFolder();
            var mountFolder = CreateMountFolder(categoryFolder);
            new RarAggregator(dbClient, mountFolder).UpdateDatabase(fileProcessingResults);
            new FileAggregator(dbClient, mountFolder).UpdateDatabase(fileProcessingResults);
            new SevenZipAggregator(dbClient, mountFolder).UpdateDatabase(fileProcessingResults);
            new MultipartMkvAggregator(dbClient, mountFolder).UpdateDatabase(fileProcessingResults);

            // validate video files found
            if (configManager.IsEnsureImportableVideoEnabled())
                new EnsureImportableVideoValidator(dbClient).ThrowIfValidationFails();

            return mountFolder;
        });
    }

    private IEnumerable<BaseProcessor> GetFileProcessors(List<GetFileInfosStep.FileInfo> fileInfos)
    {
        var groups = fileInfos
            .DistinctBy(x => x.FileName)
            .GroupBy(x => GetGroup(x.FileName));

        foreach (var group in groups)
        {
            if (group.Key == "7z")
                yield return new SevenZipProcessor(group.ToList(), usenetClient, ct);

            else if (group.Key == "rar")
                foreach (var fileInfo in group)
                    yield return new RarProcessor(fileInfo, usenetClient, ct);

            else if (group.Key == "multipart-mkv")
                yield return new MultipartMkvProcessor(group.ToList(), usenetClient, ct);

            else if (group.Key == "other")
                foreach (var fileInfo in group)
                    yield return new FileProcessor(fileInfo, usenetClient, ct);
        }

        yield break;

        string GetGroup(string x) => false ? "impossible"
            : FilenameUtil.Is7zFile(x) ? "7z"
            : FilenameUtil.IsRarFile(x) ? "rar"
            : FilenameUtil.IsMultipartMkv(x) ? "multipart-mkv"
            : "other";
    }

    private async Task<DavItem?> GetMountFolder()
    {
        var query = from mountFolder in dbClient.Ctx.Items
            join categoryFolder in dbClient.Ctx.Items on mountFolder.ParentId equals categoryFolder.Id
            where mountFolder.Name == queueItem.JobName
                  && mountFolder.ParentId != null
                  && categoryFolder.Name == queueItem.Category
                  && categoryFolder.ParentId == DavItem.ContentFolder.Id
            select mountFolder;

        return await query.FirstOrDefaultAsync(ct);
    }

    private DavItem GetOrCreateCategoryFolder()
    {
        // if the category item already exists, return it
        var categoryFolder = dbClient.Ctx.Items
            .FirstOrDefault(x => x.Parent == DavItem.ContentFolder && x.Name == queueItem.Category);
        if (categoryFolder is not null)
            return categoryFolder;

        // otherwise, create it
        categoryFolder = DavItem.New(
            id: Guid.NewGuid(),
            parent: DavItem.ContentFolder,
            name: queueItem.Category,
            fileSize: null,
            type: DavItem.ItemType.Directory
        );
        dbClient.Ctx.Items.Add(categoryFolder);
        return categoryFolder;
    }

    private DavItem CreateMountFolder(DavItem categoryFolder)
    {
        var mountFolder = DavItem.New(
            id: Guid.NewGuid(),
            parent: categoryFolder,
            name: queueItem.JobName,
            fileSize: null,
            type: DavItem.ItemType.Directory
        );
        dbClient.Ctx.Items.Add(mountFolder);
        return mountFolder;
    }

    private HistoryItem CreateHistoryItem(DavItem? mountFolder, DateTime jobStartTime, string? errorMessage = null)
    {
        return new HistoryItem()
        {
            Id = queueItem.Id,
            CreatedAt = DateTime.Now,
            FileName = queueItem.FileName,
            JobName = queueItem.JobName,
            Category = queueItem.Category,
            DownloadStatus = errorMessage == null
                ? HistoryItem.DownloadStatusOption.Completed
                : HistoryItem.DownloadStatusOption.Failed,
            TotalSegmentBytes = queueItem.TotalSegmentBytes,
            DownloadTimeSeconds = (int)(DateTime.Now - jobStartTime).TotalSeconds,
            FailMessage = errorMessage,
            DownloadDirId = mountFolder?.Id,
        };
    }

    private async Task MarkQueueItemCompleted
    (
        DateTime startTime,
        string? error = null,
        Func<DavItem?>? databaseOperations = null
    )
    {
        dbClient.Ctx.ChangeTracker.Clear();
        var mountFolder = databaseOperations?.Invoke();
        var mountDirectory = configManager.GetRcloneMountDir();
        var historyItem = CreateHistoryItem(mountFolder, startTime, error);
        var historySlot = GetHistoryResponse.HistorySlot.FromHistoryItem(historyItem, mountDirectory);
        dbClient.Ctx.QueueItems.Remove(queueItem);
        dbClient.Ctx.HistoryItems.Add(historyItem);
        await dbClient.Ctx.SaveChangesAsync(ct);
        _ = websocketManager.SendMessage(WebsocketTopic.QueueItemRemoved, queueItem.Id.ToString());
        _ = websocketManager.SendMessage(WebsocketTopic.HistoryItemAdded, historySlot.ToJson());
    }
}