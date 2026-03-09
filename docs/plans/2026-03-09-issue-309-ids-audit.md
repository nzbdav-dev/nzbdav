## Issue #309 audit

Observed symptom:
- Reads through `/.ids/<prefix>/<guid>` fail for some mounted workflows with an `input/output error`.
- The same `DavItem` remains streamable through its normal content path.

Audited code paths:
- WebDAV `.ids` routing: `backend/WebDav/DatabaseStoreIdsCollection.cs`
- `.ids` leaf wrapper: `backend/WebDav/DatabaseStoreIdFile.cs`
- Symlink target generation: `backend/WebDav/DatabaseStoreSymlinkFile.cs`
- STRM target generation: `backend/Queue/PostProcessors/CreateStrmFilesPostProcessor.cs`
- STRM-to-symlink migration: `backend/Tasks/StrmToSymlinksTask.cs`

Key finding:
- The `.ids` path is the only file-serving path that intentionally drops the original filename and extension.
- Normal content paths expose the real filename, but `.ids` exposes a bare GUID.
- That changes file metadata seen by WebDAV clients:
  - `displayname`
  - MIME inference
  - any client behavior that keys off the leaf filename/extension

Why this branch changes `.ids` names:
- It preserves the original extension in generated `.ids` targets and `.ids` listings.
- It keeps old GUID-only `.ids` paths working by stripping an optional extension during lookup.
- This is the narrowest backward-compatible change that removes the most obvious `.ids`-specific divergence.

What is still not proven:
- The repository does not currently contain a reproducible automated test for the exact rclone-mounted failure mode from issue #309.
- `backend.Tests` also has unrelated compile failures on `main` in `ContentIndexRecoveryServiceTests`, which blocks full test execution for this branch.

Validation completed:
- `dotnet build backend --no-restore`
