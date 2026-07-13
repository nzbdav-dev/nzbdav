using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using NzbWebDAV.Websocket;

namespace NzbWebDAV.Services;

/// <summary>
/// Tracks the download speed from usenet providers and periodically
/// publishes it over the websocket. The total counter includes all article
/// bytes read from usenet connections, while named sessions break the
/// traffic down per consumer (webdav streams and queue imports).
/// </summary>
public class DownloadSpeedTracker(WebsocketManager websocketManager) : BackgroundService
{
    private readonly ConcurrentDictionary<Guid, DownloadSpeedSession> _sessions = new();
    private readonly ConcurrentDictionary<SessionKey, long> _residualBytes = new();
    private long _totalBytes;

    public DownloadSpeedSession CreateSession(string name, DownloadSpeedSession.SessionCategory category)
    {
        var session = new DownloadSpeedSession(name, category, OnSessionDisposed);
        _sessions[session.Id] = session;
        return session;
    }

    public void AddTotalBytes(int byteCount)
    {
        Interlocked.Add(ref _totalBytes, byteCount);
    }

    private void OnSessionDisposed(DownloadSpeedSession session)
    {
        _sessions.TryRemove(session.Id, out _);

        // flush the bytes added since the last publish tick, so that the
        // per-session breakdown does not undercount the total.
        var remainingBytes = session.TakeBytesDelta();
        if (remainingBytes > 0)
        {
            var key = new SessionKey(session.Category, session.Name);
            _residualBytes.AddOrUpdate(key, remainingBytes, (_, bytes) => bytes + remainingBytes);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
            var stopwatch = Stopwatch.StartNew();
            var lastTotalBytes = 0L;
            var lastMessage = default(string);

            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                var elapsedSeconds = stopwatch.Elapsed.TotalSeconds;
                stopwatch.Restart();
                if (elapsedSeconds <= 0) continue;

                var totalBytes = Interlocked.Read(ref _totalBytes);
                var totalRate = (long)((totalBytes - lastTotalBytes) / elapsedSeconds);
                lastTotalBytes = totalBytes;

                // group concurrent sessions with the same name and category
                // (e.g. multiple range-requests for the same file) into one entry.
                var deltas = new Dictionary<SessionKey, long>();
                foreach (var session in _sessions.Values)
                {
                    var key = new SessionKey(session.Category, session.Name);
                    deltas[key] = deltas.GetValueOrDefault(key) + session.TakeBytesDelta();
                }

                // include the final bytes of sessions disposed since the last tick.
                foreach (var key in _residualBytes.Keys)
                    if (_residualBytes.TryRemove(key, out var residual))
                        deltas[key] = deltas.GetValueOrDefault(key) + residual;

                var sessions = deltas
                    .Select(x => new SessionReport(
                        Name: x.Key.Name,
                        Category: GetCategoryName(x.Key.Category),
                        Rate: (long)(x.Value / elapsedSeconds)))
                    .OrderBy(x => x.Category).ThenBy(x => x.Name)
                    .ToList();

                // go quiet while nothing changes (e.g. fully idle, or open
                // sessions that transfer no bytes), so the last report always
                // reflects the current state without publishing every second.
                var report = new SpeedReport(totalRate, sessions);
                var message = JsonSerializer.Serialize(report, JsonSerializerOptions.Web);
                if (message == lastMessage) continue;
                lastMessage = message;

                _ = websocketManager.SendMessage(WebsocketTopic.UsenetDownloadSpeed, message);
            }
        }
        catch (OperationCanceledException)
        {
            // application is shutting down
        }
    }

    private static string GetCategoryName(DownloadSpeedSession.SessionCategory category) => category switch
    {
        DownloadSpeedSession.SessionCategory.Stream => "stream",
        DownloadSpeedSession.SessionCategory.QueueImport => "queue",
        _ => "other",
    };

    private readonly record struct SessionKey(DownloadSpeedSession.SessionCategory Category, string Name);

    private sealed record SpeedReport(long Total, List<SessionReport> Sessions);

    private sealed record SessionReport(string Name, string Category, long Rate);
}
