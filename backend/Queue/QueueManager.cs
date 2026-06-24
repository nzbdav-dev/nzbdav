using NzbWebDAV.Clients.Usenet;
using NzbWebDAV.Config;
using NzbWebDAV.Database;
using NzbWebDAV.Database.Models;
using NzbWebDAV.Extensions;
using NzbWebDAV.Services;
using NzbWebDAV.Utils;
using NzbWebDAV.Websocket;
using Serilog;

namespace NzbWebDAV.Queue;

public class QueueManager : IDisposable
{
    private readonly Dictionary<Guid, InProgressQueueItem> _inProgressQueueItems = new();

    private readonly UsenetStreamingClient _usenetClient;
    private readonly CancellationTokenSource? _cancellationTokenSource;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly ConfigManager _configManager;
    private readonly WebsocketManager _websocketManager;

    private CancellationTokenSource _sleepingQueueToken = new();
    private readonly Lock _sleepingQueueLock = new();

    public QueueManager(
        UsenetStreamingClient usenetClient,
        ConfigManager configManager,
        WebsocketManager websocketManager
    )
    {
        _usenetClient = usenetClient;
        _configManager = configManager;
        _websocketManager = websocketManager;
        _cancellationTokenSource = CancellationTokenSource
            .CreateLinkedTokenSource(SigtermUtil.GetCancellationToken());
        _ = ProcessQueueAsync(_cancellationTokenSource.Token);
    }

    public (QueueItem queueItem, int progress)[] GetInProgressQueueItems()
    {
        return Lock(() => _inProgressQueueItems.Values
            .Select(x => (x.QueueItem, x.ProgressPercentage))
            .ToArray());
    }

    public void AwakenQueue(DateTime? dateTime = null)
    {
        TimeSpan? cancelAfter = dateTime.HasValue ? (dateTime.Value - DateTime.Now) : null;
        lock (_sleepingQueueLock)
        {
            if (cancelAfter.HasValue && cancelAfter.Value > TimeSpan.Zero)
                _sleepingQueueToken.CancelAfter(cancelAfter.Value);
            else
                _sleepingQueueToken.Cancel();
        }
    }

