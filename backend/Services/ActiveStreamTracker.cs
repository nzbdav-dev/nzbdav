using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using NzbWebDAV.Websocket;

namespace NzbWebDAV.Services;

public class ActiveStreamTracker(WebsocketManager websocketManager)
{
    private readonly ConcurrentDictionary<string, StreamInfo> _streams = new();

    public string Register(string fileKey, string fileName, long fileSize)
    {
        _streams.AddOrUpdate(
            fileKey,
            _ => new StreamInfo(fileName, fileSize),
            (_, existing) =>
            {
                Interlocked.Increment(ref existing.RefCount);
                return existing;
            });
        Broadcast();
        return fileKey;
    }

    public void ReportProgress(string fileKey, long bytesRead)
    {
        if (!_streams.TryGetValue(fileKey, out var info)) return;
        Interlocked.Add(ref info.BytesDownloaded, bytesRead);

        var now = Stopwatch.GetTimestamp();
        var elapsed = Stopwatch.GetElapsedTime(info.LastBroadcast, now);
        if (elapsed.TotalMilliseconds < 500) return;
        info.LastBroadcast = now;
        Broadcast();
    }

    public void Unregister(string fileKey)
    {
        if (!_streams.TryGetValue(fileKey, out var info)) return;
        if (Interlocked.Decrement(ref info.RefCount) <= 0)
            _streams.TryRemove(fileKey, out _);
        Broadcast();
    }

    private void Broadcast()
    {
        var entries = _streams.Values.Select(s => new StreamEntry
        {
            Name = s.FileName,
            Size = s.FileSize,
            Downloaded = Interlocked.Read(ref s.BytesDownloaded),
        }).ToArray();
        var message = JsonSerializer.Serialize(entries);
        websocketManager.SendMessage(WebsocketTopic.ActiveStreams, message);
    }

    private class StreamInfo(string fileName, long fileSize)
    {
        public string FileName { get; } = fileName;
        public long FileSize { get; } = fileSize;
        public long BytesDownloaded;
        public int RefCount = 1;
        public long LastBroadcast = Stopwatch.GetTimestamp();
    }

    private struct StreamEntry
    {
        public string Name { get; set; }
        public long Size { get; set; }
        public long Downloaded { get; set; }
    }
}
