# WebDAV

WebDAV authentication and streaming/connection behavior for playback mounts.

!!! tip "Headless ENV"

    Map config keys below to `NZBDAV_CONFIG__...` with the
    [naming algorithm](headless.md#naming-algorithm)
    (`webdav.pass` → `NZBDAV_CONFIG__WEBDAV__PASS`).

| Control | Config key | Default | Effect |
|---------|------------|---------|--------|
| WebDAV User | `webdav.user` | `admin` / `WEBDAV_USER` | Alphanumeric + `_` `-` |
| WebDAV Password | `webdav.pass` | env `WEBDAV_PASSWORD` | Required for rclone/clients |
| Queue Download Connections | `usenet.max-queue-connections` | blank = all | Cap queue NNTP use |
| Queue connection preset [since 0.9.1](https://github.com/nzbdav/nzbdav/releases/tag/v0.9.1){ .nzbdav-since } | `usenet.max-queue-connections-preset` | blank = all | `low`/`medium`/`high`/`max` = 25/50/75/100% of the pooled total, so one value fits any provider count. Ignored when the absolute setting above is set |
| Concurrent Queue Downloads [since 0.9.0](https://github.com/nzbdav/nzbdav/releases/tag/v0.9.0){ .nzbdav-since } | `queue.worker-count` | `1` | Concurrent NZB imports (1–4); the oldest item is preferred while other workers share the connection budget |
| Enable Segment Cache | `usenet.segment-cache.enabled` | off | Disk cache; **restart required** |
| Cache path | `usenet.segment-cache.path` | `/config/segment-cache` | |
| Maximum size (GB) | `usenet.segment-cache.max-gb` | `10` | |
| Max Download Connections | `usenet.max-download-connections` | `0` (auto = pool) | Streaming budget |
| Apply limit per stream | `usenet.max-download-connections-per-stream` | off | Per-stream budget |
| Per-stream performance | `usenet.max-download-connections-per-stream-preset` | `high` | low/medium/high/max |
| Streaming Priority (vs Queue) [since 0.9.0](https://github.com/nzbdav/nzbdav/releases/tag/v0.9.0){ .nzbdav-since } | `usenet.streaming-priority` | `80` | High vs Low admission odds at each saturated provider connection pool (playback High, queue/health Low). Spare capacity is never held idle for High |
| Streaming Segment Timeout | `usenet.streaming-segment-timeout-seconds` | `8` | 2–40s |
| Streaming Read Timeout [since 0.9.0](https://github.com/nzbdav/nzbdav/releases/tag/v0.9.0){ .nzbdav-since } | `usenet.streaming-read-timeout-seconds` | `30` | 5–120s initial backend wait to open a GET/range (semaphore + pool + first segment). Cleared once body bytes flow; mid-stream stalls use the per-segment timeout. Unstarted responses return **503** with `Retry-After` — rclone/FUSE may still show client-side errors |
| Streaming Segment Retries | `usenet.streaming-segment-retries` | `3` | 0–5 |
| Article Buffer Size | `usenet.article-buffer-size` | `40` | Articles buffered ahead per stream (count bound) |
| In-flight article budget (MiB) [since 0.8.2](https://github.com/nzbdav/nzbdav/releases/tag/v0.8.2){ .nzbdav-since } | `usenet.in-flight-article-budget-mb` | `512` | Host-wide cap on decoded article bytes in RAM (64–8192); distinct from per-stream article buffer count |
| Idle connection timeout | `usenet.idle-connection-timeout-seconds` | `60` | 15–300; pool rebuild/restart |
| Pipelined article downloads | `usenet.pipelined-body-requests` | on | WebDAV BODY batches |
| Enforce Read-Only | `webdav.enforce-readonly` | on | `/content` readonly |
| Show hidden files | `webdav.show-hidden-files` | off | Dot-prefixed names in Explore |
| Preview par2 files | `webdav.preview-par2-files` | off | Render as text |
| Sanitize paths for Windows | `webdav.windows-safe-paths` | on | New mounts only |

!!! tip "Speed tuning"

    Raise **Max Download Connections** until throughput plateaus without pegging CPU. Baseline with a host speed test, then time a `/view` download from inside the container against the backend.

## Article Buffer Size and adaptive prefetch [since 0.9.0](https://github.com/nzbdav/nzbdav/releases/tag/v0.9.0){ .nzbdav-since }

`usenet.article-buffer-size` bounds how many decoded articles a stream may keep ahead of the consumer — and therefore per-stream memory — not how many provider connections playback may use.

When **Pipelined article downloads** is on, WebDAV BODY requests start in batches of up to four articles on one connection. If playback starves waiting for the next segment, NzbDAV automatically narrows that batch width (`4 → 2 → 1`) so more connections can work in parallel at the same buffer depth. When the consumer stays ahead, batch width recovers gradually. Connection and host-wide byte budgets still apply.

## Capturing a buffering support pack

Playback stalls are only diagnosable if the evidence is still in the log buffer when you collect the pack:

1. Set `LOG_LEVEL=INFO`. At `DEBUG`, routine background activity can fill the whole buffer within hours and evict the streaming events. `logs/warnings.log` in the pack keeps the last 500 warnings and errors regardless, so check it first.
2. Enable **Developer stream tracing** on **Settings → Support** for 15–60 minutes and pick a capacity that covers the whole reproduction (default 100,000 events; or set `STREAM_TRACE_EVENTS=100000` and restart). After downloading the pack, check `manifest.json → streamTraces.overflowed` — if true, increase capacity and reproduce again.
3. Reproduce the stall — play the file from Explore so the read goes straight to `/view`, skipping rclone and your media server.
4. Download the pack from **Settings → Support** immediately afterwards, while tracing is still on. The buffer is in-memory and is cleared on restart.

See [Logs and crash dumps](../operations/logs-crash-dumps.md).

[Streaming](../features/streaming-seeking.md) · [NNTP pipelining](../features/nntp-pipelining.md)
