# Repairs

Background health monitoring and replacement of unhealthy library items.

!!! tip "Headless ENV"

    Map config keys below to `NZBDAV_CONFIG__...` with the
    [naming algorithm](headless.md#naming-algorithm)
    (`repair.enable` → `NZBDAV_CONFIG__REPAIR__ENABLE`). Enabling repairs via ENV
    also needs `media.library-dir` and *Arr instances.

| Control | Config key | Default | Effect |
|---------|------------|---------|--------|
| Enable Background Repairs | `repair.enable` | off | Needs library dir + *Arr |
| Health Check Concurrency [since 0.9.0](https://github.com/nzbdav/nzbdav/releases/tag/v0.9.0){ .nzbdav-since } | `repair.healthcheck-concurrency` | `50` | Worker ceiling for concurrent STAT checks; capped by the provider pool. Actual contention with playback is governed by provider-pool admission and **Streaming Priority** |
| Health Check Depth | `repair.healthcheck-depth` | `standard` | standard / enhanced / deep / complete |
| Check older releases less thoroughly [since 0.8.0](https://github.com/nzbdav/nzbdav/releases/tag/v0.8.0){ .nzbdav-since } | `repair.healthcheck-aging` | off | Aging taper |
| Repair After Streaming Failures | `repair.auto-remove-after-failures` | `0` | Consecutive streaming failures before urgent repair; `0` = immediate repair |
| Auto-remove unlinked files only | `repair.auto-remove-unlinked-only` | on | At the threshold, linked items use *Arr remove-and-search instead of force-delete |
| Library Directory | `media.library-dir` | empty | Organized media path in container |

`repair.auto-remove-after-failures` applies only to streaming-triggered failures such as missing
articles and corrupt archives. With a value greater than `0`, NzbDAV waits for that many
consecutive failures before it starts an urgent repair. At the threshold, linked library items
trigger *Arr remove-and-search when **Auto-remove unlinked files only** is enabled; unlinked files
are removed. Disable that option to force-delete linked items at the threshold.

Successful full-file playback and a successful background health check reset the in-memory failure
count. The count resets when NzbDAV restarts, so it is intentionally not a durable replacement for
health checks.

[Health and repairs](../operations/health-repairs.md)
