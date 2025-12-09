using System.Text;
using Microsoft.EntityFrameworkCore;
using NzbWebDAV.Api.SabControllers.GetHistory;
using NzbWebDAV.Clients.RadarrSonarr;
using NzbWebDAV.Clients.Usenet;
using NzbWebDAV.Config;
using NzbWebDAV.Database;
using NzbWebDAV.Database.Models;
using NzbWebDAV.Extensions;
using NzbWebDAV.Queue.DeobfuscationSteps._1.FetchFirstSegment;
using NzbWebDAV.Queue.DeobfuscationSteps._2.GetPar2FileDescriptors;
using NzbWebDAV.Queue.DeobfuscationSteps._3.GetFileInfos;
using NzbWebDAV.Queue.FileAggregators;
using NzbWebDAV.Queue.FileProcessors;
using NzbWebDAV.Queue.PostProcessors;
using NzbWebDAV.Services;
using NzbWebDAV.Utils;
using NzbWebDAV.Websocket;
using Serilog;
using Usenet.Nzb;

namespace NzbWebDAV.Queue;

public class QueueItemProcessor(
    QueueItem queueItem,
    QueueNzbContents queueNzbContents,
    DavDatabaseClient dbClient,
    INntpClient usenetClient,
    ConfigManager configManager,
    WebsocketManager websocketManager,
    HealthCheckService healthCheckService,
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
            await ProcessQueueItemAsync(startTime).ConfigureAwait(false);
        }

        // When a queue-item is removed while processing,
        // then we need to clear any db changes and finish early.
        catch (Exception e) when (e.GetBaseException().IsCancellationException())
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
                Log.Error($"Failed to process job, `{queueItem.JobName}` -- {e.Message}");
                dbClient.Ctx.ChangeTracker.Clear();
                queueItem.PauseUntil = DateTime.Now.AddMinutes(1);
                dbClient.Ctx.QueueItems.Attach(queueItem);
                dbClient.Ctx.Entry(queueItem).Property(x => x.PauseUntil).IsModified = true;
                await dbClient.Ctx.SaveChangesAsync().ConfigureAwait(false);
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
                await MarkQueueItemCompleted(startTime, error: e.Message).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.Error(e, ex.Message);
            }
        }
    }

    private async Task ProcessQueueItemAsync(DateTime startTime)
    {
        var existingMountFolder = await GetMountFolder().ConfigureAwait(false);
        var duplicateNzbBehavior = configManager.GetDuplicateNzbBehavior();

        // if the mount folder already exists and setting is `marked-failed`
        // then immediately mark the job as failed.
        var isDuplicateNzb = existingMountFolder is not null;
        if (isDuplicateNzb && duplicateNzbBehavior == "mark-failed")
        {
            const string error = "Duplicate nzb: the download folder for this nzb already exists.";
            await MarkQueueItemCompleted(startTime, error, () => Task.FromResult(existingMountFolder))
                .ConfigureAwait(false);
            return;
        }

        // read the nzb document
        var documentBytes = Encoding.UTF8.GetBytes(queueNzbContents.NzbContents);
        using var stream = new MemoryStream(documentBytes);
        var nzb = await NzbDocument.LoadAsync(stream).ConfigureAwait(false);
        var archivePassword = nzb.MetaData.GetValueOrDefault("password")?.FirstOrDefault();
        var nzbFiles = nzb.Files.Where(x => x.Segments.Count > 0).ToList();

        // step 0 -- perform article existence pre-check against cache
        // https://github.com/nzbdav-dev/nzbdav/issues/101
        var articlesToPrecheck = nzbFiles.SelectMany(x => x.Segments).Select(x => x.MessageId.Value);
        healthCheckService.CheckCachedMissingSegmentIds(articlesToPrecheck);

        // step 1 -- get name and size of each nzb file
        var part1Progress = progress
            .Scale(50, 100)
            .ToPercentage(nzbFiles.Count);
        var segments = await FetchFirstSegmentsStep.FetchFirstSegments(
            nzbFiles, usenetClient, configManager, ct, part1Progress).ConfigureAwait(false);
        var par2FileDescriptors = await GetPar2FileDescriptorsStep.GetPar2FileDescriptors(
            segments, usenetClient, ct).ConfigureAwait(false);
        var fileInfos = GetFileInfosStep.GetFileInfos(
            segments, par2FileDescriptors);

        // step 2 -- perform file processing
        var fileProcessors = GetFileProcessors(fileInfos, archivePassword).ToList();
        var part2Progress = progress
            .Offset(50)
            .Scale(50, 100)
            .ToPercentage(fileProcessors.Count);
        var fileProcessingResultsAll = await fileProcessors
            .Select(x => x!.ProcessAsync())
            .WithConcurrencyAsync(configManager.GetMaxDownloadConnections() + 5)
            .GetAllAsync(ct, part2Progress).ConfigureAwait(false);
        var fileProcessingResults = fileProcessingResultsAll
            .Where(x => x is not null)
            .Select(x => x!)
            .ToList();

        // step 3 -- Optionally check full article existence
        var checkedFullHealth = false;
        if (configManager.IsEnsureArticleExistenceEnabled())
        {
            var articlesToCheck = fileInfos
                .Where(x => x.IsRar || FilenameUtil.IsImportantFileType(x.FileName))
                .SelectMany(x => x.NzbFile.GetSegmentIds())
                .ToList();
            var part3Progress = progress
                .Offset(100)
                .ToPercentage(articlesToCheck.Count);
            var healthCheckConcurrency = configManager
                .GetUsenetProviderConfig()
                .TotalPooledConnections;
            await usenetClient
                .CheckAllSegmentsAsync(articlesToCheck, healthCheckConcurrency, part3Progress, ct)
                .ConfigureAwait(false);
            checkedFullHealth = true;
        }

        // update the database
        await MarkQueueItemCompleted(startTime, error: null, async () =>
        {
            var categoryFolder = await GetOrCreateCategoryFolder().ConfigureAwait(false);
            var mountFolder = await CreateMountFolder(categoryFolder, existingMountFolder, duplicateNzbBehavior)
                .ConfigureAwait(false);
            new RarAggregator(dbClient, mountFolder, checkedFullHealth).UpdateDatabase(fileProcessingResults);
            new FileAggregator(dbClient, mountFolder, checkedFullHealth).UpdateDatabase(fileProcessingResults);
            new SevenZipAggregator(dbClient, mountFolder, checkedFullHealth).UpdateDatabase(fileProcessingResults);
            new MultipartMkvAggregator(dbClient, mountFolder, checkedFullHealth).UpdateDatabase(fileProcessingResults);

            // post-processing
            new RenameDuplicatesPostProcessor(dbClient).RenameDuplicates();
            new BlacklistedExtensionPostProcessor(configManager, dbClient).RemoveBlacklistedExtensions();

            // validate video files found
            if (configManager.IsEnsureImportableVideoEnabled())
                new EnsureImportableVideoValidator(dbClient).ThrowIfValidationFails();

            // create strm files, if necessary
            if (configManager.GetImportStrategy() == "strm")
                await new CreateStrmFilesPostProcessor(configManager, dbClient).CreateStrmFilesAsync()
                    .ConfigureAwait(false);

            return mountFolder;
        }).ConfigureAwait(false);
    }

    private IEnumerable<BaseProcessor> GetFileProcessors
    (
        List<GetFileInfosStep.FileInfo> fileInfos,
        string? archivePassword
    )
    {
        var groups = fileInfos
            .DistinctBy(x => x.FileName)
            .GroupBy(GetGroup);

        foreach (var group in groups)
        {
            if (group.Key == "7z")
                yield return new SevenZipProcessor(group.ToList(), usenetClient, archivePassword, ct);

            else if (group.Key == "rar")
                foreach (var fileInfo in group)
                    yield return new RarProcessor(fileInfo, usenetClient, archivePassword, ct);

            else if (group.Key == "multipart-mkv")
                yield return new MultipartMkvProcessor(group.ToList(), usenetClient, ct);

            else if (group.Key == "other")
                foreach (var fileInfo in group)
                    yield return new FileProcessor(fileInfo, usenetClient, ct);
        }

        yield break;

        string GetGroup(GetFileInfosStep.FileInfo x) => false ? "impossible"
            : FilenameUtil.Is7zFile(x.FileName) ? "7z"
            : x.IsRar || FilenameUtil.IsRarFile(x.FileName) ? "rar"
            : FilenameUtil.IsMultipartMkv(x.FileName) ? "multipart-mkv"
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

        return await query.FirstOrDefaultAsync(ct).ConfigureAwait(false);
    }

    private async Task<DavItem> GetOrCreateCategoryFolder()
    {
        // if the category item already exists, return it
        var categoryFolder = await dbClient.GetDirectoryChildAsync(
            DavItem.ContentFolder.Id, queueItem.Category, ct).ConfigureAwait(false);
        if (categoryFolder is not null)
            return categoryFolder;

        // otherwise, create it
        categoryFolder = DavItem.New(
            id: Guid.NewGuid(),
            parent: DavItem.ContentFolder,
            name: queueItem.Category,
            fileSize: null,
            type: DavItem.ItemType.Directory,
            releaseDate: null,
            lastHealthCheck: null
        );
        dbClient.Ctx.Items.Add(categoryFolder);
        return categoryFolder;
    }

    private Task<DavItem> CreateMountFolder
    (
        DavItem categoryFolder,
        DavItem? existingMountFolder,
        string duplicateNzbBehavior
    )
    {
        if (existingMountFolder is not null && duplicateNzbBehavior == "increment")
            return IncrementMountFolder(categoryFolder);

        var mountFolder = DavItem.New(
            id: Guid.NewGuid(),
            parent: categoryFolder,
            name: queueItem.JobName,
            fileSize: null,
            type: DavItem.ItemType.Directory,
            releaseDate: null,
            lastHealthCheck: null
        );
        dbClient.Ctx.Items.Add(mountFolder);
        return Task.FromResult(mountFolder);
    }

    private async Task<DavItem> IncrementMountFolder(DavItem categoryFolder)
    {
        for (var i = 2; i < 100; i++)
        {
            var name = $"{queueItem.JobName} ({i})";
            var existingMountFolder =
                await dbClient.GetDirectoryChildAsync(categoryFolder.Id, name, ct).ConfigureAwait(false);
            if (existingMountFolder is not null) continue;

            var mountFolder = DavItem.New(
                id: Guid.NewGuid(),
                parent: categoryFolder,
                name: name,
                fileSize: null,
                type: DavItem.ItemType.Directory,
                releaseDate: null,
                lastHealthCheck: null
            );
            dbClient.Ctx.Items.Add(mountFolder);
            return mountFolder;
        }

        throw new Exception("Duplicate nzb with more than 100 existing copies.");
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
        Func<Task<DavItem?>>? databaseOperations = null
    )
    {
        dbClient.Ctx.ChangeTracker.Clear();
        var mountFolder = databaseOperations != null ? await databaseOperations.Invoke().ConfigureAwait(false) : null;
        var historyItem = CreateHistoryItem(mountFolder, startTime, error);
        var historySlot = GetHistoryResponse.HistorySlot.FromHistoryItem(historyItem, mountFolder, configManager);
        dbClient.Ctx.QueueItems.Remove(queueItem);
        dbClient.Ctx.HistoryItems.Add(historyItem);
        await dbClient.Ctx.SaveChangesAsync(ct).ConfigureAwait(false);
        _ = websocketManager.SendMessage(WebsocketTopic.QueueItemRemoved, queueItem.Id.ToString());
        _ = websocketManager.SendMessage(WebsocketTopic.HistoryItemAdded, historySlot.ToJson());
        _ = RefreshMonitoredDownloads();
    }

    private async Task RefreshMonitoredDownloads()
    {
        var tasks = configManager
            .GetArrConfig()
            .GetArrClients()
            .Select(RefreshMonitoredDownloads);
        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private async Task RefreshMonitoredDownloads(ArrClient arrClient)
    {
        try
        {
            var downloadClients = await arrClient.GetDownloadClientsAsync().ConfigureAwait(false);
            if (downloadClients.All(x => x.Category != queueItem.Category)) return;
            var queueCount = await arrClient.GetQueueCountAsync().ConfigureAwait(false);
            if (queueCount < 300) await arrClient.RefreshMonitoredDownloads().ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Log.Debug($"Could not refresh monitored downloads for Arr instance: `{arrClient.Host}`. {e.Message}");
        }
    }
}