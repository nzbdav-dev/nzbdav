# Migration paths

NzbDAV is designed as a **drop-in upgrade** from the NZB/WebDAV streaming lineage that started with [nzbdav-dev/nzbdav](https://github.com/nzbdav-dev/nzbdav). Migrations keep your `/config` volume (SQLite settings, queue/history metadata, blobs) and swap the container image.

!!! warning "Back up first"

    Always snapshot the host directory mapped to `/config` before changing images. Schema migrations apply automatically on startup and some are irreversible — see [Backups and upgrades](../guides/backups-upgrades.md).

## Official: nzbdav-dev/nzbdav `v0.6.4`

**Supported path.** NzbDAV targets a direct upgrade from [nzbdav-dev/nzbdav](https://github.com/nzbdav-dev/nzbdav) **v0.6.4**.

1. Stop the old container.
2. Back up `./config` (or whatever you mount at `/config`).
3. Point Compose (or `docker run`) at `ghcr.io/nzbdav/nzbdav:latest` (or a pinned release tag). Keep the same `/config` volume, `PUID`/`PGID`, and media path mounts.
4. Start the new container and wait for database maintenance / health to pass.
5. Confirm **Settings** (Usenet, WebDAV, SABnzbd API key), then spot-check queue/history and a WebDAV playback.

*Arr download-client settings usually stay the same (host, port `3000`, API key) if networking is unchanged.

Older than `0.6.0` installs may hit larger schema migrations — take a full `/config` backup and prefer stepping through a known-good intermediate if you are far behind.

## Community-reported: other forks

Operators have also **successfully migrated** to NzbDAV from these forks by the same `/config` volume swap. These are **community-validated**, not a guarantee for every divergent commit on those repos:

| Source | Notes |
|--------|--------|
| [Pukabyte/nzbdav](https://github.com/Pukabyte/nzbdav) | Fork of nzbdav-dev; same WebDAV + SAB shape. Back up `/config`, switch image to `ghcr.io/nzbdav/nzbdav`, verify settings after migrate. |
| [qooode/nzbdavex](https://github.com/qooode/nzbdavex) (NzbDavEx) | Extended fork (Watchdog, multi-provider, indexers, search profiles, etc.). Many features overlap with NzbDAV; after migrate, re-check Settings for providers, indexers, profiles, and Watchdog/Watchtower equivalents rather than assuming every UI field maps 1:1. |

### Suggested steps (fork → NzbDAV)

1. Export or copy a cold backup of `/config` from the old stack.
2. Note WebDAV credentials, SAB API key, Base URL, rclone mount paths, and *Arr client settings.
3. Replace the image with `ghcr.io/nzbdav/nzbdav` (same volume mounts).
4. On first boot, watch logs for migration progress; do not interrupt mid-migration.
5. Walk **Settings** tabs and confirm providers, WebDAV, SABnzbd, *Arr, and import strategy.
6. Test one known-good release (queue → mount → play) before relying on the library.

If something fails, restore the `/config` backup and reopen the previous image — then [file an issue](https://github.com/nzbdav/nzbdav/issues) with versions and migration log excerpts (redact secrets).

## What usually carries over

- SQLite config / queue / history / mounted WebDAV tree metadata
- NZB blobs under `{CONFIG_PATH}/blobs/` (keep this directory with the DB)
- Categories, API key, and WebDAV user (re-enter password if prompted)

## What to re-verify

- Image tags and healthchecks in Compose
- Reverse proxy / `TRUST_PROXY` / `SECURE_COOKIES` / Base URL
- Rclone sidecar URL and WebDAV password
- New NzbDAV-only features you want (Watchtower, Warden, pipelining) — enable deliberately after a stable migrate

## Related

- [Migrate from Altmount](../guides/altmount-migration.md) [since 0.9.0](https://github.com/nzbdav/nzbdav/releases/tag/v0.9.0){ .nzbdav-since } · [Docker](docker.md) · [First run](first-run.md) · [Compare alternatives](../guides/compare.md) · [About](../community/about.md)
