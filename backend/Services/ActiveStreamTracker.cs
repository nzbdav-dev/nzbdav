using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using NzbWebDAV.Websocket;

namespace NzbWebDAV.Services;

public class ActiveStreamTracker(WebsocketManager websocketManager)
{
    private readonly ConcurrentDictionary<string, StreamInfo> _streams = new();

    public string Register(string fileName)
    {
        var streamId = Guid.NewGuid().ToString();
        _streams.TryAdd(streamId, new StreamInfo(fileName));
        Broadcast();
        return streamId;
    }

    public void ReportProgress(string streamId, long bytesRead)
    {
        if (!_streams.TryGetValue(streamId, out var info)) return;
        var totalBytes = Interlocked.Add(ref info.BytesDownloaded, bytesRead);

        var now = Stopwatch.GetTimestamp();
        var elapsed = Stopwatch.GetElapsedTime(info.LastBroadcast, now);
        if (elapsed.TotalMilliseconds < 500) return;

        var prevBytes = Interlocked.Exchange(ref info.LastBroadcastBytes, totalBytes);
        info.LastBroadcast = now;
        info.Speed = (long)((totalBytes - prevBytes) / elapsed.TotalSeconds);
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
            Downloaded = Interlocked.Read(ref s.BytesDownloaded),
            Speed = s.Speed,
        }).ToArray();
        var message = JsonSerializer.Serialize(entries);
        websocketManager.SendMessage(WebsocketTopic.ActiveStreams, message);
    }

    private class StreamInfo(string fileName)
    {
        public string FileName { get; } = fileName;
        public long BytesDownloaded;
        public long LastBroadcastBytes;
        public long LastBroadcast = Stopwatch.GetTimestamp();
        public long Speed;
    }

    private struct StreamEntry
    {
        public string Name { get; set; }
        public long Downloaded { get; set; }
        public long Speed { get; set; }
    }
}
