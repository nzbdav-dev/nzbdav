using System.Diagnostics;
using System.IO.Compression;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NzbWebDAV.Clients.Usenet;
using NzbWebDAV.Config;
using NzbWebDAV.Database;
using NzbWebDAV.Database.Models.Metrics;
using NzbWebDAV.Logging;
using NzbWebDAV.Services.Diagnostics;
using NzbWebDAV.Services.Metrics;
using NzbWebDAV.Services.StreamTrace;
using NzbWebDAV.Streams;
using Serilog;

namespace NzbWebDAV.Services.SupportPack;

public sealed class SupportPackService(
    LogBufferSink logBuffer,
    WarningLogBuffer warningLogBuffer,
    ConfigManager configManager,
    MetricsWriter metricsWriter,
    ProviderBytesTracker bytesTracker,
    ProviderLatencyTracker latencyTracker,
    UsenetStreamingClient usenetStreamingClient,
    ArticleMissNegativeCache articleMissCache,
    InFlightArticleBudget inFlightArticleBudget,
    StreamTraceBuffer streamTraceBuffer,
    RuntimeUsageTracker runtimeUsage)
{
    private const long MinuteMs = 60_000;
    private const long HourMs = 60 * MinuteMs;
    private const long DayMs = 24 * HourMs;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly JsonSerializerOptions CompactJsonOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    internal async Task WriteAsync(Stream output, CancellationToken cancellationToken)
    {
        var generatedAt = DateTimeOffset.UtcNow;
        var config = configManager.GetDiagnosticSnapshot();
        var redactor = new SupportPackRedactor(CollectSecrets(config));
        var logSnapshot = logBuffer.Snapshot(logBuffer.Capacity, null, null, null, null);
        var warningSink = warningLogBuffer.Sink;
        var warningSnapshot = warningSink.Snapshot(warningSink.Capacity, null, null, null, null);
        var sectionStatus = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["logs"] = "included",
            ["warnings"] = "included",
            ["configuration"] = "included",
            ["environment"] = "included",
        };

        using var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
        await WriteTextAsync(archive, "README.txt", BuildReadme(), cancellationToken).ConfigureAwait(false);
        await WriteTextAsync(
            archive,
            "logs/backend.log",
            redactor.RedactText(FormatLogs(logSnapshot.Entries)),
            cancellationToken).ConfigureAwait(false);
        await WriteTextAsync(
            archive,
            "logs/warnings.log",
            warningSnapshot.Entries.Count == 0
                ? "No warnings or errors were logged since this backend started.\n"
                : redactor.RedactText(FormatLogs(warningSnapshot.Entries)),
            cancellationToken).ConfigureAwait(false);
        await WriteJsonAsync(
            archive,
            "configuration.json",
            BuildConfiguration(config, redactor),
            redactor,
            cancellationToken).ConfigureAwait(false);
        await WriteJsonAsync(
            archive,
            "environment.json",
            await BuildEnvironmentAsync(generatedAt, cancellationToken).ConfigureAwait(false),
            redactor,
            cancellationToken).ConfigureAwait(false);

        try
        {
            var metrics = await BuildMetricsAsync(generatedAt, cancellationToken).ConfigureAwait(false);
            await WriteJsonAsync(archive, "metrics/recent.json", metrics, redactor, cancellationToken)
                .ConfigureAwait(false);
            sectionStatus["metrics"] = "included";
        }
        catch
        {
            sectionStatus["metrics"] = "unavailable";
        }

        var traceSnapshot = streamTraceBuffer.CaptureSnapshot(50);
        if (traceSnapshot.Status.Enabled || traceSnapshot.Status.EventCount > 0)
        {
            await WriteJsonAsync(
                archive,
                "stream-traces/sessions.json",
                traceSnapshot.Sessions.Select(s => new
                {
                    sessionId = s.SessionId,
                    path = s.Path,
                    firstAt = s.FirstAt,
                    lastAt = s.LastAt,
                    eventCount = s.EventCount,
                    retainedEventCount = s.RetainedEventCount,
                    eventsComplete = s.EventsComplete,
                    lastKind = s.LastKind,
                }),
                redactor,
                cancellationToken).ConfigureAwait(false);
            await WriteTraceEventsAsync(
                archive,
                "stream-traces/events.jsonl",
                traceSnapshot.Events,
                redactor,
                cancellationToken).ConfigureAwait(false);
            if (traceSnapshot.Status.Overflowed)
            {
                await WriteTextAsync(
                    archive,
                    "stream-traces/OVERFLOW.txt",
                    BuildOverflowNote(traceSnapshot.Status),
                    cancellationToken).ConfigureAwait(false);
            }

            sectionStatus["streamTraces"] = traceSnapshot.Status.Overflowed
                ? "included-truncated"
                : "included";
        }
        else
        {
            sectionStatus["streamTraces"] = "disabled";
        }

        await WriteJsonAsync(
            archive,
            "manifest.json",
            await BuildManifestAsync(
                    generatedAt,
                    logSnapshot,
                    warningSnapshot,
                    sectionStatus,
                    redactor,
                    traceSnapshot,
                    cancellationToken)
                .ConfigureAwait(false),
            redactor,
            cancellationToken).ConfigureAwait(false);
    }

    internal static string BuildReadme() =>
        """
        NzbDAV technical support pack

        This archive contains the current backend in-memory log buffer, a redacted
        active Settings snapshot, runtime information, and aggregate metrics.
        Backend logs are cleared when NzbDAV restarts. Frontend and container logs
        are not included.

        logs/backend.log holds the most recent events of every level, so a busy or
        debug-level install can push older events out of it. logs/warnings.log keeps
        the last 500 warnings and errors separately for that reason - check it first
        when the main log looks like it only contains routine activity.

        environment.json reports CPU and GC pause figures, thread pool occupancy, and
        per-provider connection-pool state with lifetime churn. Read cpu.rolling and
        gc.rolling first: a background sampler records them every few seconds, so
        their peaks span the whole time the backend has been running rather than the
        moment the pack was collected. peakWhileReading only counts samples taken
        while a read was in flight, which tells playback cost apart from container
        startup, a queue import or a health sweep. cpu.onDemandSample is a half-second
        window measured while this pack was written - packs are usually collected
        after the symptom has passed, so treat it as a footnote, not the headline.
        Prefer the cumulative GC counters (totalPauseDurationMs, totalAllocatedBytes,
        collection counts) over pauseTimePercentage, which reflects only the most
        recent collection.

        stream-traces/ is included while developer stream tracing is enabled or while
        a stopped capture is retained for one hour (Settings → Support, or
        STREAM_TRACE_EVENTS). Tracing is opt-in, memory-only, and resets on restart.
        Check manifest.json → streamTraces for capacity, retained/overwritten counts,
        and overflowed. When the ring wraps, stream-traces/OVERFLOW.txt explains how
        much of the reproduction was discarded; sessions.json reports eventsComplete
        per session because session summaries can outlive their retained events.
        RangeEnd events carry stall attribution for the range that just finished:
        connWaitMs (waiting for an NNTP connection),
        providerWaitMs (waiting for provider response headers), bodyDrainMs (reading
        article bodies), consumerWaitMs (playback starved waiting for prefetch), and
        clientWriteMs (blocked writing to the player). They overlap, because segments
        are fetched concurrently, so read them as shares of the range rather than a
        breakdown that sums to its duration.

        Each RangeEnd also reports fetches, the number of segment fetches attributed to
        that range. providerWaitMs is an aggregate across concurrent fetches, so it can
        exceed wall clock; compare providerWaitMs / fetches (average provider wait) and
        providerWaitMs / elapsed RangeOpen-to-RangeEnd time (implied concurrency) with
        the configured pool size. Attribution is by the range that started each fetch,
        including fetches that finish after an aborted RangeEnd, so late work is not
        billed to the next range. Pair RangeOpen and RangeEnd by rangeGeneration when
        requests overlap or finish out of order.

        Trace connWaitMs and the connection pool's GateWaitMs are not comparable. Trace
        stalls are scoped to read sessions captured while tracing was active; pool churn
        counters are process-wide and cumulative, so they also include queue imports and
        health sweeps. On a scan-heavy install the two legitimately differ by orders of
        magnitude.

        metrics/recent.json → latency24Hours projects one-minute response, pool-wait,
        and permit-wait histograms into five-minute buckets. Percentiles are bucket
        upper bounds (not exact sample percentiles). Only successful NNTP responses are
        counted; body-drain time is excluded from response. Compare:
        - high response with low pool-wait/permit-wait → provider/server latency
        - high provider pool-wait → that provider's connections are saturated/churning
        - high streaming/queue permit-wait → that workload's connection cap is saturated
        - high trace consumerWaitMs with low values in all three → prefetch/consumer pacing

        The archive deliberately excludes database files, backups, blobs/NZBs,
        environment files, session/API key files, crash dumps, and segment-cache
        data. Credentials, API keys, tokens, URL credentials and sensitive URL query
        values are redacted. IP addresses are pseudonymized.

        File names, filesystem paths, account usernames, DNS hostnames, and
        non-secret URL paths can remain for troubleshooting. Share this archive only
        with trusted NzbDAV support.
        """;

    private static object BuildConfiguration(
        IReadOnlyList<ConfigDiagnosticSnapshot> config,
        SupportPackRedactor redactor) =>
        new
        {
            settings = config.Select(item => new
            {
                key = item.Key,
                value = redactor.RedactConfigurationValue(item.Key, item.Value),
                source = item.Source,
                environmentVariable = item.EnvironmentVariableName,
            }),
        };

    private async Task<object> BuildEnvironmentAsync(
        DateTimeOffset generatedAt,
        CancellationToken cancellationToken)
    {
        ThreadPool.GetMinThreads(out var minWorkerThreads, out var minIoThreads);
        ThreadPool.GetMaxThreads(out var maxWorkerThreads, out var maxIoThreads);
        var configPath = DavDatabaseContext.ConfigPath;
        var root = Path.GetPathRoot(Path.GetFullPath(configPath)) ?? configPath;
        var drive = new DriveInfo(root);
        var uptime = ProcessUptime();
        var streamTracing = streamTraceBuffer.GetStatus();
        var usage = runtimeUsage.Snapshot();
        var cpu = await BuildCpuDiagnosticsAsync(usage, uptime, cancellationToken).ConfigureAwait(false);

        return new
        {
            generatedAtUtc = generatedAt,
            appVersion = ConfigManager.AppVersion,
            commit = Environment.GetEnvironmentVariable("NZBDAV_COMMIT_SHA"),
            uptimeSeconds = (long)uptime.TotalSeconds,
            processStartedAtUtc = generatedAt - uptime,
            runtime = new
            {
                framework = RuntimeInformation.FrameworkDescription,
                os = RuntimeInformation.OSDescription,
                osArchitecture = RuntimeInformation.OSArchitecture.ToString(),
                processArchitecture = RuntimeInformation.ProcessArchitecture.ToString(),
                processorCount = Environment.ProcessorCount,
                workingSetBytes = Environment.WorkingSet,
                gcTotalMemoryBytes = GC.GetTotalMemory(forceFullCollection: false),
                inFlightArticleBytes = inFlightArticleBudget.LeasedBytes,
                inFlightArticleBudgetBytes = inFlightArticleBudget.CapBytes,
                inFlightArticleThrottleEvents = inFlightArticleBudget.ThrottleEvents,
                timeZone = TimeZoneInfo.Local.Id,
            },
            runtimeSampler = BuildRuntimeSamplerDiagnostics(usage),
            cpu,
            gc = BuildGcDiagnostics(usage),
            threadPool = new
            {
                minWorkerThreads,
                minIoThreads,
                maxWorkerThreads,
                maxIoThreads,
                threadCount = ThreadPool.ThreadCount,
                pendingWorkItems = ThreadPool.PendingWorkItemCount,
                completedWorkItems = ThreadPool.CompletedWorkItemCount,
                processThreadCount = ProcessThreadCount(),
            },
            connections = BuildConnectionDiagnostics(),
            storage = new
            {
                configPath,
                configDatabaseBytes = FileSize(DavDatabaseContext.DatabaseFilePath),
                metricsDatabaseBytes = FileSize(MetricsDbContext.DatabaseFilePath),
                availableFreeSpaceBytes = drive.IsReady ? drive.AvailableFreeSpace : (long?)null,
            },
            streamTracing = new
            {
                enabled = streamTracing.Enabled,
                retained = streamTracing.Retained,
                source = streamTracing.Source,
                expiresAtUnixMs = streamTracing.ExpiresAtUnixMs,
                retainedUntilUnixMs = streamTracing.RetainedUntilUnixMs,
                capacity = streamTracing.Capacity,
                eventCount = streamTracing.EventCount,
                sessionCount = streamTracing.SessionCount,
                retainedEventCount = streamTracing.RetainedEventCount,
                overwrittenEventCount = streamTracing.OverwrittenEventCount,
                oldestRetainedSequence = streamTracing.OldestRetainedSequence,
                newestRetainedSequence = streamTracing.NewestRetainedSequence,
                oldestRetainedAtUnixMs = streamTracing.OldestRetainedAtUnixMs,
                newestRetainedAtUnixMs = streamTracing.NewestRetainedAtUnixMs,
                overflowed = streamTracing.Overflowed,
            },
            environment = new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["LOG_LEVEL"] = Environment.GetEnvironmentVariable("LOG_LEVEL"),
                ["LOG_BUFFER_SIZE"] = Environment.GetEnvironmentVariable("LOG_BUFFER_SIZE"),
                ["STREAM_TRACE_EVENTS"] = Environment.GetEnvironmentVariable("STREAM_TRACE_EVENTS"),
                ["TZ"] = Environment.GetEnvironmentVariable("TZ"),
                ["PUID"] = Environment.GetEnvironmentVariable("PUID"),
                ["PGID"] = Environment.GetEnvironmentVariable("PGID"),
                ["MAX_REQUEST_BODY_SIZE"] = Environment.GetEnvironmentVariable("MAX_REQUEST_BODY_SIZE"),
            },
        };
    }

    /// <summary>
    /// Health of the background sampler that produces the rolling CPU and GC figures.
    /// A <c>lastSampleAtUtc</c> far behind the pack's generation time, or a
    /// <c>windowSpanMs</c> well under a minute, means the rolling numbers are stale or
    /// cover a partial window and should be read with that in mind.
    /// </summary>
    private static object BuildRuntimeSamplerDiagnostics(RuntimeUsageSnapshot usage) =>
        new
        {
            intervalMs = (long)RuntimeUsageSampler.TickInterval.TotalMilliseconds,
            sampleCount = usage.SampleCount,
            windowSpanMs = usage.WindowSpanMs,
            lastSampleAtUtc = usage.LastSampleAtUtc,
        };

    /// <summary>
    /// CPU cost of the process. Read <c>rolling</c> first. A pack is nearly always
    /// collected after the symptom has passed, so <c>onDemandSample</c> — measured while
    /// the pack was being written — usually describes an idle process and answers the
    /// wrong question. The peaks span the whole process lifetime, and
    /// <c>peakWhileReading</c> only counts samples taken with a read in flight, which
    /// separates playback cost from container startup, a queue import or a health sweep.
    /// Percentages are of the whole machine, so 100 means every core busy.
    /// </summary>
    private static async Task<object> BuildCpuDiagnosticsAsync(
        RuntimeUsageSnapshot usage,
        TimeSpan uptime,
        CancellationToken cancellationToken)
    {
        var cores = Math.Max(1, Environment.ProcessorCount);
        var rolling = new
        {
            currentPercentAllCores = usage.Cpu.CurrentPercent,
            oneMinutePercentAllCores = usage.Cpu.OneMinutePercent,
            peak = BuildPeak(usage.Cpu.Peak),
            peakWhileReading = BuildPeak(usage.Cpu.PeakWhileReading),
        };

        try
        {
            var before = Environment.CpuUsage;
            var sampleWindow = TimeSpan.FromMilliseconds(500);
            await Task.Delay(sampleWindow, cancellationToken).ConfigureAwait(false);
            var after = Environment.CpuUsage;

            var sampleMs = (after.TotalTime - before.TotalTime).TotalMilliseconds;
            return new
            {
                processorCount = cores,
                rolling,
                lifetimeUserMs = (long)after.UserTime.TotalMilliseconds,
                lifetimePrivilegedMs = (long)after.PrivilegedTime.TotalMilliseconds,
                lifetimeTotalMs = (long)after.TotalTime.TotalMilliseconds,
                lifetimePercentAllCores = Percent(
                    after.TotalTime.TotalMilliseconds, uptime.TotalMilliseconds * cores),
                onDemandSample = new
                {
                    windowMs = (long)sampleWindow.TotalMilliseconds,
                    percentAllCores = Percent(sampleMs, sampleWindow.TotalMilliseconds * cores),
                    percentOneCore = Percent(sampleMs, sampleWindow.TotalMilliseconds),
                },
            };
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            Log.Debug(e, "Support pack: could not sample CPU usage");
            // The rolling figures come from counters already banked by the sampler, so
            // they survive a failure to read the lifetime totals here.
            return new { processorCount = cores, rolling, unavailable = true };
        }

        static double? Percent(double used, double capacity) =>
            capacity <= 0 ? null : Math.Round(used / capacity * 100, 1);
    }

    private static object? BuildPeak(RuntimeUsagePeak? peak) =>
        peak is null
            ? null
            : new { percent = peak.Percent, atUtc = peak.AtUtc, activeReads = peak.ActiveReads };

    /// <summary>
    /// GC shape and cost. Generation sizes include the large-object heap, which is where
    /// article buffers land. Prefer the cumulative <c>totalPauseDurationMs</c> and the
    /// rolling block over <c>pauseTimePercentage</c>, which reflects only the most recent
    /// collection. Pause percentages are of wall clock, not of core time, because a pause
    /// stops the whole process.
    /// </summary>
    private static object BuildGcDiagnostics(RuntimeUsageSnapshot usage)
    {
        var rolling = new
        {
            currentPausePercent = usage.GcPause.CurrentPercent,
            oneMinutePausePercent = usage.GcPause.OneMinutePercent,
            peak = BuildPeak(usage.GcPause.Peak),
            peakWhileReading = BuildPeak(usage.GcPause.PeakWhileReading),
        };

        try
        {
            var info = GC.GetGCMemoryInfo();
            var generations = new List<object>(info.GenerationInfo.Length);
            for (var generation = 0; generation < info.GenerationInfo.Length; generation++)
            {
                var entry = info.GenerationInfo[generation];
                generations.Add(new
                {
                    name = GenerationName(generation),
                    sizeAfterBytes = entry.SizeAfterBytes,
                    fragmentationAfterBytes = entry.FragmentationAfterBytes,
                });
            }

            return new
            {
                isServerGc = GCSettings.IsServerGC,
                latencyMode = GCSettings.LatencyMode.ToString(),
                rolling,
                gen0Collections = GC.CollectionCount(0),
                gen1Collections = GC.CollectionCount(1),
                gen2Collections = GC.CollectionCount(2),
                totalAllocatedBytes = GC.GetTotalAllocatedBytes(precise: false),
                totalPauseDurationMs = (long)GC.GetTotalPauseDuration().TotalMilliseconds,
                pauseTimePercentage = info.PauseTimePercentage,
                heapSizeBytes = info.HeapSizeBytes,
                committedBytes = info.TotalCommittedBytes,
                generations,
            };
        }
        catch (Exception e)
        {
            Log.Debug(e, "Support pack: could not read GC diagnostics");
            return new { rolling, unavailable = true };
        }

        static string GenerationName(int generation) => generation switch
        {
            0 => "gen0",
            1 => "gen1",
            2 => "gen2",
            3 => "loh",
            4 => "poh",
            _ => $"gen{generation}",
        };
    }

    /// <summary>
    /// Live pool occupancy plus lifetime churn per provider. High
    /// <c>connectionsDestroyed</c> against low <c>connectionsReused</c> means connections
    /// are being replaced rather than pooled, and the handshake wait shows what that costs.
    /// </summary>
    private object BuildConnectionDiagnostics()
    {
        try
        {
            return usenetStreamingClient.GetProviderConnectionSnapshots()
                .Select(snapshot => new
                {
                    providerKey = snapshot.MetricsKey,
                    host = snapshot.Host,
                    providerType = snapshot.ProviderType.ToString(),
                    snapshot.LiveConnections,
                    snapshot.IdleConnections,
                    snapshot.ActiveConnections,
                    snapshot.AvailableConnections,
                    snapshot.PendingSelections,
                    churn = new
                    {
                        snapshot.Churn.ConnectionsOpened,
                        snapshot.Churn.ConnectionsReused,
                        snapshot.Churn.ConnectionsDestroyed,
                        snapshot.Churn.StaleEvictions,
                        snapshot.Churn.HandshakeFailures,
                        snapshot.Churn.GateWaitMs,
                        snapshot.Churn.HandshakeWaitMs,
                    },
                })
                .ToList<object>();
        }
        catch (Exception e)
        {
            Log.Debug(e, "Support pack: could not read connection-pool diagnostics");
            return Array.Empty<object>();
        }
    }

    private static int? ProcessThreadCount()
    {
        try
        {
            using var process = Process.GetCurrentProcess();
            return process.Threads.Count;
        }
        catch (Exception e)
        {
            Log.Debug(e, "Support pack: could not read the process thread count");
            return null;
        }
    }

    private async Task<object> BuildMetricsAsync(DateTimeOffset generatedAt, CancellationToken cancellationToken)
    {
        var now = generatedAt.ToUnixTimeMilliseconds();
        var since24Hours = now - DayMs;
        var since7Days = now - 7 * DayMs;
        var providers = configManager.GetUsenetProviderConfig().Providers;
        var nicknames = providers
            .Where(provider => provider.ProviderId != Guid.Empty)
            .ToDictionary(
                UsenetProviderIdentity.MetricsKey,
                provider => string.IsNullOrWhiteSpace(provider.Nickname) ? null : provider.Nickname,
                StringComparer.Ordinal);

        try
        {
            await metricsWriter.FlushNowAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Support pack best-effort metrics flush failed; continuing with queued/tracker data");
        }

        List<MetricEvent> persistedLatency;
        IReadOnlyList<MetricEvent> queuedLatency;
        IReadOnlyList<LatencyFlushItem> trackerLatency;
        await using (var diagnosticsLease =
                     await metricsWriter.AcquireDiagnosticSnapshotLeaseAsync(cancellationToken).ConfigureAwait(false))
        {
            (queuedLatency, trackerLatency) =
                metricsWriter.CaptureLatencyHandoff(latencyTracker.SnapshotUnpersisted);

            await using var db = new MetricsDbContext();
            persistedLatency = await db.MetricEvents
                .Where(row => row.Kind == "latency" && row.At >= since24Hours)
                .AsNoTracking()
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        var malformedRows = 0;
        var normalized = new List<LatencySupportPackProjection.NormalizedLatencyRow>();
        normalized.AddRange(LatencySupportPackProjection.FromMetricEvents(
            persistedLatency, LatencySupportPackProjection.SourcePersisted, out var persistedMalformed));
        malformedRows += persistedMalformed;
        normalized.AddRange(LatencySupportPackProjection.FromMetricEvents(
            queuedLatency, LatencySupportPackProjection.SourceQueued, out var queuedMalformed));
        malformedRows += queuedMalformed;
        normalized.AddRange(LatencySupportPackProjection.FromFlushItems(trackerLatency));
        var latency24Hours = LatencySupportPackProjection.BuildLatency24Hours(
            LatencySupportPackProjection.Deduplicate(normalized),
            nicknames,
            malformedRows);

        await using var metricsDb = new MetricsDbContext();
        var minuteRows = await metricsDb.ThroughputMinutes
            .Where(row => row.Minute >= since24Hours)
            .Select(row => new
            {
                row.Minute,
                row.Articles,
                row.Misses,
                row.Errors,
                row.BytesFetched,
                row.BytesServed,
            })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var providerHours = await metricsDb.ProviderHourly
            .Where(row => row.Hour >= since7Days)
            .Select(row => new
            {
                row.Hour,
                row.Provider,
                row.Articles,
                row.Misses,
                row.Errors,
                row.Retries,
                row.BytesFetched,
                row.FailoverSaves,
            })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var circuitTransitions = await metricsDb.MetricEvents
            .Where(row => row.Kind == "circuit" && row.At >= since7Days)
            .Select(row => new { row.At, row.Tag1, row.Tag2, row.Num })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var failover = await metricsDb.FailoverHourly
            .Where(row => row.Hour >= since7Days)
            .GroupBy(row => row.Reason)
            .Select(group => new { reason = group.Key.ToString(), count = group.Sum(row => row.Count) })
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var usageHours = await ProviderUsageHelper.ReadRecentHoursAsync(
            providers.Where(provider => provider.ProviderId != Guid.Empty).Select(UsenetProviderIdentity.MetricsKey))
            .ConfigureAwait(false);
        var usage = providers
            .Where(provider => provider.ProviderId != Guid.Empty)
            .Select(provider =>
            {
                var key = UsenetProviderIdentity.MetricsKey(provider);
                usageHours.TryGetValue(key, out var hours);
                var bytesUsed = ProviderUsageHelper.ComputeUsage(bytesTracker, provider);
                var (bytesPerDay, daysRemaining) = ProviderUsageHelper.ComputeBurnRate(provider, bytesUsed, hours);
                return new
                {
                    providerKey = key,
                    nickname = nicknames.GetValueOrDefault(key),
                    bytesUsed,
                    byteLimit = provider.ByteLimit,
                    overLimit = ProviderUsageHelper.IsOverLimit(bytesTracker, provider),
                    bytesPerDay,
                    daysRemaining,
                };
            })
            .ToList();

        var circuitStates = usenetStreamingClient.GetProviderCircuitSnapshots()
            .Select(snapshot => new
            {
                providerKey = snapshot.MetricsKey,
                nickname = nicknames.GetValueOrDefault(snapshot.MetricsKey),
                state = snapshot.Breaker.State.ToString(),
                snapshot.Breaker.CooldownRemainingSeconds,
                snapshot.Breaker.LastFailureReason,
                snapshot.Breaker.TripCount,
                snapshot.Breaker.FailureCount,
                snapshot.Breaker.ArticleMissCount,
            })
            .ToList();
        var stats = metricsWriter.Stats;
        return new
        {
            generatedAtUtc = generatedAt,
            outage24Hours = new
            {
                bucketSizeMs = 5 * MinuteMs,
                throughput = minuteRows
                    .GroupBy(row => row.Minute - row.Minute % (5 * MinuteMs))
                    .OrderBy(group => group.Key)
                    .Select(group => new
                    {
                        bucket = group.Key,
                        articles = group.Sum(row => row.Articles),
                        misses = group.Sum(row => row.Misses),
                        errors = group.Sum(row => row.Errors),
                        bytesFetched = group.Sum(row => row.BytesFetched),
                        bytesServed = group.Sum(row => row.BytesServed),
                    }),
            },
            consumption7Days = new
            {
                providerHours = providerHours
                    .OrderBy(row => row.Hour)
                    .Select(row => new
                    {
                        row.Hour,
                        providerKey = row.Provider,
                        nickname = nicknames.GetValueOrDefault(row.Provider),
                        row.Articles,
                        row.Misses,
                        row.Errors,
                        row.Retries,
                        row.BytesFetched,
                        row.FailoverSaves,
                    }),
                providerUsage = usage,
            },
            circuits = new
            {
                current = circuitStates,
                transitions = circuitTransitions
                    .Where(row => row.Tag1 is not null && row.Tag2 is not null)
                    .Select(row => new
                    {
                        at = row.At,
                        providerKey = row.Tag1,
                        nickname = nicknames.GetValueOrDefault(row.Tag1!),
                        state = row.Tag2,
                        cooldownMs = row.Num,
                    }),
            },
            failoverReasons = failover,
            articleMissCache = new
            {
                hits = articleMissCache.Hits,
                skips = articleMissCache.Skips,
                entries = articleMissCache.Entries,
            },
            latency24Hours,
            metricsHealth = new
            {
                queued = stats.QueuedFetches + stats.QueuedEvents + stats.QueuedSessions + stats.QueuedFailoverMisses,
                dropped = stats.DroppedFetches + stats.DroppedEvents + stats.DroppedSessions + stats.DroppedFailoverMisses,
                stats.LastSuccessfulFlushAtMs,
                stats.LastFlushError,
                latencyPendingBuckets = latencyTracker.PendingBuckets,
                latencyDroppedObservations = latencyTracker.DroppedObservations,
            },
        };
    }

    private async Task<object> BuildManifestAsync(
        DateTimeOffset generatedAt,
        LogSnapshot logs,
        LogSnapshot warnings,
        IReadOnlyDictionary<string, string> sectionStatus,
        SupportPackRedactor redactor,
        StreamTraceSnapshot traceSnapshot,
        CancellationToken cancellationToken)
    {
        var (mainMigration, metricsMigration) = await ReadMigrationsAsync(cancellationToken).ConfigureAwait(false);
        return new
        {
            schemaVersion = 3,
            generatedAtUtc = generatedAt,
            appVersion = ConfigManager.AppVersion,
            commit = Environment.GetEnvironmentVariable("NZBDAV_COMMIT_SHA"),
            migrations = new { main = mainMigration, metrics = metricsMigration },
            logs = new { count = logs.Entries.Count, logs.OldestSequence, logs.NewestSequence, capacity = logBuffer.Capacity },
            warnings = new
            {
                count = warnings.Entries.Count,
                warnings.OldestSequence,
                warnings.NewestSequence,
                capacity = warningLogBuffer.Sink.Capacity,
            },
            streamTraces = new
            {
                capacity = traceSnapshot.Status.Capacity,
                eventCount = traceSnapshot.Status.EventCount,
                retainedEventCount = traceSnapshot.Status.RetainedEventCount,
                overwrittenEventCount = traceSnapshot.Status.OverwrittenEventCount,
                overflowed = traceSnapshot.Status.Overflowed,
                oldestRetainedSequence = traceSnapshot.Status.OldestRetainedSequence,
                newestRetainedSequence = traceSnapshot.Status.NewestRetainedSequence,
                oldestRetainedAtUnixMs = traceSnapshot.Status.OldestRetainedAtUnixMs,
                newestRetainedAtUnixMs = traceSnapshot.Status.NewestRetainedAtUnixMs,
                sessionCount = traceSnapshot.Status.SessionCount,
                retainedSessionCount = traceSnapshot.RetainedSessionCount,
            },
            sections = sectionStatus,
            redaction = new { secrets = redactor.SecretsRedacted, ipAddresses = redactor.AddressesPseudonymized },
        };
    }

    private static string BuildOverflowNote(StreamTraceStatus status)
    {
        var total = status.EventCount;
        var retained = status.RetainedEventCount;
        var overwritten = status.OverwrittenEventCount;
        var pct = total > 0 ? 100.0 * overwritten / total : 0;
        var oldest = status.OldestRetainedAtUnixMs > 0
            ? DateTimeOffset.FromUnixTimeMilliseconds(status.OldestRetainedAtUnixMs).UtcDateTime
                .ToString("yyyy-MM-ddTHH:mm:ssZ")
            : "unknown";
        var newest = status.NewestRetainedAtUnixMs > 0
            ? DateTimeOffset.FromUnixTimeMilliseconds(status.NewestRetainedAtUnixMs).UtcDateTime
                .ToString("yyyy-MM-ddTHH:mm:ssZ")
            : "unknown";

        return
            $"""
            Stream trace capture is INCOMPLETE.

            {total:n0} events were recorded but the ring buffer holds {status.Capacity:n0}, so {overwritten:n0} ({pct:0.0}%) were
            overwritten. Retained window: {oldest} to {newest}.

            Sessions listed in sessions.json can outlive their events, so a session with no events
            in events.jsonl was evicted, not idle. Re-run the reproduction with a larger capacity
            (Settings -> Support, or STREAM_TRACE_EVENTS) or a shorter test.
            """;
    }

    private static async Task WriteTraceEventsAsync(
        ZipArchive archive,
        string name,
        IReadOnlyList<StreamTraceEvent> events,
        SupportPackRedactor redactor,
        CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Fastest);
        await using var stream = entry.Open();
        await using var writer = new StreamWriter(stream, Encoding.UTF8);
        foreach (var evt in events)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var frozen = evt.FreezeForExport();
            var line = JsonSerializer.Serialize(frozen, CompactJsonOptions);
            await writer.WriteLineAsync(redactor.RedactText(line)).ConfigureAwait(false);
        }
    }

    private static async Task<(string? Main, string? Metrics)> ReadMigrationsAsync(CancellationToken cancellationToken)
    {
        async Task<string?> ReadAsync(DbContext db)
        {
            try
            {
                return (await db.Database.GetAppliedMigrationsAsync(cancellationToken).ConfigureAwait(false))
                    .LastOrDefault();
            }
            catch
            {
                return null;
            }
        }

        await using var main = new DavDatabaseContext();
        await using var metrics = new MetricsDbContext();
        return (await ReadAsync(main).ConfigureAwait(false), await ReadAsync(metrics).ConfigureAwait(false));
    }

    private static IEnumerable<string?> CollectSecrets(IEnumerable<ConfigDiagnosticSnapshot> config)
    {
        foreach (var item in config)
        {
            if (item.Value is null)
                continue;
            if (item.Key is ConfigKeys.ApiKey or ConfigKeys.ApiStrmKey or ConfigKeys.RclonePass
                or ConfigKeys.WebdavPass or ConfigKeys.WatchtowerProfileToken)
            {
                yield return item.Value;
                continue;
            }

            if (item.Key is not (ConfigKeys.UsenetProviders or ConfigKeys.ArrInstances
                or ConfigKeys.IndexersInstances or ConfigKeys.ProfilesInstances))
                continue;

            List<string>? structuredSecrets = null;
            try
            {
                using var document = JsonDocument.Parse(item.Value);
                structuredSecrets = CollectJsonSecrets(document.RootElement).ToList();
            }
            catch (JsonException)
            {
                // The structured value will be omitted by the redactor.
            }

            if (structuredSecrets is not null)
                foreach (var secret in structuredSecrets)
                    yield return secret;
        }

        yield return Environment.GetEnvironmentVariable("FRONTEND_BACKEND_API_KEY");
        yield return Environment.GetEnvironmentVariable("WEBDAV_PASSWORD");
        yield return Environment.GetEnvironmentVariable("SESSION_KEY");
    }

    private static IEnumerable<string> CollectJsonSecrets(JsonElement element, string? propertyName = null)
    {
        var normalized = propertyName?.Replace("-", "", StringComparison.Ordinal)
            .Replace("_", "", StringComparison.Ordinal)
            .ToLowerInvariant();
        if (normalized is "apikey" or "pass" or "password" or "token")
        {
            if (element.ValueKind == JsonValueKind.String)
                yield return element.GetString()!;
            yield break;
        }

        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
                foreach (var secret in CollectJsonSecrets(property.Value, property.Name))
                    yield return secret;
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
                foreach (var secret in CollectJsonSecrets(item))
                    yield return secret;
        }
    }

    private static async Task WriteJsonAsync(
        ZipArchive archive,
        string name,
        object value,
        SupportPackRedactor redactor,
        CancellationToken cancellationToken) =>
        await WriteTextAsync(
            archive,
            name,
            redactor.RedactText(JsonSerializer.Serialize(value, JsonOptions)),
            cancellationToken).ConfigureAwait(false);

    private static async Task WriteTextAsync(
        ZipArchive archive,
        string name,
        string content,
        CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Fastest);
        await using var stream = entry.Open();
        var bytes = Encoding.UTF8.GetBytes(content);
        await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
    }

    private static long? FileSize(string path) =>
        File.Exists(path) ? new FileInfo(path).Length : null;

    /// <summary>
    /// Real uptime of the backend process. This used to be measured from this service's
    /// own construction, but DI creates it lazily on the first support-pack download, so a
    /// backend running for hours reported a few seconds and made the log buffer's
    /// timestamps look impossible. Process start time is absolute, so reading it on demand
    /// stays accurate. Note that Environment.TickCount64 is unusable here: inside a
    /// container it reports the host's uptime, not this process's.
    /// </summary>
    private static TimeSpan ProcessUptime()
    {
        try
        {
            using var process = Process.GetCurrentProcess();
            var uptime = DateTimeOffset.UtcNow - process.StartTime.ToUniversalTime();
            if (uptime > TimeSpan.Zero) return uptime;
        }
        catch (Exception e)
        {
            Log.Debug(e, "Support pack: could not read the process start time");
        }

        return TimeSpan.Zero;
    }

    private static string FormatLogs(IEnumerable<LogEntry> entries)
    {
        var builder = new StringBuilder();
        foreach (var entry in entries)
        {
            builder.Append('[')
                .Append(DateTimeOffset.FromUnixTimeMilliseconds(entry.TimestampUnixMs)
                    .ToString("yyyy-MM-dd HH:mm:ss.fff"))
                .Append("] [").Append(entry.Level).Append(']');
            if (entry.Source is not null)
                builder.Append(" [").Append(entry.Source).Append(']');
            builder.Append(' ').AppendLine(entry.Message);
            if (entry.Exception is not null)
                builder.AppendLine(entry.Exception);
        }
        return builder.ToString();
    }
}
