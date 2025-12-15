# SQLite Maintenance & Payload Compression PR Summary

## Overview

This PR addresses runaway growth of the embedded SQLite database (observed at 20 GB+) by:

- enforcing SQLite pragmas that keep freelists under control (WAL, synchronous=NORMAL, auto_vacuum=FULL);
- rewriting large JSON payloads (NZB XML, segment lists, multipart metadata) as compressed base64 strings to reduce footprint without schema changes; and
- introducing scheduled retention + vacuum passes so log-style tables do not grow indefinitely.

All changes are additive and backwards compatible with upstream releases. Existing rows remain readable; newly written rows still use the same schema (TEXT columns) even though their content is compressed.

## Key Changes

1. **Compression utilities** (`backend/Utils/CompressionUtil.cs`)
   - Provides Brotli-based helpers used by EF value converters.
2. **Compressed payload columns** (`backend/Database/DavDatabaseContext.cs`)
   - Queue NZB XML, `DavNzbFile.SegmentIds`, `DavRarFile.RarParts`, and `DavMultipartFile.Metadata` now round-trip through compressors.
   - Converters auto-detect legacy plain-text payloads, so no migration is required.
3. **SQLite connection & maintenance hooks**
   - `SqliteForeignKeyEnabler` now enables WAL + synchronous NORMAL per connection.
   - New `DatabaseMaintenance` module ensures `auto_vacuum=FULL`, provides retention routines, runs one-time payload rewrites, and issues VACUUM when needed.
4. **Hosted maintenance service** (`backend/Services/DatabaseMaintenanceService.cs`)
   - Registered in `Program.cs` to run every 6 hours (configurable). Cleans up data and triggers compaction in the background.
5. **Config & knobs** (`backend/Config/ConfigManager.cs`, `backend/Database/Models/ConfigItem.cs`)
   - Adds keys/env overrides for history retention, health-check retention, and maintenance interval:
     - `DATABASE_HISTORY_RETENTION_DAYS` / `database.history-retention-days` (default 90)
     - `DATABASE_HEALTHCHECK_RETENTION_DAYS` / `database.healthcheck-retention-days` (default 30)
     - `DATABASE_MAINTENANCE_INTERVAL_HOURS`
6. **Manual compaction CLI** (`backend/Program.cs`, `backend/Database/DatabaseMaintenance.cs`)
   - Adds `--compact-db` (with optional `--vacuum-into /path`) to run payload rewrite, retention, and VACUUM on demand, optionally writing to another volume via `VACUUM INTO`.
7. **Documentation** (`README.md`)
   - Explains the automated maintenance loop, compression behavior, and the new environment variables.

## Operational Notes

- First boot on an existing large DB will:
  - switch the file to `auto_vacuum=FULL` and run `VACUUM` once (requires temp disk roughly equal to current DB size);
  - rewrite payload columns in batches, followed by another VACUUM; and
  - prune history/health rows older than the configured retention window (set the values to `0` to keep everything).
- All SQLite work happens under the configured `/config` directory. Operators with limited space can temporarily relocate `CONFIG_PATH` to a larger volume, run the fork once, and then move the shrunken `db.sqlite` back.
- `.devconfig/` is only a local testing folder to satisfy `CONFIG_PATH`; it should not be committed.

## Testing

- `CONFIG_PATH=.devconfig dotnet build backend/NzbWebDAV.csproj`
- `DOTNET_ROLL_FORWARD=LatestMajor CONFIG_PATH=.devconfig dotnet ef migrations list --project backend/NzbWebDAV.csproj --startup-project backend/NzbWebDAV.csproj`
  - Lists all historical migrations; no schema updates were required.

## Backwards Compatibility

- Schema is unchanged; upstream binaries can still read/write the same database even if some payloads are now compressed (they simply see base64 text within the same columns).
- Retention deletes only SAB history and health-check logs. Actual WebDAV content (`DavItems`, `DavNzbFiles`, etc.) is untouched.
- Operators may opt out of retention by setting the relevant config values to `0`.
