# Technical support pack [since 0.9.0](https://github.com/nzbdav/nzbdav/releases/tag/v0.9.0){ .nzbdav-since }

**Settings → Support** generates a ZIP you can provide to trusted NzbDAV support
when troubleshooting an issue. The archive is streamed to your browser and is
not retained by NzbDAV.

## Included

- Recent backend logs from the in-memory log buffer
- Redacted active Settings and runtime/build information
- Aggregate provider throughput, outage, failover, and consumption metrics
- Historical latency phase histograms (`metrics/recent.json` → `latency24Hours`)

Backend logs are memory-only and are cleared when NzbDAV restarts. Frontend and
container logs are not included.

## Latency phases [since 0.9.0](https://github.com/nzbdav/nzbdav/releases/tag/v0.9.0){ .nzbdav-since }

`latency24Hours` projects one-minute histograms into five-minute buckets for:

| Phase | Meaning |
|-------|---------|
| `response` | Successful NNTP response availability after a provider connection is acquired. Excludes body drain. |
| `pool-wait` | Wait to acquire a connection from the named provider pool. |
| `permit-wait` | Top-level workload connection-budget wait; no provider is selected yet. |

Percentiles are **bucket upper bounds**, not exact sample percentiles. Only
successful responses are counted — misses, errors, and cancellations stay in
existing status metrics. Body-drain time is never folded into `response`.

## Privacy

The pack redacts passwords, API keys, tokens, URL credentials, sensitive URL
parameters, authorization values, and IP addresses. It does **not** anonymize
file names, filesystem paths, account usernames, DNS names, or non-secret URL
paths. Review the ZIP before sharing it.

The pack never includes databases, database backups, NZBs, blobs, environment
files, session or API-key files, crash dumps, stream traces, or segment-cache
data.
