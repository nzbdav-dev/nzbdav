# Migrate from Altmount [since 0.9.0](https://github.com/nzbdav/nzbdav/releases/tag/v0.9.0){ .nzbdav-since }

The guided migration under **Settings → Altmount Migration** imports an existing Altmount library by rebuilding each release's NZB and submitting it through NzbDAV's normal queue. It does not modify Altmount metadata or store files.

Both legacy raw-protobuf metadata and Altmount v3-prefixed `.meta` formats are supported, including v3 stores. The wizard follows each file's recorded `store_ref`; **Altmount Store Root** can remap its `.nzbs/...` suffix when the library was copied from another host or mounted at a different container path.

WARNING: "Back up and keep Altmount data available"


    Back up both applications before beginning. Mount the Altmount metadata, config, and store paths read-only when possible, and keep the old service available until migrated releases play correctly from NzbDAV. The optional symlink step needs write access to the media library and a writable backup directory.

## Before you start
Note: Altmount does NOT have to be running, but can be, for this process to run.
- Configure and test the Usenet providers NzbDAV will use.
- Create the destination NzbDAV categories expected by Radarr and Sonarr.
- Mount the Altmount paths into the NzbDAV container. Values entered in the wizard are **container paths**, not host paths.
- Locate the metadata tree containing `.meta` files and the store tree containing `.nzbs/*.nzbz` files.
- Keep Altmount's `config.yaml` available if you want its SABnzbd category list discovered automatically.

## Run the wizard

1. **Connect** — enter the metadata root. Add `config.yaml` when available and the store root when recorded store paths are not directly readable. **Submit Workers** controls how many imports may cross the queue boundary concurrently; keep it at `1` unless you have a specific reason to increase it. **Max Queue Depth** limits how many imports NzbDAV queues at once.
2. **Categories** — map every discovered Altmount category to an existing NzbDAV category, or exclude it.
3. **Scan** — NzbDAV groups metadata by store, verifies the referenced `.nzbz` data, estimates fetch cost, and checks whether the release is already present.
4. **Review** — inspect red/amber findings, exclusions, filename changes, and collisions. Blocking collisions must be resolved before the run can start.
5. **Run** — the wizard reconstructs NZBs and submits them through NzbDAV's normal import pipeline. Progress survives restarts. Pause or cancel stops before the next submission; an individual submission already crossing the queue boundary is allowed to finish and is recorded safely.
6. **Links (optional)** — build a dry-run plan for library symlinks that still target Altmount. Review every status before applying. NzbDAV writes a restore archive first, changes symlinks only, and leaves real files, unmatched links, and drifted links untouched.

The wizard can be reset after work reaches a non-active state. Resetting clears the current scan and plan but retains completed migration mappings so later symlink scans can identify releases imported in earlier runs.

## Symlink safety and restore

Set **Library Root** to the media library containing the links and **Backup Directory** to a separate writable location. Apply and restore are confined to the Library Root and reject symlinked or reparse-point parent directories. If a link target changes after planning, the drift guard leaves it untouched.

The dry-run plan classifies every symlink found during the library walk:

| Status | Meaning |
|--------|---------|
| `rewrite` | Points to Altmount and has a verified NzbDAV replacement. |
| `orphan` | Points to Altmount, but no safe NzbDAV match was found. |
| `unreadable` | The link was found, but its target could not be read or classified. It remains unchanged and may still point at Altmount. Review directory permissions and filenames that are not valid UTF-8, and check whether another process changed the entry during the scan. Applying other rewrites requires explicit acknowledgement of these gaps. |
| `already-nzbdav` | Already points to NzbDAV and needs no change. |
| `not-altmount` | Does not correlate to scanned Altmount content and is left unchanged. |
| `applied` | Successfully repointed to NzbDAV. |
| `failed` | A verified rewrite was attempted but could not be completed. |

Use **Restore previous rewrite** to select a generated archive. Restore verifies that each link still points to the replacement recorded by that archive before restoring its original target.

## Troubleshooting

| Symptom | Check |
|---------|-------|
| No categories discovered | Supply Altmount's `config.yaml`, or continue to Scan so categories can be discovered from stores. |
| `store_missing` | Mount the `.nzbs` tree and set **Altmount Store Root** to the directory that contains `.nzbs`. |
| A release is already migrated | It is not resubmitted; retained provenance can still be used for symlink matching. |
| Start migration is disabled | Resolve blocking collisions, map or exclude every category, and successfully refresh the review tables. |
| Reset is disabled | Cancel active work and wait for the session to reach a non-active state. A paused run is still active. |
| A symlink is `orphan`, `unreadable`, or `failed` | Review its match/target details. The wizard will not guess or overwrite a link that cannot be verified safely. |

## Related

[Migration paths](../getting-started/migration.md) · [Backups and upgrades](backups-upgrades.md) · [SABnzbd settings](../configuration/sabnzbd.md)
