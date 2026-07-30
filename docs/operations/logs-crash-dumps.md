# Logs and crash dumps

## Container logs

```bash
docker compose logs --tail=200 -f nzbdav
docker compose logs --tail=200 -f nzbdav_rclone
```

When a process exits, the entrypoint logs the exit code. Values above `128` encode a fatal signal (`128+N`) — e.g. `139` is SIGSEGV, `132` is SIGILL.

The admin UI also offers a live log viewer.

## Stream traces [since 0.10.0](https://github.com/nzbdav/nzbdav/releases/tag/v0.10.0){ .nzbdav-since }

Capture per-segment playback events while reproducing buffering or seek stalls:

1. Open **Settings → Support → Developer stream tracing**.
2. Choose 15, 30, or 60 minutes, pick a capacity (20,000–200,000 events; default 100,000), and turn tracing on. A warning banner appears while it is active.
3. Reproduce the issue (Explore `/view` playback is ideal because it skips rclone and your media server).
4. Turn tracing off when you are done, or let the timer elapse. The capture is **kept in memory** so you can still collect it.
5. Download a support pack from the same page — `stream-traces/` rides along whether tracing is still on or the capture is retained. Check `manifest.json → streamTraces` and, when the ring wrapped, `stream-traces/OVERFLOW.txt`.
6. Turning tracing on again resumes a retained capture (same capacity). Use **Discard captured traces** first only when you intentionally want a fresh capture at a different size.
7. Retained traces are released automatically an hour after tracing stops, or immediately via **Discard captured traces**. UI-enabled tracing always resets on restart, and nothing is written to disk outside the support pack.

### Incomplete captures [since 0.9.0](https://github.com/nzbdav/nzbdav/releases/tag/v0.9.0){ .nzbdav-since }

When recorded events exceed capacity, the ring keeps only the newest events. The Support UI and banner warn when the buffer is nearly full or has overflowed. Support packs set `sections.streamTraces` to `included-truncated` and include an `OVERFLOW.txt` note with retained/overwritten counts and the retained time window. Session summaries can outlive their events — check per-session `eventsComplete` in `sessions.json`.

Setting `STREAM_TRACE_EVENTS` still enables tracing from startup with no expiry, which is handy for local dev. Dump a single session with `./scripts/dump-stream-trace.sh` — see [Contributing](../community/contributing.md).

## .NET crash dumps (opt-in)

Minidumps are **off** by default (large; may contain article data). To capture on the next backend crash:

```yaml
environment:
  - DOTNET_DbgEnableMiniDump=1
  - DOTNET_DbgMiniDumpType=2
  - DOTNET_DbgMiniDumpName=/config/dump.%p
```

Analyze with `dotnet-dump`, then remove the env vars and dump files.

## Rclone mount failures

Verify `/dev/fuse`, sidecar start order, WebDAV credentials, and `user_allow_other` if `--allow-other` is rejected. RC Test Conn needs `--rc*` flags and matching host/user/pass.
