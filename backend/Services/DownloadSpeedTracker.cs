using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using NzbWebDAV.Utils;
using NzbWebDAV.Websocket;

namespace NzbWebDAV.Services;

/// <summary>
/// Tracks the download speed from usenet providers and periodically
/// publishes it over the websocket. The total counter includes all article
/// bytes read from usenet connections, while named sessions break the
/// traffic down per consumer (webdav streams and queue imports).
/// </summary>
public class DownloadSpeedTracker : IDisposable
{
    private readonly WebsocketManager _websocketManager;
    private readonly ConcurrentDictionary<Guid, DownloadSpeedSession> _sessions = new();
    private readonly CancellationTokenSource _cts;
    private long _totalBytes;

    public DownloadSpeedTracker(WebsocketManager websocketManager)
    {
        _websocketManager = websocketManager;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(SigtermUtil.GetCancellationToken());
        _ = PublishLoopAsync(_cts.Token);
    }

    public DownloadSpeedSession CreateSession(string name, DownloadSpeedSession.SessionCategory category)
    {
        var session = new DownloadSpeedSession(name, category, x => _sessions.TryRemove(x.Id, out _));
        _sessions[session.Id] = session;
        return session;
    }

    public void AddTotalBytes(int byteCount)
    {
        Interlocked.Add(ref _totalBytes, byteCount);
    }

    private async Task PublishLoopAsync(CancellationToken ct)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
            var stopwatch = Stopwatch.StartNew();
            var lastTotalBytes = 0L;
            var wasIdle = false;

            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                var elapsedSeconds = stopwatch.Elapsed.TotalSeconds;
                stopwatch.Restart();
                if (elapsedSeconds <= 0) continue;

                var totalBytes = Interlocked.Read(ref _totalBytes);
                var totalRate = (long)((totalBytes - lastTotalBytes) / elapsedSeconds);
                lastTotalBytes = totalBytes;

                // group concurrent sessions with the same name and category
                // (e.g. multiple range-requests for the same file) into one entry.
                var sessions = _sessions.Values
                    .GroupBy(x => (x.Category, x.Name))
                    .Select(group => new SessionReport(
                        Name: group.Key.Name,
                        Category: GetCategoryName(group.Key.Category),
                        Rate: (long)(group.Sum(x => x.TakeBytesDelta()) / elapsedSeconds)))
                    .OrderBy(x => x.Category).ThenBy(x => x.Name)
                    .ToList();

                var streamsRate = sessions
                    .Where(x => x.Category == StreamCategoryName)
                    .Sum(x => x.Rate);

                // while idle, publish a single zeroed report and then go quiet
                // until there is activity again.
                var isIdle = totalRate == 0 && sessions.Count == 0;
                if (isIdle && wasIdle) continue;
                wasIdle = isIdle;

                var report = new SpeedReport(totalRate, streamsRate, sessions);
                var message = JsonSerializer.Serialize(report, JsonSerializerOptions.Web);
                _ = _websocketManager.SendMessage(WebsocketTopic.UsenetDownloadSpeed, message);
            }
        }
        catch (OperationCanceledException)
        {
            // application is shutting down
        }
    }

    private const string StreamCategoryName = "stream";

    private static string GetCategoryName(DownloadSpeedSession.SessionCategory category) => category switch
    {
        DownloadSpeedSession.SessionCategory.Stream => StreamCategoryName,
        DownloadSpeedSession.SessionCategory.QueueImport => "queue",
        _ => "other",
    };

    private sealed record SpeedReport(long Total, long Streams, List<SessionReport> Sessions);

    private sealed record SessionReport(string Name, string Category, long Rate);

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
        GC.SuppressFinalize(this);
    }
}
