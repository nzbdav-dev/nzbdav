using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NzbWebDAV.Config;
using NzbWebDAV.Database;
using NzbWebDAV.Services.Metrics;

namespace NzbWebDAV.Api.Controllers.ClearOverviewStats;

[ApiController]
[Route("api/clear-overview-stats")]
public class ClearOverviewStatsController(
    ConfigManager configManager,
    MetricsWriter metricsWriter,
    ProviderBytesTracker bytesTracker,
    ProviderLatencyTracker latencyTracker
) : BaseApiController
{
    protected override async Task<IActionResult> HandleRequest()
    {
        var ct = HttpContext.RequestAborted;
        var request = new ClearOverviewStatsRequest(HttpContext);
        var providerKey = request.ProviderKey;

        // Validate the key against configured providers so a stale UI cannot
        // silently no-op (deleted providers require a full reset instead).
        var providerConfig = configManager.GetUsenetProviderConfig();
        if (providerKey != null && !providerConfig.Providers.Any(p =>
                p.ProviderId != Guid.Empty && UsenetProviderIdentity.MetricsKey(p) == providerKey))
            throw new BadHttpRequestException("Unknown provider");

        // 1. Snapshot usage before clearing the tracker so the data-cap gauge
        //    can be preserved after ResetCounters without double-counting.
        var usageSnapshot = OverviewStatsReset.SnapshotUsage(providerConfig, bytesTracker, providerKey);
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // 2. Pause flushes and abandon any in-flight drained batch, then drop
        //    queued rows so they cannot reappear after the wipe.
        await using var resetLease = await metricsWriter.BeginResetAsync(ct).ConfigureAwait(false);

        if (providerKey == null) metricsWriter.DiscardQueuedAndResetStats();
        else metricsWriter.DiscardQueuedForProvider(providerKey);

        // 3. Wipe metrics tables (all, or provider-keyed rows only).
        await using var db = new MetricsDbContext();
        var deletedRows = providerKey == null
            ? await OverviewStatsReset.WipeAsync(db, ct).ConfigureAwait(false)
            : await OverviewStatsReset.WipeProviderAsync(db, providerKey, ct).ConfigureAwait(false);

        // 4. Zero in-memory lifetime counters and pending minute buckets.
        if (providerKey == null)
        {
            bytesTracker.ResetCounters();
            latencyTracker.ResetCounters();
        }
        else
        {
            bytesTracker.ResetProvider(providerKey);
            latencyTracker.ResetProvider(providerKey);
        }

        // 5. Fold the pre-wipe usage snapshot into BytesUsedOffset and
        //    persist. Done after the wipe so SeedTrackerAsync cannot read
        //    stale ProviderHourly rows or double-count live tracker bytes.
        if (OverviewStatsReset.FoldUsageIntoOffsets(providerConfig, usageSnapshot, nowMs, providerKey))
            await UsenetProviderIdentity.SaveProvidersAsync(configManager, providerConfig, ct)
                .ConfigureAwait(false);

        // 6. Drop anything enqueued during the wipe window.
        if (providerKey == null) metricsWriter.DiscardQueuedAndResetStats();
        else metricsWriter.DiscardQueuedForProvider(providerKey);

        return Ok(new ClearOverviewStatsResponse { Status = true, DeletedRows = deletedRows });
    }
}
