# Environment variables

Advanced reference for **process / container** wiring and **legacy Settings fallbacks**. Most day-to-day tunables live in the Settings UI (SQLite).

!!! tip "Authoritative headless Settings [since 0.9.0](https://github.com/nzbdav/nzbdav/releases/tag/v0.9.0){ .nzbdav-since }"

    To drive every Settings (`ConfigItems`) value from the environment — with read-only UI locks and values kept out of SQLite — use the **`NZBDAV_CONFIG__...`** overlay documented in **[Headless environment configuration](headless.md)**. That page includes a fully hydrated Compose example.

    Precedence when both are present: **`NZBDAV_CONFIG__...` > SQLite/UI > legacy fallbacks on this page > defaults**.

## Container / entrypoint

| Variable | Default | Effect |
|----------|---------|--------|
| `CONFIG_PATH` | `/config` | SQLite, blobs, backups, session key |
| `PUID` / `PGID` | `1000` | Container user/group for `/config` ownership |
| `TZ` | unset | Schedules and log timestamps |
| `BACKEND_URL` | `http://localhost:8080` | Frontend → backend (set by entrypoint if empty) |
| `FRONTEND_BACKEND_API_KEY` | random if unset | Shared API key; also seeds `api.key` when empty |
| `MAX_BACKEND_HEALTH_RETRIES` | `30` | Entrypoint health wait |
| `MAX_BACKEND_HEALTH_RETRY_DELAY` | `1` | Seconds between health probes |

## Frontend (Node)

| Variable | Default | Effect |
|----------|---------|--------|
| `PORT` | `3000` | HTTP listen port |
| `BACKEND_URL` | required in split deploys | Backend base URL |
| `FRONTEND_BACKEND_API_KEY` | required | Injected as `x-api-key` for authenticated proxy |
| `TRUST_PROXY` | off | `1`/`true`/`yes` — honor proxy forwarded headers |
| `SECURE_COOKIES` | unset | `true` for HTTPS-only UI (recommended behind TLS) |
| `SESSION_KEY` | file under `CONFIG_PATH` | Stable cookie signing secret |
| `SESSION_MAX_AGE` | ~1 year (seconds) | Session lifetime |
| `DISABLE_FRONTEND_AUTH` | `false` | `true` disables UI login (**dangerous**) |
| `LOG_LEVEL` | `info` (prod) | Frontend log verbosity |
| `VITE_ALLOWED_HOSTS` | unset | Dev/build host allowlist |
| `NZBDAV_VERSION` / `NZBDAV_COMMIT_SHA` | image build | Version display |

## Backend (.NET)

| Variable | Default | Effect |
|----------|---------|--------|
| `ASPNETCORE_URLS` | from hosting | Backend listen URLs |
| `CONFIG_PATH` | `/config` | Same as above |
| `LOG_LEVEL` | Information | Serilog minimum level |
| `LOG_BUFFER_SIZE` | `2000` | In-memory log buffer for UI (100–50000) |
| `STREAM_TRACE_EVENTS` | `0` (off) | Opt-in stream trace capacity, always-on with no expiry; the Settings → Support toggle can also set capacity (20k–200k) for timed captures |
| `TRUSTED_PROXY_CIDRS` | loopback | Comma-separated IPs/CIDRs trusted for forwarded headers |
| `DISABLE_WEBDAV_AUTH` | unset | Disables WebDAV auth (**dangerous**) |
| `USENET_DISABLE_CRC_VALIDATION` [since 0.8.0](https://github.com/nzbdav/nzbdav/releases/tag/v0.8.0){ .nzbdav-since } | unset | `1` skips yEnc CRC checks (emergency) |
| `THREADPOOL_MIN_THREADS` | `max(2×CPU, 50)` | Override min worker/IOCP threads |
| `THREADPOOL_MAX_THREADS` | `max(50×CPU, 1000)` | Override max threads |
| `MAX_REQUEST_BODY_SIZE` | 100 MiB | Max request body bytes |
| `NZBDAV_VERSION` | `0.0.0` | Reported app version |
| `DOTNET_DbgEnableMiniDump` | off | Opt-in crash dumps — [Logs](../operations/logs-crash-dumps.md) |

`PORT` and `ASPNETCORE_URLS` control listeners inside the container; they do not
change Docker's published host port. For normal Compose deployments, leave the
internal ports unchanged and use a `HOST_PORT:CONTAINER_PORT` mapping. See
[Change the published port](../getting-started/docker.md#change-the-published-port)
for bridge networking, host networking, healthcheck, and DUMB examples.

## Settings fallbacks (when UI empty)

These apply only when the matching Settings value is empty **and** no
[`NZBDAV_CONFIG__...`](headless.md) overlay supplies that key.

| Variable | Related setting | Default if both empty |
|----------|-----------------|------------------------|
| `FRONTEND_BACKEND_API_KEY` | API Key | required |
| `CATEGORIES` | Categories | `audio,software,tv,movies` |
| `NZB_GRAB_USER_AGENT` | User Agent / retrieve UA | `nzbdav/{version}` |
| `NZB_SEARCH_USER_AGENT` | Search User-Agent | `nzbdav/{version}` |
| `TRUSTED_INTERNAL_HOSTS` [since 0.8.0](https://github.com/nzbdav/nzbdav/releases/tag/v0.8.0){ .nzbdav-since } | Trusted local hosts | none |
| `MOUNT_DIR` | Rclone Mount Directory | `/mnt/nzbdav` |
| `WEBDAV_USER` | WebDAV User | `admin` |
| `WEBDAV_PASSWORD` | WebDAV Password | none (hashed when set) |
| `RESOLUTION_CACHE_TTL_HOURS` [since 0.8.0](https://github.com/nzbdav/nzbdav/releases/tag/v0.8.0){ .nzbdav-since } | Search link lifetime | `168` |
| `DATABASE_HISTORY_RETENTION_DAYS` | History retention | `90` |
| `DATABASE_HEALTHCHECK_RETENTION_DAYS` | Health-check retention | `30` |
| `DATABASE_MAINTENANCE_INTERVAL_HOURS` | Retention sweep cadence | `6` |

## Example Compose snippet

Operational wiring only — for a full Settings-via-ENV stack see the
[headless Compose example](headless.md#fully-hydrated-compose-example).

```yaml
environment:
  PUID: "1000"
  PGID: "1000"
  TZ: America/New_York
  TRUST_PROXY: "1"
  SECURE_COOKIES: "true"
  TRUSTED_INTERNAL_HOSTS: "prowlarr"
```

!!! danger "Security"

    Never enable `DISABLE_FRONTEND_AUTH` or `DISABLE_WEBDAV_AUTH` on a network-exposed instance. Prefer TLS + strong WebDAV passwords + `SECURE_COOKIES=true`.