    public async Task RemoveQueueItemsAsync
    (
        List<Guid> queueItemIds,
        DavDatabaseClient dbClient,
        CancellationToken ct = default
    )
    {
        var queueItemIdSet = queueItemIds.ToHashSet();
        var inProgressQueueItems = await LockAsync(() => _inProgressQueueItems
            .Where(x => queueItemIdSet.Contains(x.Key))
            .Select(x => x.Value)
            .ToList()).ConfigureAwait(false);

        foreach (var inProgressQueueItem in inProgressQueueItems)
            await inProgressQueueItem.CancellationTokenSource.CancelAsync().ConfigureAwait(false);

        try
        {
            await Task.WhenAll(inProgressQueueItems.Select(x => x.ProcessingTask)).ConfigureAwait(false);
        }
        catch (Exception e) when (!e.GetBaseException().IsCancellationException())
        {
            Log.Warning(e, "Error while waiting for removed queue items to stop.");
        }

        await LockAsync(() =>
        {
            foreach (var queueItemId in queueItemIds)
                _inProgressQueueItems.Remove(queueItemId);
        }).ConfigureAwait(false);

        await dbClient.RemoveQueueItemsAsync(queueItemIds, ct).ConfigureAwait(false);
        await dbClient.Ctx.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private async Task ProcessQueueAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await RemoveCompletedQueueItemsAsync().ConfigureAwait(false);
                var startedAnyQueueItems = await StartAvailableQueueItemsAsync(ct).ConfigureAwait(false);
                var hasActiveQueueItems = await LockAsync(() => _inProgressQueueItems.Count > 0).ConfigureAwait(false);

                if (!hasActiveQueueItems)
                {
                    await SleepUntilQueueAwakens().ConfigureAwait(false);
                    continue;
                }

                if (!startedAnyQueueItems)
                    await WaitForAnyQueueItemToComplete(ct).ConfigureAwait(false);
            }
            catch (Exception e) when (e.GetBaseException().IsCancellationException())
            {
                // Application shutdown or queue cancellation.
            }
            catch (Exception e)
            {
                Log.Error($"An unexpected error occured while processing the queue: {e.Message}");
            }
        }
    }

    private async Task<bool> StartAvailableQueueItemsAsync(CancellationToken ct)
    {
        var startedAnyQueueItems = false;
        var maxConcurrentDownloads = _configManager.GetConcurrentNzbDownloads();

        while (!ct.IsCancellationRequested)
        {
            var activeQueueItemIds = await LockAsync(() => _inProgressQueueItems.Keys.ToHashSet()).ConfigureAwait(false);
            if (activeQueueItemIds.Count >= maxConcurrentDownloads) break;

            await using var dbContext = new DavDatabaseContext();
            var dbClient = new DavDatabaseClient(dbContext);
            var topItem = await dbClient.GetTopQueueItem(ct, activeQueueItemIds).ConfigureAwait(false);
            if (topItem.queueItem is null)
            {
                if (topItem.queueNzbStream is not null)
                    await topItem.queueNzbStream.DisposeAsync().ConfigureAwait(false);
                break;
            }

            await LockAsync(() =>
            {
                if (_inProgressQueueItems.ContainsKey(topItem.queueItem.Id))
                {
                    if (topItem.queueNzbStream is not null)
                        _ = topItem.queueNzbStream.DisposeAsync();
                    return;
                }

                _inProgressQueueItems[topItem.queueItem.Id] = BeginProcessingQueueItem(
                    topItem.queueItem,
                    topItem.queueNzbStream,
                    ct
                );
                startedAnyQueueItems = true;
            }).ConfigureAwait(false);
        }

        return startedAnyQueueItems;
    }

    private async Task WaitForAnyQueueItemToComplete(CancellationToken ct)
    {
        var tasks = await LockAsync(() => _inProgressQueueItems.Values
            .Select(x => x.ProcessingTask)
            .ToArray()).ConfigureAwait(false);

        if (tasks.Length == 0) return;
        await Task.WhenAny(tasks).WaitAsync(ct).ConfigureAwait(false);
        await RemoveCompletedQueueItemsAsync().ConfigureAwait(false);
    }

    private async Task RemoveCompletedQueueItemsAsync()
    {
        await LockAsync(() =>
        {
            var completedQueueItems = _inProgressQueueItems
                .Where(x => x.Value.ProcessingTask.IsCompleted)
                .ToList();

            foreach (var completedQueueItem in completedQueueItems)
            {
                var queueItemId = completedQueueItem.Key;
                var processingTask = completedQueueItem.Value.ProcessingTask;
                if (processingTask.IsFaulted)
                    Log.Error(processingTask.Exception, "Queue item {QueueItemId} processing task failed unexpectedly.", queueItemId);

                _inProgressQueueItems.Remove(queueItemId);
            }
        }).ConfigureAwait(false);
    }

    private async Task SleepUntilQueueAwakens()
    {
        try
        {
            // if we're done with the queue, wait a minute before checking again.
            // or wait until awoken by cancellation of _sleepingQueueToken
            await Task.Delay(TimeSpan.FromMinutes(1), _sleepingQueueToken.Token).ConfigureAwait(false);
        }
        catch when (_sleepingQueueToken.IsCancellationRequested)
        {
            lock (_sleepingQueueLock)
            {
                if (!_sleepingQueueToken.TryReset())
                {
                    _sleepingQueueToken.Dispose();
                    _sleepingQueueToken = new CancellationTokenSource();
                }
            }
        }
    }

    private InProgressQueueItem BeginProcessingQueueItem
    (
        QueueItem queueItem,
        Stream? queueNzbStream,
        CancellationToken ct
    )
    {
        var dbContext = new DavDatabaseContext();
        var dbClient = new DavDatabaseClient(dbContext);
        var cachingUsenetClient = new ArticleCachingNntpClient(_usenetClient);
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var progressHook = new Progress<int>();
        var inProgressQueueItem = new InProgressQueueItem()
        {
            QueueItem = queueItem,
            ProgressPercentage = 0,
            CancellationTokenSource = cts
        };

        inProgressQueueItem.ProcessingTask = ProcessQueueItemAsync();

        var debounce = DebounceUtil.CreateDebounce(TimeSpan.FromMilliseconds(200));
        progressHook.ProgressChanged += (_, progress) =>
        {
            inProgressQueueItem.ProgressPercentage = progress;
            var message = $"{queueItem.Id}|{progress}";
            if (progress is 100 or 200) _websocketManager.SendMessage(WebsocketTopic.QueueItemProgress, message);
            else debounce(() => _websocketManager.SendMessage(WebsocketTopic.QueueItemProgress, message));
        };
        return inProgressQueueItem;

        async Task ProcessQueueItemAsync()
        {
            try
            {
                await new QueueItemProcessor(
                    queueItem, queueNzbStream, dbClient, cachingUsenetClient,
                    _configManager, _websocketManager, progressHook, cts.Token
                ).ProcessAsync().ConfigureAwait(false);
            }
            finally
            {
                if (queueNzbStream is not null)
                    await queueNzbStream.DisposeAsync().ConfigureAwait(false);
                cachingUsenetClient.Dispose();
                await dbContext.DisposeAsync().ConfigureAwait(false);
                cts.Dispose();
            }
        }
    }

    private async Task LockAsync(Func<Task> actionAsync)
    {
        await _semaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            await actionAsync().ConfigureAwait(false);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private async Task<T> LockAsync<T>(Func<Task<T>> actionAsync)
    {
        await _semaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            return await actionAsync().ConfigureAwait(false);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private async Task LockAsync(Action action)
    {
        await _semaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            action();
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private async Task<T> LockAsync<T>(Func<T> action)
    {
        await _semaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            return action();
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private T Lock<T>(Func<T> action)
    {
        _semaphore.Wait();
        try
        {
            return action();
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public void Dispose()
    {
        _cancellationTokenSource?.Cancel();
        _cancellationTokenSource?.Dispose();
    }

    private class InProgressQueueItem
    {
        public required QueueItem QueueItem { get; init; }
        public int ProgressPercentage { get; set; }
        public Task ProcessingTask { get; set; } = Task.CompletedTask;
        public required CancellationTokenSource CancellationTokenSource { get; init; }
    }
}