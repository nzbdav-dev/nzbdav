# Troubleshooting

## Container unhealthy / won't start

- Check `docker logs nzbdav` for migration or backend health failures.
- Ensure `CONFIG_PATH` (`/config`) is writable by `PUID`/`PGID`.
- Frontend `/healthz` should pass during long migrations; backend `/health` must eventually succeed.

## WebDAV or playback fails

- Confirm WebDAV username/password.
- Behind a proxy: TLS, `/ws` Upgrade, `SECURE_COOKIES`, Base URL / `TRUST_PROXY`.
- Overview **Active Reads**: unexpected traffic → rclone VFS or media-server scans.
- Try disabling segment cache or adjusting Max Download Connections — [WebDAV](../configuration/webdav.md).

## Playback slowed but nothing failed

When streams buffer without hard errors, read support-pack latency phases first
(`metrics/recent.json` → `latency24Hours`):

- High `response` with low `pool-wait` / `permit-wait` → provider/server latency.
- High provider `pool-wait` → that provider's connections are saturated or churning.
- High streaming/queue `permit-wait` → that workload's configured connection cap is saturated.
- High stream-trace `consumerWaitMs` with low values in all three phases → prefetch
  geometry or consumer pacing — compare with `bodyDrainMs` on RangeEnd events.

Generate a pack from **Settings → Support** — [Technical support pack](../configuration/support.md).

## *Arr won't import

- Paths must match exactly between NzbDAV completed path and *Arr containers.
- Symlinks: rclone mount healthy? `ls` shows `completed-symlinks` and `.ids`?
- STRM: Base URL reachable from Emby/Jellyfin?
- Check Automatic Queue Management rules — [Arrs](../configuration/arrs.md).

## 403 / 405 on MKCOL, PUT or DELETE

The mount is a **read-only** virtual filesystem — `/content`, `/completed-symlinks` and `/.ids`
serve data streamed from Usenet and accept no writes. Refused writes are expected, not a fault:

- `403 Forbidden` — a client tried to create, copy, move or upload something.
- `405 Method Not Allowed` — `MKCOL` targeted a directory that already exists.

Logs show one aggregated warning per read-only path every 5 minutes (`Refused to create item under
read-only path …`), with per-attempt detail at `LOG_LEVEL=debug`. NzbDAV cannot stop a client from
re-attempting, so fix it at the source — the warning and the access-log line both name the client IP
and User-Agent:

- Media servers (Emby/Jellyfin/Plex/Kodi): turn off saving metadata, artwork or `.nfo`/`.srt`
  sidecars **into media folders**, or scan your library rather than the NzbDAV mount.
- *Arr: disable metadata/extra-file writing for the affected root folder.
- rclone: mount with `--read-only` so it stops probing for writability.

## `addurl` SSRF / private indexer [since 0.8.0](https://github.com/nzbdav/nzbdav/releases/tag/v0.8.0){ .nzbdav-since }

Allow Docker DNS or LAN hosts under **Trusted local hosts** — [SABnzbd API](../features/sab-api.md).

## Why did files disappear?

See [Deletion audit](../operations/deletion-audit.md) — history retention ≠ deleting mounts; orphan cleanup and *Arr actions can remove content.

## Provider / missing articles

- Circuit breaker may pause a bad provider — check Usenet settings and Overview.
- Storage groups skip sibling resellers after a miss — only group identical upstream storage.
- Health/repairs can replace unhealthy library items — [Health and repairs](../operations/health-repairs.md).

## Still stuck

Generate a [technical support pack](../configuration/support.md) from **Settings → Support**,
review it for personal paths and names, then [open an issue](https://github.com/nzbdav/nzbdav/issues).
For local stream debugging, see [Contributing](../community/contributing.md).
