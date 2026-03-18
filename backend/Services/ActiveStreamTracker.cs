using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using NzbWebDAV.Websocket;

namespace NzbWebDAV.Services;

public class ActiveStreamTracker(WebsocketManager websocketManager)
{
    private readonly ConcurrentDictionary<string, StreamInfo> _streams = new();

    public string Register(string fileName, long fileSize)
    {
        var streamId = Guid.NewGuid().ToString();
        _streams.TryAdd(streamId, new StreamInfo(fileName, fileSize));
        Broadcast();
        return streamId;
    }

    public void ReportProgress(string streamId, long bytesRead)
    {
        if (!_streams.TryGetValue(streamId, out var info)) return;
        Interlocked.Add(ref info.BytesDownloaded, bytesRead);

        var now = Stopwatch.GetTimestamp();
        var elapsed = Stopwatch.GetElapsedTime(info.LastBroadcast, now);
        if (elapsed.TotalMilliseconds < 500) return;
        info.LastBroadcast = now;
        Broadcast();
    }

    public void Unregister(string streamId)
    {
        _streams.TryRemove(streamId, out _);
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
        public long LastBroadcast = Stopwatch.GetTimestamp();
    }

    private struct StreamEntry
    {
        public string Name { get; set; }
        public long Size { get; set; }
        public long Downloaded { get; set; }
    }
}
