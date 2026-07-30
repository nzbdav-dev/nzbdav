using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using NzbWebDAV.Clients.Usenet;
using NzbWebDAV.Config;
using NzbWebDAV.Database.Models.Metrics;
using NzbWebDAV.Logging;
using NzbWebDAV.Services;
using NzbWebDAV.Services.Diagnostics;
using NzbWebDAV.Services.Metrics;
using NzbWebDAV.Services.StreamTrace;
using NzbWebDAV.Services.SupportPack;
using NzbWebDAV.Streams;
using NzbWebDAV.Tests.Database;
using NzbWebDAV.Websocket;
using Serilog;
using Serilog.Events;

namespace NzbWebDAV.Tests.Services.SupportPack;

[Collection(nameof(ConfigPathCollection))]
public sealed class SupportPackContentsTests : IDisposable
{
    private readonly string _configRoot =
        Path.Combine(Path.GetTempPath(), $"nzbdav-support-{Guid.NewGuid():N}");

    private readonly string? _previousConfigPath;

    public SupportPackContentsTests()
    {
        _previousConfigPath = Environment.GetEnvironmentVariable("CONFIG_PATH");
        Directory.CreateDirectory(_configRoot);
        Environment.SetEnvironmentVariable("CONFIG_PATH", _configRoot);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("CONFIG_PATH", _previousConfigPath);
        try { Directory.Delete(_configRoot, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public async Task Pack_KeepsWarningsThatOverflowedTheMainLogBuffer()
    {
        // Mirrors the production wiring: one small all-level buffer plus a
        // Warning-and-above lane, both fed by the same logger.
        var mainSink = new LogBufferSink(10);
        var warningBuffer = new WarningLogBuffer(new LogBufferSink(500));
        var logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Sink(mainSink)
            .WriteTo.Sink(warningBuffer.Sink, restrictedToMinimumLevel: LogEventLevel.Warning)
            .CreateLogger();

        logger.Warning("Streaming timeout executing nntp BODY command");
        for (var i = 0; i < 50; i++)
            logger.Debug("Watchtower: cycle starting {Index}", i);

        var entries = await ReadPackEntriesAsync(mainSink, warningBuffer);

        var backendLog = entries["logs/backend.log"];
        var warningsLog = entries["logs/warnings.log"];

        Assert.DoesNotContain("Streaming timeout", backendLog);
        Assert.Contains("Watchtower: cycle starting", backendLog);
        Assert.Contains("Streaming timeout executing nntp BODY command", warningsLog);
        Assert.DoesNotContain("Watchtower: cycle starting", warningsLog);

        using var manifest = JsonDocument.Parse(entries["manifest.json"]);
        var warnings = manifest.RootElement.GetProperty("warnings");
        Assert.Equal(1, warnings.GetProperty("count").GetInt32());
        Assert.Equal(500, warnings.GetProperty("capacity").GetInt32());
        Assert.Equal("included", manifest.RootElement.GetProperty("sections").GetProperty("warnings").GetString());
    }

    [Fact]
    public async Task Pack_ExplainsAnEmptyWarningLaneInsteadOfShippingAnEmptyFile()
    {
        var entries = await ReadPackEntriesAsync(new LogBufferSink(10), new WarningLogBuffer(new LogBufferSink(50)));

        // A zero-byte file reads as a broken export; say why it is empty.
        Assert.Contains("No warnings or errors", entries["logs/warnings.log"]);
    }

    [Fact]
    public async Task Pack_ReportsProcessUptimeRatherThanServiceConstructionTime()
    {
        var entries = await ReadPackEntriesAsync(new LogBufferSink(10), new WarningLogBuffer(new LogBufferSink(50)));

        using var environment = JsonDocument.Parse(entries["environment.json"]);
        var root = environment.RootElement;
        var generatedAt = root.GetProperty("generatedAtUtc").GetDateTimeOffset();
        var reportedStart = root.GetProperty("processStartedAtUtc").GetDateTimeOffset();
        var uptimeSeconds = root.GetProperty("uptimeSeconds").GetInt64();

        using var process = Process.GetCurrentProcess();
        var actualStart = new DateTimeOffset(process.StartTime.ToUniversalTime(), TimeSpan.Zero);

        // The service is constructed moments before the pack is written, so anything
        // measured from construction would report ~0. It must track the OS process.
        Assert.True(
            Math.Abs((reportedStart - actualStart).TotalSeconds) < 5,
            $"reported process start {reportedStart:O} should match {actualStart:O}");
        Assert.Equal((long)(generatedAt - reportedStart).TotalSeconds, uptimeSeconds);
    }

    [Fact]
    public async Task Pack_ReportsCpuGcAndThreadPoolCountersForBottleneckTriage()
    {
        var entries = await ReadPackEntriesAsync(new LogBufferSink(10), new WarningLogBuffer(new LogBufferSink(50)));

        using var environment = JsonDocument.Parse(entries["environment.json"]);
        var root = environment.RootElement;

        var cpu = root.GetProperty("cpu");
        Assert.True(cpu.GetProperty("processorCount").GetInt32() >= 1);
        Assert.True(cpu.GetProperty("lifetimeTotalMs").GetInt64() >= 0);

        // The on-demand probe is kept as a footnote, so it must still be readable.
        var onDemand = cpu.GetProperty("onDemandSample");
        Assert.True(onDemand.GetProperty("windowMs").GetInt64() > 0);
        Assert.True(onDemand.GetProperty("percentAllCores").GetDouble() >= 0);

        var gc = root.GetProperty("gc");
        Assert.True(gc.GetProperty("gen0Collections").GetInt32() >= 0);
        Assert.True(gc.GetProperty("gen2Collections").GetInt32() >= 0);
        Assert.True(gc.GetProperty("totalAllocatedBytes").GetInt64() > 0);
        Assert.True(gc.GetProperty("totalPauseDurationMs").GetInt64() >= 0);
        Assert.True(gc.TryGetProperty("isServerGc", out _));
        // Article buffers land on the large-object heap, so its size must be visible.
        var generations = gc.GetProperty("generations").EnumerateArray()
            .Select(entry => entry.GetProperty("name").GetString())
            .ToList();
        Assert.Contains("loh", generations);

        var threadPool = root.GetProperty("threadPool");
        Assert.True(threadPool.GetProperty("threadCount").GetInt32() >= 0);
        Assert.True(threadPool.GetProperty("pendingWorkItems").GetInt64() >= 0);
        Assert.True(threadPool.GetProperty("completedWorkItems").GetInt64() >= 0);

        // No providers are configured in this fixture, so the section is present but empty.
        Assert.Equal(JsonValueKind.Array, root.GetProperty("connections").ValueKind);
    }

    [Fact]
    public async Task Pack_ReportsPeakCpuAttributedToPlaybackRatherThanOnlyAnIdleSnapshot()
    {
        // Packs are collected after the symptom has passed, so an instantaneous sample
        // describes an idle process. These are the counters that span the incident.
        var busyAt = new DateTimeOffset(2026, 7, 27, 12, 0, 0, TimeSpan.Zero);
        var playbackAt = busyAt.AddMinutes(30);
        var runtimeUsage = new RuntimeUsageTracker(processorCount: 4);
        runtimeUsage.Record(
            TimeSpan.FromMilliseconds(16000),
            TimeSpan.FromMilliseconds(250),
            TimeSpan.FromSeconds(5),
            activeReads: 0,
            busyAt);
        runtimeUsage.Record(
            TimeSpan.FromMilliseconds(8000),
            TimeSpan.FromMilliseconds(100),
            TimeSpan.FromSeconds(5),
            activeReads: 2,
            playbackAt);

        var entries = await ReadPackEntriesAsync(
            new LogBufferSink(10),
            new WarningLogBuffer(new LogBufferSink(50)),
            runtimeUsage);

        using var environment = JsonDocument.Parse(entries["environment.json"]);
        var root = environment.RootElement;

        var sampler = root.GetProperty("runtimeSampler");
        Assert.True(sampler.GetProperty("intervalMs").GetInt64() > 0);
        Assert.Equal(2, sampler.GetProperty("sampleCount").GetInt64());
        Assert.Equal(10_000, sampler.GetProperty("windowSpanMs").GetInt64());
        Assert.Equal(playbackAt, sampler.GetProperty("lastSampleAtUtc").GetDateTimeOffset());

        var rolling = root.GetProperty("cpu").GetProperty("rolling");
        Assert.Equal(40, rolling.GetProperty("currentPercentAllCores").GetDouble());
        Assert.Equal(60, rolling.GetProperty("oneMinutePercentAllCores").GetDouble());

        var peak = rolling.GetProperty("peak");
        Assert.Equal(80, peak.GetProperty("percent").GetDouble());
        Assert.Equal(busyAt, peak.GetProperty("atUtc").GetDateTimeOffset());
        Assert.Equal(0, peak.GetProperty("activeReads").GetInt32());

        // Without the read count a peak cannot be told apart from a queue import or a
        // health sweep, which is what made the original figure unusable.
        var peakWhileReading = rolling.GetProperty("peakWhileReading");
        Assert.Equal(40, peakWhileReading.GetProperty("percent").GetDouble());
        Assert.Equal(playbackAt, peakWhileReading.GetProperty("atUtc").GetDateTimeOffset());
        Assert.Equal(2, peakWhileReading.GetProperty("activeReads").GetInt32());

        var gcRolling = root.GetProperty("gc").GetProperty("rolling");
        Assert.Equal(2, gcRolling.GetProperty("currentPausePercent").GetDouble());
        Assert.Equal(5, gcRolling.GetProperty("peak").GetProperty("percent").GetDouble());
        Assert.Equal(2, gcRolling.GetProperty("peakWhileReading").GetProperty("percent").GetDouble());
    }

    [Fact]
    public async Task Pack_ReportsRollingFiguresAsNullBeforeTheSamplerHasTicked()
    {
        var entries = await ReadPackEntriesAsync(new LogBufferSink(10), new WarningLogBuffer(new LogBufferSink(50)));

        using var environment = JsonDocument.Parse(entries["environment.json"]);
        var root = environment.RootElement;

        // A pack pulled in the first seconds of process life has nothing banked yet.
        // Null is honest here; zero would read as a genuinely idle backend.
        Assert.Equal(0, root.GetProperty("runtimeSampler").GetProperty("sampleCount").GetInt64());
        var rolling = root.GetProperty("cpu").GetProperty("rolling");
        Assert.Equal(JsonValueKind.Null, rolling.GetProperty("currentPercentAllCores").ValueKind);
        Assert.Equal(JsonValueKind.Null, rolling.GetProperty("peak").ValueKind);
        Assert.Equal(JsonValueKind.Null, rolling.GetProperty("peakWhileReading").ValueKind);
    }

    [Fact]
    public async Task Pack_IncludesStreamTracesWhileTracingIsEnabled()
    {
        var disabled = await ReadPackEntriesAsync(
            new LogBufferSink(10),
            new WarningLogBuffer(new LogBufferSink(50)),
            new StreamTraceBuffer(100, enabled: false));

        using (var disabledManifest = JsonDocument.Parse(disabled["manifest.json"]))
        {
            Assert.Equal(
                "disabled",
                disabledManifest.RootElement.GetProperty("sections").GetProperty("streamTraces").GetString());
        }

        Assert.False(disabled.ContainsKey("stream-traces/sessions.json"));
        Assert.False(disabled.ContainsKey("stream-traces/events.jsonl"));

        var buffer = new StreamTraceBuffer(100, enabled: false);
        buffer.EnableFor(TimeSpan.FromMinutes(15), 100, StreamTraceBuffer.SourceUi);
        var session = Guid.NewGuid();
        var range = buffer.RangeOpen(session, "/view/movie.mkv", "GET", 0, 99, 1000, "ua", "203.0.113.10");
        buffer.Seek(session, 50);
        buffer.Segment(session, "provider-a", SegmentFetch.FetchStatus.Ok, 12, 0, "msgid@a");
        buffer.RangeEnd(session, range, ReadSession.EndReasonCode.Completed, 100);

        var enabled = await ReadPackEntriesAsync(
            new LogBufferSink(10),
            new WarningLogBuffer(new LogBufferSink(50)),
            buffer);

        using var enabledManifest = JsonDocument.Parse(enabled["manifest.json"]);
        Assert.Equal(3, enabledManifest.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(
            "included",
            enabledManifest.RootElement.GetProperty("sections").GetProperty("streamTraces").GetString());
        Assert.False(enabledManifest.RootElement.GetProperty("streamTraces").GetProperty("overflowed").GetBoolean());

        Assert.Contains("/view/movie.mkv", enabled["stream-traces/sessions.json"]);
        Assert.Contains("RangeOpen", enabled["stream-traces/events.jsonl"]);
        Assert.Contains("Seek", enabled["stream-traces/events.jsonl"]);
        Assert.Contains("[IP-", enabled["stream-traces/events.jsonl"]);
        Assert.DoesNotContain("203.0.113.10", enabled["stream-traces/events.jsonl"]);

        using var environment = JsonDocument.Parse(enabled["environment.json"]);
        var tracing = environment.RootElement.GetProperty("streamTracing");
        Assert.True(tracing.GetProperty("enabled").GetBoolean());
        Assert.Equal("ui", tracing.GetProperty("source").GetString());
        Assert.True(tracing.GetProperty("expiresAtUnixMs").GetInt64() > 0);
        Assert.True(tracing.GetProperty("eventCount").GetInt64() >= 4);
        Assert.False(tracing.GetProperty("overflowed").GetBoolean());
    }

    [Fact]
    public async Task Pack_FlagsTruncatedStreamTraceCaptures()
    {
        var buffer = new StreamTraceBuffer(100, enabled: false);
        buffer.EnableFor(TimeSpan.FromMinutes(15), 100, StreamTraceBuffer.SourceUi);
        var session = Guid.NewGuid();
        for (var i = 0; i < 150; i++)
            buffer.Seek(session, i);

        var pack = await ReadPackEntriesAsync(
            new LogBufferSink(10),
            new WarningLogBuffer(new LogBufferSink(50)),
            buffer);

        Assert.True(pack.ContainsKey("stream-traces/OVERFLOW.txt"));
        Assert.Contains("INCOMPLETE", pack["stream-traces/OVERFLOW.txt"]);

        using var manifest = JsonDocument.Parse(pack["manifest.json"]);
        Assert.Equal(3, manifest.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(
            "included-truncated",
            manifest.RootElement.GetProperty("sections").GetProperty("streamTraces").GetString());
        var streamTraces = manifest.RootElement.GetProperty("streamTraces");
        Assert.True(streamTraces.GetProperty("overflowed").GetBoolean());
        Assert.Equal(150, streamTraces.GetProperty("eventCount").GetInt64());
        Assert.Equal(100, streamTraces.GetProperty("retainedEventCount").GetInt64());
        Assert.Equal(50, streamTraces.GetProperty("overwrittenEventCount").GetInt64());

        using var sessions = JsonDocument.Parse(pack["stream-traces/sessions.json"]);
        Assert.True(sessions.RootElement.GetArrayLength() >= 1);
        Assert.Contains("retainedEventCount", pack["stream-traces/sessions.json"]);
        Assert.Contains("eventsComplete", pack["stream-traces/sessions.json"]);
    }

    [Fact]
    public async Task Pack_IncludesRetainedStreamTracesUntilDiscarded()
    {
        var buffer = new StreamTraceBuffer(100, enabled: false);
        buffer.EnableFor(TimeSpan.FromMinutes(15), 100, StreamTraceBuffer.SourceUi);
        var session = Guid.NewGuid();
        buffer.RangeOpen(session, "/view/retained.mkv", "GET", 0, 99, 1000, null, null);
        buffer.StopRecording();

        var retained = await ReadPackEntriesAsync(
            new LogBufferSink(10),
            new WarningLogBuffer(new LogBufferSink(50)),
            buffer);

        using (var manifest = JsonDocument.Parse(retained["manifest.json"]))
        {
            Assert.Equal(
                "included",
                manifest.RootElement.GetProperty("sections").GetProperty("streamTraces").GetString());
        }

        Assert.Contains("/view/retained.mkv", retained["stream-traces/sessions.json"]);
        Assert.Contains("RangeOpen", retained["stream-traces/events.jsonl"]);

        using (var environment = JsonDocument.Parse(retained["environment.json"]))
        {
            var tracing = environment.RootElement.GetProperty("streamTracing");
            Assert.False(tracing.GetProperty("enabled").GetBoolean());
            Assert.True(tracing.GetProperty("retained").GetBoolean());
            Assert.True(tracing.GetProperty("retainedUntilUnixMs").GetInt64() > 0);
            Assert.True(tracing.GetProperty("eventCount").GetInt64() > 0);
        }

        buffer.Discard();
        var discarded = await ReadPackEntriesAsync(
            new LogBufferSink(10),
            new WarningLogBuffer(new LogBufferSink(50)),
            buffer);

        using var discardedManifest = JsonDocument.Parse(discarded["manifest.json"]);
        Assert.Equal(
            "disabled",
            discardedManifest.RootElement.GetProperty("sections").GetProperty("streamTraces").GetString());
        Assert.False(discarded.ContainsKey("stream-traces/sessions.json"));
        Assert.False(discarded.ContainsKey("stream-traces/events.jsonl"));
    }

    private static Task<Dictionary<string, string>> ReadPackEntriesAsync(
        LogBufferSink logBuffer,
        WarningLogBuffer warningBuffer,
        RuntimeUsageTracker? runtimeUsage = null) =>
        ReadPackEntriesAsync(logBuffer, warningBuffer, new StreamTraceBuffer(100, enabled: false), runtimeUsage);

    private static async Task<Dictionary<string, string>> ReadPackEntriesAsync(
        LogBufferSink logBuffer,
        WarningLogBuffer warningBuffer,
        StreamTraceBuffer streamTraceBuffer,
        RuntimeUsageTracker? runtimeUsage = null)
    {
        var configManager = new ConfigManager();
        var websocketManager = new WebsocketManager();
        var usenet = new UsenetStreamingClient(
            configManager,
            websocketManager,
            new ProviderUsageTracker(),
            new MetricsWriter(),
            new ProviderBytesTracker(),
            streamTraceBuffer,
            new ActiveReadRegistry());

        var service = new SupportPackService(
            logBuffer,
            warningBuffer,
            configManager,
            new MetricsWriter(),
            new ProviderBytesTracker(),
            new ProviderLatencyTracker(),
            usenet,
            new ArticleMissNegativeCache(configManager),
            new InFlightArticleBudget(64 * 1024 * 1024),
            streamTraceBuffer,
            runtimeUsage ?? new RuntimeUsageTracker());

        using var memory = new MemoryStream();
        await service.WriteAsync(memory, CancellationToken.None);

        memory.Position = 0;
        using var archive = new ZipArchive(memory, ZipArchiveMode.Read);
        var entries = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in archive.Entries)
        {
            await using var stream = entry.Open();
            using var reader = new StreamReader(stream, Encoding.UTF8);
            entries[entry.FullName] = await reader.ReadToEndAsync();
        }

        return entries;
    }
}
